using API.DTOs;
using API.Entities;

namespace API.Extensions;

public static class PaymentExtensions
{
    public static string ToEffectiveStatus(this Payment payment)
    {
        if (payment.Status == PaymentStatus.Paid)
            return "Paid";
        var latest = payment.Attempts.MaxBy(a => a.CreatedAt);
        return latest?.Status switch
        {
            PaymentAttemptStatus.Failed => "Failed",
            PaymentAttemptStatus.Cancelled => "Failed",
            PaymentAttemptStatus.Expired => "Expired",
            _ => "Pending"
        };
    }

    public static OrderDto ToOrderDto(this Payment payment)
    {
        return new OrderDto()
        {
            OrderId = payment.PaymentId,
            AuctionId = payment.AuctionId,
            ItemName = payment.Auction.ItemName,
            SellerName = payment.Auction.Seller.DisplayName,
            Amount = payment.Amount,
            Status = payment.ToEffectiveStatus(),
            CreatedAt = payment.CreatedAt,
            CompletedAt = payment.CompletedAt
        };
    }
}
