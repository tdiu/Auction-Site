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

    public async Task<Payment?> GetByIdAsync(int paymentId)
    {
        return await context.Payments
            .Include(p => p.Attempts)
            .Include(p => p.Auction).ThenInclude(a => a.Seller)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.PaymentId == paymentId);
    }

    public async Task<IReadOnlyList<Payment>> GetForUserAsync(string userId)
    {
        return await context.Payments
            .Where(p => p.UserId == userId)
            .Include(p => p.Attempts)
            .Include(p => p.Auction).ThenInclude(a => a.Seller)
            .OrderByDescending(p => p.CreatedAt)
            .AsNoTracking()
            .ToListAsync();
    }

    public void Add(Payment payment) => context.Add(payment);

    public void Detach(Payment payment) => context.Entry(payment).State = EntityState.Detached;
}
