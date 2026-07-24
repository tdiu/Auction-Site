namespace API.Services.Email.Models;

public record ReceiptEmailModel(
    int OrderId, string ItemName, string SellerName,
    decimal Amount, DateTimeOffset PaidAt, string OrderUrl);
