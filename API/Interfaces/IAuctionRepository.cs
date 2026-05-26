using API.DTOs;
using API.Entities;

namespace API.Interfaces;

public interface IAuctionRepository
{
    IQueryable<Auction> GetAuctionsQueryable();
    Task<Auction?> GetAuctionAsync(int id);
    Task<Auction> CreateAuctionAsync(Auction auction);
    Task<int> UpdateAuctionStatusesAsync();
}
