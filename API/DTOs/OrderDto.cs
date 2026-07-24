namespace API.DTOs;

public class OrderDto
{
    public int OrderId { get; set; }
    public int AuctionId { get; set; }
    public required string ItemName { get; set; }
    public required string SellerName { get; set; }
    public decimal Amount { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
