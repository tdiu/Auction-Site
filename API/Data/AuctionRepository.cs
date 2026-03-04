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

    public async Task<AuctionResponseDto?> GetAuctionAsync(int id)
    {
        return await context.Auctions
            .ProjectToDto()
            .FirstOrDefaultAsync(a => a.AuctionId == id);
    }

    public async Task<Auction> CreateAuctionAsync(Auction auction)
    {
         context.Add(auction);
         await context.SaveChangesAsync();
         return auction;
    }
    
}