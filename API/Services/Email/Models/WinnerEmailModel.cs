namespace API.Services.Email.Models;

public class WinnerEmailModel
{
    public string ItemName { get; set; } = null!;
    public decimal Amount { get; set; }
    public string AuctionUrl { get; set; } = null!;
}
