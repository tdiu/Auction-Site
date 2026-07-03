using API.Core;
using API.DTOs;
using API.Entities;
using API.Interfaces;
using Stripe;
using Stripe.Checkout;

namespace API.Services;

public class PaymentService(IUnitOfWork unitOfWork, IConfiguration configuration, StripeClient stripeClient) : IPaymentService
{
    public async Task<Result<CreatePaymentResponseDto>> CreateCheckoutSession(int auctionId, string userId)
    {
        var auction = await unitOfWork.Auctions.GetAuctionAsync(auctionId);
        var existing = await unitOfWork.Payments.GetByAuctionIdAsync(auctionId);
        var currTime = DateTimeOffset.UtcNow;

        if (auction == null)
            return Result<CreatePaymentResponseDto>.Failure("Cannot find auction", FailureReason.NotFound);
        if (auction.EndTime > currTime || auction.CurrentHighBid == null)
            return Result<CreatePaymentResponseDto>.Failure("Auction in progress or no bidder", FailureReason.Conflict);
        if (auction.CurrentHighBidderId != userId)
            return Result<CreatePaymentResponseDto>.Failure("Invalid user", FailureReason.Forbidden);
        if (existing is {Status: PaymentStatus.Paid})
            return Result<CreatePaymentResponseDto>.Failure("Payment already completed", FailureReason.Conflict);

        Payment payment;
        if (existing == null)
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
            await unitOfWork.CompleteAsync();
        }
        else
        {
            payment = existing;
            payment.Status = PaymentStatus.Pending;
            payment.Amount = auction.CurrentHighBid.Value;
            payment.CompletedAt = null;
            payment.StripeSessionId = null;
        }

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
            ClientReferenceId = payment.PaymentId.ToString(),
            Metadata = new Dictionary<string, string>
            {
                ["payment_id"] = payment.PaymentId.ToString(), ["auction_id"] = auctionId.ToString(),
            },
        };

        try
        {
            var session = await stripeClient.V1.Checkout.Sessions.CreateAsync(options,
                new RequestOptions { IdempotencyKey = $"payment-{payment.PaymentId}-{Guid.NewGuid():N}" });

            payment.StripeSessionId = session.Id;
            await unitOfWork.CompleteAsync();

            return Result<CreatePaymentResponseDto>.Success(new CreatePaymentResponseDto { CheckoutUrl = session.Url, });
        }
        catch (StripeException e)
        {
            return Result<CreatePaymentResponseDto>.Failure(e.Message, FailureReason.InternalError);
        }
    }

    public Task<Result<PaymentStatusDto>> GetPaymentStatus(int auctionId, string userId) => throw new NotImplementedException();

    public Task HandleWebhook(string json, string stripeSignature) => throw new NotImplementedException();
}
