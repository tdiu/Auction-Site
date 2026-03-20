using Microsoft.AspNetCore.Identity;

namespace API.Entities;

public class AppUser : IdentityUser
{
    public required string DisplayName { get; set; }
    
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiry { get; set; }
    public string? ImageUrl { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastActive { get; set; }
    public string? Description { get; set; }
    public ICollection<Auction> Auctions { get; set; } = new List<Auction>();
    public ICollection<Bid> Bids { get; set; } = new List<Bid>();
}