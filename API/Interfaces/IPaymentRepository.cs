using API.Entities;

namespace API.Interfaces;

public interface IPaymentRepository
{
    Task<Payment?> GetByAuctionIdAsync(int auctionId);
    Task<PaymentAttempt?> GetAttemptByStripeSessionIdAsync(string stripeSessionId);
    void Add(Payment payment);
}
