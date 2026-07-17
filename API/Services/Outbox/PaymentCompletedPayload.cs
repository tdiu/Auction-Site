namespace API.Services.Outbox;

public sealed record PaymentCompletedPayload(
    int PaymentId, int AuctionId, string BuyerId, string SellerId, string ItemName);
