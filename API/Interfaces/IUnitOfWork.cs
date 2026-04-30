namespace API.Interfaces;

public interface IUnitOfWork : IDisposable
{
    IAuctionRepository Auctions { get; }
    IBidRepository Bids { get; }
    Task<bool> CompleteAsync();
}