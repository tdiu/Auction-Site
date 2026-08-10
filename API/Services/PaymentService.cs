using API.Core;
using API.DTOs;
using API.Entities;
using API.Extensions;
using API.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using API.Services.Outbox;
using Stripe;
using Stripe.Checkout;

namespace API.Services;

public class PaymentService(IUnitOfWork unitOfWork, IConfiguration configuration, StripeClient stripeClient, ILogger<PaymentService> logger) : IPaymentService
{
    public async Task<Result<CreatePaymentResponseDto>> CreateCheckoutSession(int auctionId, string userId)
    {
        var auction = await unitOfWork.Auctions.GetAuctionAsync(auctionId);
        var currTime = DateTimeOffset.UtcNow;

        if (auction == null)
            return Result<CreatePaymentResponseDto>.Failure("Cannot find auction", FailureReason.NotFound);
        if (auction.EndTime > currTime || auction.CurrentHighBid == null)
            return Result<CreatePaymentResponseDto>.Failure("Auction in progress or no bidder", FailureReason.Conflict);
        if (auction.CurrentHighBidderId != userId)
            return Result<CreatePaymentResponseDto>.Failure("Invalid user", FailureReason.Forbidden);

        var winner = await unitOfWork.Users.GetUserByIdAsync(userId);
        var payment = await unitOfWork.Payments.GetByAuctionIdAsync(auctionId);

        // Payment already settled. Refuse before creating anything.
        if (payment is { Status: PaymentStatus.Paid })
            return Result<CreatePaymentResponseDto>.Failure("Payment already completed", FailureReason.Conflict);

        var payWindow = configuration.GetValue("Payments:PayWindow", TimeSpan.FromDays(7));
        var sessionTtl = configuration.GetValue("Payments:SessionTtl", TimeSpan.FromMinutes(60));
        var deadline = (auction.FinalizedAt ?? auction.EndTime) + payWindow;
        if (currTime > deadline)
            return Result<CreatePaymentResponseDto>.Failure("Payment window has closed", FailureReason.Conflict);

        // Reuse a live session instead of creating a new one
        if (payment != null)
        {
            var reuse = await TryReuseOpenSessionAsync(payment, currTime);
            if (reuse != null)
                return reuse;
        }

        if (payment == null)
        {
            payment = new Payment
            {
                AuctionId = auctionId,
                UserId = userId,
                Amount = auction.CurrentHighBid.Value,
                Status = PaymentStatus.Pending,
                CreatedAt = currTime,
                PayableUntil = deadline
            };

            unitOfWork.Payments.Add(payment);
            try
            {
                await unitOfWork.CompleteAsync();
            }
            catch (DbUpdateException e) when (e.IsUniqueViolation())
            {
                // Concurrent request created it first. Drop our failed insert (still tracked as
                // Added) so the next save won't retry it, then re-read and continue.
                unitOfWork.Payments.Detach(payment);
                payment = await unitOfWork.Payments.GetByAuctionIdAsync(auctionId);
                if (payment == null)
                    return Result<CreatePaymentResponseDto>.Failure("Could not create payment", FailureReason.InternalError);
                if (payment is { Status: PaymentStatus.Paid })
                    return Result<CreatePaymentResponseDto>.Failure("Payment already completed", FailureReason.Conflict);

                var raced = await TryReuseOpenSessionAsync(payment, currTime);
                if (raced != null)
                    return raced;
            }
        }

        // Create the Stripe session BEFORE persisting the attempt. A Pending attempt then always
        // carries a session id. Never leaves a Pending/null-session row that the reuse path
        // can't recover and the Pending unique index would block forever.
        var clientUrl = configuration["ClientAppUrl"];

        var options = new SessionCreateOptions
        {
            Mode = "payment",
            ExpiresAt = (currTime + sessionTtl).UtcDateTime,
            CustomerEmail = string.IsNullOrEmpty(winner?.Email) ? null : winner.Email,
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "cad",
                        UnitAmount = (long)Math.Round(payment.Amount * 100m),
                        ProductData = new SessionLineItemPriceDataProductDataOptions { Name = auction.ItemName }
                    }
                }
            ],
            SuccessUrl = $"{clientUrl}/orders/{payment.PaymentId}?session_id={{CHECKOUT_SESSION_ID}}",
            CancelUrl = $"{clientUrl}/auctions/{auctionId}?cancelled=true",
            // Correlate attemptId on payment. Both fields are write-only
            // (the webhook looks the attempt up by StripeSessionId), so this is traceability only.
            ClientReferenceId = payment.PaymentId.ToString(),
            Metadata = new Dictionary<string, string>
            {
                ["payment_id"] = payment.PaymentId.ToString(),
                ["auction_id"] = auctionId.ToString()
            },
        };

        Session session;
        try
        {
            // Guid idempotency key replaces the old attempt-{id} one: it protects a transport-level
            // retry of THIS create call without needing a persisted identity first.
            session = await stripeClient.V1.Checkout.Sessions.CreateAsync(options,
                new RequestOptions { IdempotencyKey = Guid.NewGuid().ToString() });
        }
        catch (StripeException e)
        {
            // Nothing persisted yet, so there is no attempt to mark Failed
            logger.LogWarning(e, "Stripe session creation failed for payment {PaymentId}", payment.PaymentId);
            return Result<CreatePaymentResponseDto>.Failure(e.Message, FailureReason.InternalError);
        }

        var attempt = new PaymentAttempt
        {
            PaymentId = payment.PaymentId,
            Amount = payment.Amount,
            Status = PaymentAttemptStatus.Pending,
            StripeSessionId = session.Id,
            CreatedAt = currTime
        };
        payment.Attempts.Add(attempt);

        try
        {
            await unitOfWork.CompleteAsync();
        }
        catch (DbUpdateException e) when (e.IsUniqueViolation())
        {
            // Lost the race for the single open attempt. The session we just created is now an
            // orphan; it self-expires via ExpiresAt and its expiry webhook finds no attempt. Reuse theirs.
            payment.Attempts.Remove(attempt);
            unitOfWork.Payments.Detach(attempt);
            payment = await unitOfWork.Payments.GetByAuctionIdAsync(auctionId)
                      ?? throw new InvalidOperationException($"Payment for auction {auctionId} could not be found");
            return await TryReuseOpenSessionAsync(payment, currTime)
                ?? Result<CreatePaymentResponseDto>.Failure("Could not start checkout", FailureReason.InternalError);
        }

        return Result<CreatePaymentResponseDto>.Success(new CreatePaymentResponseDto { CheckoutUrl = session.Url });
    }

    public async Task<Result<PaymentStatusDto>> GetPaymentStatus(int auctionId, string userId)
    {
        var payment = await unitOfWork.Payments.GetByAuctionIdAsync(auctionId);
        if (payment == null)
            return Result<PaymentStatusDto>.Failure("No payment found", FailureReason.NotFound);
        if (payment.UserId != userId)
            return Result<PaymentStatusDto>.Failure("Invalid user", FailureReason.Forbidden);

        return Result<PaymentStatusDto>.Success(new PaymentStatusDto
        {
            Status = payment.ToEffectiveStatus(),
            Amount = payment.Amount,
            CompletedAt = payment.CompletedAt
        });
    }

    public async Task<Result<OrderDto>> GetOrder(int orderId, string userId)
    {
        var payment = await unitOfWork.Payments.GetByIdAsync(orderId);
        if (payment == null || payment.UserId != userId)
            return Result<OrderDto>.Failure("No order found", FailureReason.NotFound);

        return Result<OrderDto>.Success(payment.ToOrderDto());
    }

    public async Task<IReadOnlyList<OrderDto>> GetOrdersForUser(string userId)
    {
        var payments = await unitOfWork.Payments.GetForUserAsync(userId);
        return payments.Select(p => p.ToOrderDto()).ToList();
    }

    public async Task HandleWebhook(string json, string stripeSignature)
    {
        var secret = configuration["Stripe:WebhookSecret"] ?? throw new InvalidOperationException("Missing webhook secret");
        var stripeEvent = EventUtility.ConstructEvent(json, stripeSignature, secret);

        if (stripeEvent.Data.Object is not Session session)
        {
            logger.LogDebug("Ignoring unhandled Stripe event type {EventType}", stripeEvent.Type);
            return;
        }

        switch (stripeEvent.Type)
        {
            case EventTypes.CheckoutSessionCompleted:
                {
                    var attempt = await unitOfWork.Payments.GetAttemptByStripeSessionIdAsync(session.Id);
                    if (attempt == null) return;

                    // No-op for cards as they're paid at completion. Required for async methods
                    // where "complete" can arrive with funds still unconfirmed
                    if (session.PaymentStatus is not ("paid" or "no_payment_required"))
                        return;

                    var now = DateTimeOffset.UtcNow;
                    if (attempt.Status != PaymentAttemptStatus.Completed)
                    {
                        attempt.Status = PaymentAttemptStatus.Completed;
                        attempt.CompletedAt = now;

                        var auction = await unitOfWork.Auctions.GetAuctionAsync(attempt.Payment.AuctionId)
                                      ?? throw new InvalidOperationException(
                                          $"Auction {attempt.Payment.AuctionId} missing for completed payment");

                        // One payload, two rows: the seller's notification and the buyer's receipt
                        // retry independently, so a mail outage cannot dead-letter the notification.
                        var payload = JsonSerializer.Serialize(new PaymentCompletedPayload(
                            attempt.PaymentId, attempt.Payment.AuctionId,
                            attempt.Payment.UserId, auction.SellerId, auction.ItemName));

                        unitOfWork.Outbox.Add(new OutboxMessage
                        {
                            Type = "PaymentCompleted",
                            CreatedAt = now,
                            VisibleAt = now,
                            Payload = payload
                        });

                        unitOfWork.Outbox.Add(new OutboxMessage
                        {
                            Type = "PaymentReceipt",
                            CreatedAt = now,
                            VisibleAt = now,
                            Payload = payload
                        });
                    }
                    attempt.Payment.MarkPaid(now);

                    try
                    {
                        await unitOfWork.CompleteAsync();
                    }
                    catch (DbUpdateException e) when (e.IsUniqueViolation())
                    {
                        logger.LogWarning(e, "Duplicate completed attempt for payment {PaymentId}, session {SessionId}",
                            attempt.PaymentId, session.Id);
                    }
                    break;
                }
            case EventTypes.CheckoutSessionExpired:
                {
                    var attempt = await unitOfWork.Payments.GetAttemptByStripeSessionIdAsync(session.Id);
                    if (attempt is { Status: PaymentAttemptStatus.Pending })
                    {
                        attempt.Status = PaymentAttemptStatus.Expired;
                        await unitOfWork.CompleteAsync();
                    }
                    break;
                }
            default:
                logger.LogDebug("Ignoring unhandled Stripe event type {EventType}", stripeEvent.Type);
                break;
        }
    }

    private async Task<Result<CreatePaymentResponseDto>?> TryReuseOpenSessionAsync(Payment payment, DateTimeOffset now)
    {
        var open = payment.Attempts
            .Where(a => a.Status == PaymentAttemptStatus.Pending && a.StripeSessionId != null)
            .MaxBy(a => a.CreatedAt);

        if (open == null)
            return null;

        // Reuse existing stripe session if it exists
        Session existing;
        try
        {
            existing = await stripeClient.V1.Checkout.Sessions.GetAsync(open.StripeSessionId);
        }
        catch (StripeException e)
        {
            logger.LogWarning(e, "Could not retrieve session {SessionId} for payment {PaymentId}",
                open.StripeSessionId, payment.PaymentId);
            return Result<CreatePaymentResponseDto>.Failure("Could not reach payment provider",
                FailureReason.InternalError);
        }

        switch (existing.Status)
        {
            case "complete" when existing.PaymentStatus is "paid" or "no_payment_required":
                open.Status = PaymentAttemptStatus.Completed;
                open.CompletedAt = now;
                payment.MarkPaid(now);
                await unitOfWork.CompleteAsync();
                return Result<CreatePaymentResponseDto>.Failure("Payment already ccompleted", FailureReason.Conflict);

            // Only reachable with async methods where committed but funds not confirmed received
            // Let async webhooks resolve
            case "complete":
                return Result<CreatePaymentResponseDto>.Failure("Payment is processing", FailureReason.Conflict);

            // Still payable; handle back same URL
            case "open":
                return Result<CreatePaymentResponseDto>.Success(new CreatePaymentResponseDto
                {
                    CheckoutUrl = existing.Url
                });

            // Expired. Reap and let the caller create a fresh session
            default:
                open.Status = PaymentAttemptStatus.Expired;
                await unitOfWork.CompleteAsync();
                return null;
        }
    }
}
