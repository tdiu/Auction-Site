using API.Controllers;
using API.DTOs;
using API.Entities;

namespace API.Extensions;

public static class AuctionExtensions
{
    public static IQueryable<AuctionResponseDto> ProjectToDto(this IQueryable<Auction> query)
    {
        return query.Select(a => new AuctionResponseDto
        {
            AuctionId = a.AuctionId,
            ItemName = a.ItemName,
            StartingPrice = a.StartingPrice,
            BuyNowPrice = a.BuyNowPrice,
            SellerId = a.SellerId,
            SellerName = a.Seller.DisplayName,
            SellerCreatedAt = a.Seller.CreatedAt,
            StartTime = a.StartTime,
            EndTime = a.EndTime,
            CurrentHighBid = a.CurrentHighBid,
            CurrentHighBidderId = a.CurrentHighBidderId,
            Status = a.Status,
        });

    }

    public static AuctionResponseDto ToDto(this Auction a)
    {
        return new AuctionResponseDto
        {
            AuctionId = a.AuctionId,
            ItemName = a.ItemName,
            StartingPrice = a.StartingPrice,
            BuyNowPrice = a.BuyNowPrice,
            SellerId = a.SellerId,
            SellerName = a.Seller.DisplayName,
            SellerCreatedAt = a.Seller.CreatedAt,
            StartTime = a.StartTime,
            EndTime = a.EndTime,
            CurrentHighBid = a.CurrentHighBid,
            CurrentHighBidderId = a.CurrentHighBidderId,
            Status = a.Status,
        };
    }
}
