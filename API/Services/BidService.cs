using API.Data;
using API.DTOs;
using API.Extensions;
using API.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace API.Services;

public class BidService(IBidRepository bidRepository) : IBidService 
{
    public async Task<IReadOnlyList<BidResponseDto>> GetAllBids(int auctionId)
    {
        var query = bidRepository.GetBidsQueryable();
        
        return await query.ProjectToDto().ToListAsync();
    }
}