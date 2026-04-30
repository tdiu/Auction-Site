using API.DTOs;
using API.Entities;

namespace API.Extensions;

public static class BidExtensions
{
    public static IQueryable<BidResponseDto> ProjectToDto(this IQueryable<Bid> query)
    {
        return query.Select(b => new BidResponseDto
        {
            BidId = b.BidId,
            AuctionId = b.AuctionId,
            BidAmount = b.BidAmount,
            BidDate = b.BidDate,
            BidderId = b.BidderId,
            BidderName = b.Bidder.DisplayName
        });
    }

    public static BidResponseDto ToDto(this Bid b)
    {
        return new BidResponseDto
        {
            BidId = b.BidId,
            AuctionId = b.AuctionId,
            BidAmount = b.BidAmount,
            BidDate = b.BidDate,
            BidderId = b.BidderId,
            BidderName = b.Bidder.DisplayName
        };
    }
}