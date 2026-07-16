using System.ComponentModel.DataAnnotations;
using API.Interfaces;
using Microsoft.EntityFrameworkCore.Storage;

namespace API.Data;

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;
    private readonly IUserRepository _userRepository;
    private readonly IAuctionRepository _auctionRepository;
    private readonly IBidRepository _bidRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly IOutboxRepository _outboxRepository;


    public UnitOfWork(
        AppDbContext context,
        IUserRepository userRepository,
        IAuctionRepository auctionRepository,
        IBidRepository bidRepository,
        IPaymentRepository paymentRepository,
        IMessageRepository messageRepository,
        IOutboxRepository outboxRepository)
    {
        this._context = context;
        this._userRepository = userRepository;
        this._auctionRepository = auctionRepository;
        this._bidRepository = bidRepository;
        this._paymentRepository = paymentRepository;
        this._messageRepository = messageRepository;
        this._outboxRepository = outboxRepository;
    }

    public IAuctionRepository Auctions => _auctionRepository;
    public IBidRepository Bids => _bidRepository;
    public IUserRepository Users => _userRepository;
    public IPaymentRepository Payments => _paymentRepository;
    public IMessageRepository Messages => _messageRepository;
    public IOutboxRepository Outbox => _outboxRepository;

    public async Task<bool> CompleteAsync() => await _context.SaveChangesAsync() > 0;

    public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct)
        => await _context.Database.BeginTransactionAsync(ct);

    public void Dispose()
    {
        _context.Dispose();
    }
}
