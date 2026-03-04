using API.DTOs;
using API.Entities;

namespace API.Interfaces;

public interface IAuctionRepository
{
    IQueryable<Auction> GetAuctionsQueryable();
    Task<AuctionResponseDto?> GetAuctionAsync(int id);
    Task<Auction> CreateAuctionAsync(Auction auction);
}