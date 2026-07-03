using System.ComponentModel.DataAnnotations;
using API.Interfaces;

namespace API.Data;

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;
    private readonly IUserRepository _userRepository;
    private readonly IAuctionRepository _auctionRepository;
    private readonly IBidRepository _bidRepository;
    private readonly IPaymentRepository _paymentRepository;

    public UnitOfWork(
        AppDbContext context,
        IUserRepository userRepository,
        IAuctionRepository auctionRepository,
        IBidRepository bidRepository,
        IPaymentRepository paymentRepository)
    {
        this._context = context;
        this._userRepository = userRepository;
        this._auctionRepository = auctionRepository;
        this._bidRepository = bidRepository;
        this._paymentRepository = paymentRepository;
    }

    public IAuctionRepository Auctions => _auctionRepository;
    public IBidRepository Bids => _bidRepository;
    public IUserRepository Users => _userRepository;
    public IPaymentRepository Payments => _paymentRepository;

    public async Task<bool> CompleteAsync() => await _context.SaveChangesAsync() > 0;

    public void Dispose()
    {
        _context.Dispose();
    }
}
