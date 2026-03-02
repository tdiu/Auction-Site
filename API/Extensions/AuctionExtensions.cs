using API.DTOs;
using API.Entities;

namespace API.Extensions;

public static class AuctionExtensions
{
    public static AuctionResponseDto ToDto(this Auction auction)
    {
        return new AuctionResponseDto()
        {
            ItemName = auction.ItemName,
            StartingPrice = auction.StartingPrice,
            BuyNowPrice = auction.BuyNowPrice,
            SellerId =  auction.SellerId
        };
    }
}