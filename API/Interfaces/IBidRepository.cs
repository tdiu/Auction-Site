using API.Entities;

namespace API.Interfaces;

public interface IBidRepository
{
    IQueryable<Bid> GetBidsQueryable();
}