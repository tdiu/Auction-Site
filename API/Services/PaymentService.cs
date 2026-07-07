using API.Core;
using API.DTOs;
using API.Entities;
using API.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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

        var payment = await unitOfWork.Payments.GetByAuctionIdAsync(auctionId);

        if (payment == null)
        {
            payment = new Payment
            {
                AuctionId = auctionId,
                UserId = userId,
                Amount = auction.CurrentHighBid.Value,
                Status = PaymentStatus.Pending,
                CreatedAt = currTime
            };

            unitOfWork.Payments.Add(payment);
            try
            {
                await unitOfWork.CompleteAsync();
            }
            catch (DbUpdateException e) when (IsUniqueViolation(e))
            {
                // Concurrent request created it first. Drop our failed insert (still tracked as
                // Added) so the next save won't retry it, then re-read and continue.
                unitOfWork.Payments.Detach(payment);
                payment = await unitOfWork.Payments.GetByAuctionIdAsync(auctionId);
                if (payment == null)
                    return Result<CreatePaymentResponseDto>.Failure("Could not create payment", FailureReason.InternalError);
                if (payment is { Status: PaymentStatus.Paid })
                    return Result<CreatePaymentResponseDto>.Failure("Payment already completed", FailureReason.Conflict);
            }
        }

        var attempt = new PaymentAttempt
        {
            PaymentId = payment.PaymentId,
            Amount = payment.Amount,
            Status = PaymentAttemptStatus.Pending,
            CreatedAt = currTime
        };
        payment.Attempts.Add(attempt);
        await unitOfWork.CompleteAsync();


        var clientUrl = configuration["ClientAppUrl"];

        var options = new SessionCreateOptions
        {
            Mode = "payment",
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
            SuccessUrl = $"{clientUrl}/auctions/{auctionId}?session_id={{CHECKOUT_SESSION_ID}}",
            CancelUrl = $"{clientUrl}/auctions/{auctionId}?cancelled=true",
            ClientReferenceId = attempt.AttemptId.ToString(),
            Metadata = new Dictionary<string, string>
            {
                ["payment_id"] = payment.PaymentId.ToString(),
                ["attempt_id"] = attempt.AttemptId.ToString(),
                ["auction_id"] = auctionId.ToString()
            },
        };

        try
        {
            var session = await stripeClient.V1.Checkout.Sessions.CreateAsync(options,
                new RequestOptions { IdempotencyKey = $"attempt-{attempt.AttemptId}" });

            attempt.StripeSessionId = session.Id;
            await unitOfWork.CompleteAsync();
            return Result<CreatePaymentResponseDto>.Success(new CreatePaymentResponseDto { CheckoutUrl = session.Url, });
        }
        catch (StripeException e)
        {
            attempt.Status = PaymentAttemptStatus.Failed;
            await unitOfWork.CompleteAsync();
            return Result<CreatePaymentResponseDto>.Failure(e.Message, FailureReason.InternalError);
        }
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
            Status = payment.Status.ToString(),
            Amount = payment.Amount,
            CompletedAt = payment.CompletedAt
        });
    }

    public async Task HandleWebhook(string json, string stripeSignature)
    {
        var secret = configuration["Stripe:WebhookSecret"] ?? throw new InvalidOperationException("Missing webhook secret");
        var stripeEvent = EventUtility.ConstructEvent(json, stripeSignature, secret);

        if (stripeEvent.Data.Object is not Session session) return;

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                {
                    var attempt = await unitOfWork.Payments.GetAttemptByStripeSessionIdAsync(session.Id);
                    if (attempt == null) return;

                    var now = DateTimeOffset.UtcNow;
                    if (attempt.Status != PaymentAttemptStatus.Completed)
                    {
                        attempt.Status = PaymentAttemptStatus.Completed;
                        attempt.CompletedAt = now;
                    }
                    attempt.Payment.MarkPaid(now);

                    try
                    {
                        await unitOfWork.CompleteAsync();
                    }
                    catch (DbUpdateException e) when (IsUniqueViolation(e))
                    {
                        logger.LogWarning(e, "Duplicate completed attempt for payment {PaymentId}, session {SessionId}",
                            attempt.PaymentId, session.Id);
                    }
                    break;
                }
            case "checkout.session.expired":
                {
                    var attempt = await unitOfWork.Payments.GetAttemptByStripeSessionIdAsync(session.Id);
                    if (attempt is { Status: PaymentAttemptStatus.Pending })
                    {
                        attempt.Status = PaymentAttemptStatus.Expired;
                        await unitOfWork.CompleteAsync();
                    }
                    break;
                }
        }
    }

    private static bool IsUniqueViolation(DbUpdateException e) => e.InnerException is PostgresException
    {
        SqlState: PostgresErrorCodes.UniqueViolation
    };
}
