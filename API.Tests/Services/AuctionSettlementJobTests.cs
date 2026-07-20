using API.Entities;
using API.Interfaces;
using API.Services;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace API.Tests.Services;

/// <summary>
/// Fast (substituted-<see cref="IUnitOfWork"/>) coverage of the settlement sweep's choreography:
/// claim → finalize → (winner?) message + enqueue → save → commit. These prove *what calls are made*,
/// not *what reaches Postgres* — the integration suite proves persistence and locking.
/// </summary>
public class AuctionSettlementJobTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IAuctionRepository _auctions = Substitute.For<IAuctionRepository>();
    private readonly IMessageRepository _messages = Substitute.For<IMessageRepository>();
    private readonly IOutboxRepository _outbox = Substitute.For<IOutboxRepository>();
    private readonly IDbContextTransaction _tx = Substitute.For<IDbContextTransaction>();

    public AuctionSettlementJobTests()
    {
        _uow.Auctions.Returns(_auctions);
        _uow.Messages.Returns(_messages);
        _uow.Outbox.Returns(_outbox);
        _uow.BeginTransactionAsync(Arg.Any<CancellationToken>()).Returns(_tx);
    }

    private AuctionSettlementJob CreateJob() =>
        new(_uow, EmptyConfig(), NullLogger<AuctionSettlementJob>.Instance);

    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().Build();

    [Fact]
    public async Task Finalizes_ended_auction_and_messages_the_winner()
    {
        var auction = new Auction
        {
            AuctionId = 7,
            ItemName = "Strat",
            StartingPrice = 1m,
            SellerId = "seller",
            StartTime = default,
            EndTime = DateTimeOffset.UtcNow.AddMinutes(-1),
            CurrentHighBid = 1500m,
            CurrentHighBidderId = "winner"
        };
        _auctions.ClaimEndedUnfinalizedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>()).Returns([auction]);

        await CreateJob().RunAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(auction.FinalizedAt);
        _messages.Received(1).AddMessage(Arg.Is<Message>(m =>
            m.Id == "auction-ended-7" && m.SenderId == "seller" && m.RecipientId == "winner"));
        _outbox.Received(1).Add(Arg.Is<OutboxMessage>(m => m.Type == "AuctionEnded"));
        await _uow.Received(1).CompleteAsync();
        // The mechanism this revision rests on: without the commit the whole sweep rolls back,
        // nothing is finalized, and the dashboard still shows green because RunAsync didn't throw.
        await _tx.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Finalizes_but_writes_no_message_or_outbox_when_auction_had_no_bids()
    {
        // A lot ending unsold: the winner branch's `CurrentHighBid/BidderId not null` guard must skip it.
        var auction = new Auction
        {
            AuctionId = 9,
            ItemName = "Unsold",
            StartingPrice = 1m,
            SellerId = "seller",
            StartTime = default,
            EndTime = DateTimeOffset.UtcNow.AddMinutes(-1),
            CurrentHighBid = null,
            CurrentHighBidderId = null
        };
        _auctions.ClaimEndedUnfinalizedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>()).Returns([auction]);

        await CreateJob().RunAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(auction.FinalizedAt);                       // still finalized
        _messages.DidNotReceive().AddMessage(Arg.Any<Message>());  // no "you won"
        _outbox.DidNotReceive().Add(Arg.Any<OutboxMessage>());     // no email enqueued
        await _uow.Received(1).CompleteAsync();
        await _tx.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Commits_and_writes_nothing_when_no_auctions_are_due()
    {
        _auctions.ClaimEndedUnfinalizedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>())
            .Returns(Array.Empty<Auction>());

        await CreateJob().RunAsync(TestContext.Current.CancellationToken);

        _messages.DidNotReceive().AddMessage(Arg.Any<Message>());
        _outbox.DidNotReceive().Add(Arg.Any<OutboxMessage>());
        await _uow.DidNotReceive().CompleteAsync();                // nothing to save
        await _tx.Received(1).CommitAsync(Arg.Any<CancellationToken>()); // empty tx still closed cleanly
    }
}
