using API.Entities;

namespace API.Interfaces;

public interface IPaymentRepository
{
    Task<Payment?> GetByAuctionIdAsync(int auctionId);
    Task<PaymentAttempt?> GetAttemptByStripeSessionIdAsync(string stripeSessionId);
    void Add(Payment payment);

    /// <summary>Stop tracking a payment (e.g. an insert that failed a unique-violation) so a later save won't retry it.</summary>
    void Detach(Payment payment);
}
