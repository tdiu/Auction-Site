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
            BidderName = ObfuscateName(b.Bidder.DisplayName)
        };
    }

    public static void ObfuscateBidderNames(this IEnumerable<BidResponseDto> bids)
    {
        foreach (var bid in bids)
            bid.BidderName = ObfuscateName(bid.BidderName);
    }

    private static string ObfuscateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Anonymous";
        if (name.Length == 1) return name + "***";
        if (name.Length == 2) return name[0] + "***";
        return name[0] + "***" + name[^1];
    }
}
