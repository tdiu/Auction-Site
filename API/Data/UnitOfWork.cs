using System.ComponentModel.DataAnnotations;
using API.Interfaces;

namespace API.Data;

public class UnitOfWork (AppDbContext context, IAuctionRepository auctionRepository, IBidRepository bidRepository) : IUnitOfWork
{
    public IAuctionRepository Auctions => auctionRepository;
    public IBidRepository Bids => bidRepository;

    public async Task<bool> CompleteAsync() => await context.SaveChangesAsync() > 0;

    public void Dispose()
    {
        context.Dispose();
    }
}