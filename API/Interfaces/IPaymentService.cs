using API.Core;
using API.DTOs;

namespace API.Interfaces;

public interface IPaymentService
{
    Task<Result<CreatePaymentResponseDto>> CreateCheckoutSession(int auctionId, string userId);
    Task<Result<PaymentStatusDto>> GetPaymentStatus(int auctionId, string userId);
    Task HandleWebhook(string json, string stripeSignature);
}
