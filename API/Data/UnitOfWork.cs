using System.ComponentModel.DataAnnotations;
using API.Interfaces;

namespace API.Data;

public class UnitOfWork(AppDbContext context, IAuctionRepository auctionRepository, IBidRepository bidRepository, IUserRepository userRepository) : IUnitOfWork
{
    public IAuctionRepository Auctions => auctionRepository;
    public IBidRepository Bids => bidRepository;
    public IUserRepository Users => userRepository;

    public async Task<bool> CompleteAsync() => await context.SaveChangesAsync() > 0;

    public void Dispose()
    {
        context.Dispose();
    }
}
