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

    public async Task<IReadOnlyList<Auction>> ClaimEndedUnfinalizedAsync(DateTimeOffset now, int batchSize)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                $"{nameof(ClaimEndedUnfinalizedAsync)} must run inside an explicit transaction: " +
                "FOR UPDATE SKIP LOCKED releases its locks at statement end otherwise, and the " +
                "claim provides no mutual exclusion at all.");

        return await context.Auctions
            .FromSql($"""
                      SELECT * FROM "Auctions"
                      WHERE "EndTime" <= {now} AND "FinalizedAt" IS NULL
                      ORDER BY "EndTime"
                      LIMIT {batchSize}
                      FOR UPDATE SKIP LOCKED
                      """)
            .ToListAsync();
    }
}
