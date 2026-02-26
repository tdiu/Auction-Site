using API.Entities;
using Microsoft.EntityFrameworkCore;

namespace API.Data;

public class AppDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<AppUser> Users { get; set; }
    
    public DbSet<Auction> Auctions { get; set; }
    
    public DbSet<Bid> Bids { get; set; }
}