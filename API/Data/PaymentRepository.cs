using API.Entities;
using API.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace API.Data;

public class PaymentRepository(AppDbContext context) : IPaymentRepository
{
    public async Task<Payment?> GetByAuctionIdAsync(int auctionId)
    {
        return await context.Payments
            .Where(p => p.AuctionId == auctionId)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<Payment?> GetByStripeSessionIdAsync(string stripeSessionId)
    {
        return await context.Payments
            .Where(p => p.StripeSessionId == stripeSessionId)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public void Add(Payment payment) => context.Add(payment);
}
