namespace API.Interfaces;

public interface IUnitOfWork : IDisposable
{
    IAuctionRepository Auctions { get; }
    IBidRepository Bids { get; }
    IUserRepository Users { get; }
    IPaymentRepository Payments { get; }
    Task<bool> CompleteAsync();
}
