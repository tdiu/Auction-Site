using API.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace API.Data;

public class AppDbContext(DbContextOptions options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Auction> Auctions { get; set; }

    public DbSet<Bid> Bids { get; set; }
    public DbSet<Message> Messages { get; set; }
    public DbSet<Payment> Payments { get; set; }
    public DbSet<PaymentAttempt> PaymentAttempts { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Message>()
            .HasOne(x => x.Recipient)
            .WithMany(m => m.MessagesReceived)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Message>()
            .HasOne(x => x.Sender)
            .WithMany(m => m.MessagesSent)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<IdentityRole>()
            .HasData(
                new IdentityRole { Id = "member-id", Name = "Member", NormalizedName = "MEMBER", ConcurrencyStamp = "1" },
                new IdentityRole { Id = "moderator-id", Name = "Moderator", NormalizedName = "MODERATOR", ConcurrencyStamp = "2" },
                new IdentityRole { Id = "admin-id", Name = "Admin", NormalizedName = "ADMIN", ConcurrencyStamp = "3" }
            );

        modelBuilder.Entity<AppUser>()
            .HasIndex(u => u.DisplayName)
            .IsUnique();

        modelBuilder.Entity<AppUser>()
            .Property(u => u.CreatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        modelBuilder.Entity<AppUser>()
            .Property(u => u.LastActive)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        // Fix for multiple cascade paths on Bid (Dependency Loop)
        modelBuilder.Entity<Bid>()
            .HasOne(b => b.Bidder)
            .WithMany(u => u.Bids)
            .HasForeignKey(b => b.BidderId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Bid>()
            .HasOne(b => b.Auction)
            .WithMany(a => a.Bids)
            .HasForeignKey(b => b.AuctionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Payment>()
            .HasIndex(p => p.AuctionId)
            .IsUnique();

        modelBuilder.Entity<Payment>()
            .HasMany(p => p.Attempts)
            .WithOne(a => a.Payment)
            .HasForeignKey(a => a.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PaymentAttempt>()
            .HasIndex(a => a.StripeSessionId)
            .IsUnique();

        modelBuilder.Entity<PaymentAttempt>()
            .HasIndex(a => a.PaymentId)
            .IsUnique()
            .HasFilter($"\"Status\" = {(int)PaymentAttemptStatus.Completed}");

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            // Partial index to keep dispatcher claim query cheap. Filtered on pending state and ordered by visibleat, createdat
            // to match claim's WHERE/ORDER BY exactly
            b.Property(m => m.Payload).HasColumnType("jsonb");
            b.HasIndex(m => new { m.VisibleAt, m.CreatedAt }).HasFilter("\"Status\" = 0");
        });
    }
}
