using API.DTOs;

namespace API.Interfaces;

public class IBidService
{
    Task<IReadOnlyList<BidResponseDto>> GetAllBids(int auctionId);
}