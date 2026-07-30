using Microsoft.EntityFrameworkCore.Storage;

namespace API.Interfaces;

public interface IUnitOfWork : IDisposable
{
    IAuctionRepository Auctions { get; }
    IBidRepository Bids { get; }
    IUserRepository Users { get; }
    IPaymentRepository Payments { get; }
    IMessageRepository Messages { get; }
    IOutboxRepository Outbox { get; }
    IRefreshSessionRepository RefreshSessions { get; }
    Task<bool> CompleteAsync();
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
}
