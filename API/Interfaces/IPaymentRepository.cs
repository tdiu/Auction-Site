using API.Entities;

namespace API.Interfaces;

public interface IPaymentRepository
{
    Task<Payment?> GetByAuctionIdAsync(int auctionId);
    Task<Payment?> GetByStripeSessionIdAsync(string stripeSessionId);
    void Add(Payment payment);
}
