using API.DTOs;
using API.Entities;
using API.Extensions;
using API.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace API.Data;

public class AuctionRepository(AppDbContext context) : IAuctionRepository
{
    public IQueryable<Auction> GetAuctionsQueryable()
    {
        return context.Auctions;
    }

    public async Task<Auction?> GetAuctionAsync(int id)
    {
        return await context.Auctions
            .Include(a => a.Seller)
            .FirstOrDefaultAsync(a => a.AuctionId == id);
    }

    public async Task<Auction> CreateAuctionAsync(Auction auction)
    {
        context.Add(auction);
        await context.SaveChangesAsync();
        return auction;
    }

    public async Task<int> UpdateAuctionStatusesAsync()
    {
        var now = DateTimeOffset.UtcNow;
        return await context.Auctions
            .Where(a => a.EndTime <= now && a.Status == AuctionStatus.Active)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.Status, a => a.CurrentHighBid == null
                        ? AuctionStatus.Expired
                        : AuctionStatus.Ended));
    }
}
