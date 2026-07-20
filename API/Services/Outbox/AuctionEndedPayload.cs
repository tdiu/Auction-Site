namespace API.Services.Outbox;

public record AuctionEndedPayload(int AuctionId, string WinnerId, string ItemName, decimal Amount);
