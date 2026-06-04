using API.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace API.Data;

public static class DbInitializer
{
    public static async Task SeedData(AppDbContext context, UserManager<AppUser> userManager)
    {
        // 1. Ensure migrations are applied
        if (context.Database.GetPendingMigrations().Any())
        {
            await context.Database.MigrateAsync();
        }

        // 2. Seed Users if none exist
        if (!await userManager.Users.AnyAsync())
        {
            var users = new List<AppUser>
            {
                new AppUser
                {
                    DisplayName = "Alice",
                    UserName = "Alice",
                    Email = "alice@test.com",
                    DateOfBirth = new DateOnly(1990, 1, 1),
                },
                new AppUser
                {
                    DisplayName = "Bob",
                    UserName = "Bob",
                    Email = "bob@test.com",
                    DateOfBirth = new DateOnly(1992, 5, 10),
                }
            };

            foreach (var user in users)
            {
                await userManager.CreateAsync(user, "Pa$$w0rd");
                await userManager.AddToRoleAsync(user, "Member");
            }
        }

        // 3. Seed Auctions if none exist
        if (!await context.Auctions.AnyAsync())
        {
            var alice = await userManager.FindByEmailAsync("alice@test.com");
            var bob = await userManager.FindByEmailAsync("bob@test.com");

            if (alice != null && bob != null)
            {
                var auctions = new List<Auction>
                {
                    new Auction
                    {
                        ItemName = "Vintage Fender Stratocaster",
                        StartingPrice = 1200.00m,
                        BuyNowPrice = 2000.00m,
                        SellerId = alice.Id,
                        StartTime = DateTimeOffset.UtcNow,
                        EndTime = DateTimeOffset.UtcNow.AddDays(7)
                    },
                    new Auction
                    {
                        ItemName = "Signed Baseball",
                        StartingPrice = 50.00m,
                        BuyNowPrice = null,
                        SellerId = bob.Id,
                        StartTime = DateTimeOffset.UtcNow,
                        EndTime = DateTimeOffset.UtcNow.AddDays(3)
                    }
                };

                context.Auctions.AddRange(auctions);
                await context.SaveChangesAsync();
            }
        }
    }
}
