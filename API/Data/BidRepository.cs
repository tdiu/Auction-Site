using API.Entities;
using API.Extensions;
using API.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace API.Data;

public class BidRepository(AppDbContext context) : IBidRepository
{
    public IQueryable<Bid> GetBidsQueryable()
    {
        return context.Bids;
    }
    
    public void  Add(Bid bid)
    {
        context.Bids.Add(bid);
    }
    
}