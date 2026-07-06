using API.Entities;
using API.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace API.Data;

public class PaymentRepository(AppDbContext context) : IPaymentRepository
{
    public async Task<Payment?> GetByAuctionIdAsync(int auctionId)
    {
        return await context.Payments
            .Include(p => p.Attempts)
            .FirstOrDefaultAsync(a => a.AuctionId == auctionId);
    }

    public async Task<PaymentAttempt?> GetAttemptByStripeSessionIdAsync(string stripeSessionId)
    {
        return await context.PaymentAttempts
            .Include(a => a.Payment)
            .FirstOrDefaultAsync(a => a.StripeSessionId == stripeSessionId);
    }

    public void Add(Payment payment) => context.Add(payment);
}
