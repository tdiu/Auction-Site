using API.DTOs;
using API.Entities;

namespace API.Extensions;

public static class BidExtensions
{
    public static IQueryable<BidResponseDto> ProjectToDto(this IQueryable<Bid> query)
    {
        return query.Select(a => new BidResponseDto
        {
            BidId = a.BidId,
            AuctionId = a.AuctionId,
            BidAmount = a.BidAmount,
            BidDate = a.BidDate,
            BidderId = a.BidderId,
            BidderName = a.Bidder.DisplayName
        });

    }
}