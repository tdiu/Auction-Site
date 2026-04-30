using API.Core;
using API.DTOs;

namespace API.Interfaces;

public interface IBidService
{
    Task<IReadOnlyList<BidResponseDto>> GetAllBids(int auctionId);
    Task<Result<BidResponseDto>> PlaceBid(BidRequestDto bidRequestDto, int auctionId, string userId);
}