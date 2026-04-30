using API.Entities;
using API.Interfaces;

namespace API.Data;

public class BidRepository(AppDbContext context) : IBidRepository
{
    public IQueryable<Bid> GetBidsQueryable()
    {
        return context.Bids;
    }
}