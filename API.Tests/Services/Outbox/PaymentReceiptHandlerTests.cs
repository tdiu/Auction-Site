using System.Text.Json;
using API.Entities;
using API.Interfaces;
using API.Services.Outbox;
using API.Services.Outbox.Handlers;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace API.Tests.Services.Outbox;

/// <summary>
/// Fast coverage of the buyer's receipt email, split out of PaymentCompletedHandler so the two side
/// effects of one payment retry independently. This handler owns no DB write, which is why it throws
/// on every failure rather than logging and returning: failing its row costs nothing else.
/// </summary>
public class PaymentReceiptHandlerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IPaymentRepository _payments = Substitute.For<IPaymentRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IEmailTemplateRenderer _renderer = Substitute.For<IEmailTemplateRenderer>();

    public PaymentReceiptHandlerTests()
    {
        _uow.Payments.Returns(_payments);
        _uow.Users.Returns(_users);
        _renderer.RenderAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns("<html>receipt</html>");
    }

    private PaymentReceiptHandler CreateHandler()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ClientAppUrl"] = "https://client.test" })
            .Build();
        return new PaymentReceiptHandler(_uow, _emailSender, _renderer, config);
    }

    private static OutboxMessage MessageFor(PaymentCompletedPayload payload) => new()
    {
        Type = "PaymentReceipt",
        CreatedAt = default,
        VisibleAt = default,
        Payload = JsonSerializer.Serialize(payload)
    };

    private static PaymentCompletedPayload Payload() => new(
        PaymentId: 7, AuctionId: 1, BuyerId: "buyer", SellerId: "seller", ItemName: "Strat");

    private static Payment OrderPayment(string seller = "Seller Co") => new()
    {
        AuctionId = 1,
        UserId = "buyer",
        Amount = 150m,
        Status = PaymentStatus.Paid,
        CompletedAt = DateTimeOffset.UtcNow,
        Auction = new Auction
        {
            AuctionId = 1,
            ItemName = "Strat",
            StartingPrice = 100m,
            SellerId = "seller",
            StartTime = DateTimeOffset.UtcNow.AddDays(-2),
            EndTime = DateTimeOffset.UtcNow.AddMinutes(-5),
            Seller = new AppUser { DisplayName = seller }
        }
    };

    private void SetUp(PaymentCompletedPayload payload, Payment? payment = null, string? buyerEmail = "buyer@test.com")
    {
        _payments.GetByIdAsync(payload.PaymentId).Returns(payment ?? OrderPayment());
        _users.GetUserByIdAsync(payload.BuyerId)
            .Returns(new AppUser { DisplayName = "Buyer", Email = buyerEmail });
    }

    [Fact]
    public async Task Sends_receipt_to_buyer_with_idempotency_key()
    {
        var payload = Payload();
        SetUp(payload);

        await CreateHandler().Handle(MessageFor(payload), TestContext.Current.CancellationToken);

        // The key is the only duplicate defence this handler has. The dispatcher's 23505 rule is
        // no help: the send completes before the commit that would surface a collision.
        await _emailSender.Received(1).SendAsync(
            "buyer@test.com",
            Arg.Is<string>(s => s.Contains("Strat")),
            Arg.Any<string>(),
            "payment-receipt-7",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Renders_the_receipt_template_with_the_order_details()
    {
        var payload = Payload();
        SetUp(payload);

        await CreateHandler().Handle(MessageFor(payload), TestContext.Current.CancellationToken);

        await _renderer.Received(1).RenderAsync("Receipt", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stages_no_db_write()
    {
        // The whole point of the split. If this handler ever stages a write, it re-acquires a
        // stake in its own row committing, and the independent-retry property is gone.
        var messages = Substitute.For<IMessageRepository>();
        _uow.Messages.Returns(messages);
        SetUp(Payload());

        await CreateHandler().Handle(MessageFor(Payload()), TestContext.Current.CancellationToken);

        messages.DidNotReceive().AddMessage(Arg.Any<Message>());
        await _uow.DidNotReceive().CompleteAsync();
    }

    [Fact]
    public async Task Throws_when_buyer_has_no_email()
    {
        // Unreachable while Identity is configured with RequireUniqueEmail, which rejects a null or
        // empty address on both CreateAsync and UpdateAsync. Asserted anyway: the nullable type is
        // inherited from IdentityUser and cannot express the constraint, so this pins the intended
        // behaviour if the invariant ever weakens (a provider whose email scope is not granted).
        var payload = Payload();
        SetUp(payload, buyerEmail: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler().Handle(MessageFor(payload), TestContext.Current.CancellationToken));

        await _emailSender.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Throws_when_the_payment_is_missing()
    {
        var payload = Payload();
        _payments.GetByIdAsync(payload.PaymentId).Returns((Payment?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler().Handle(MessageFor(payload), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Throws_on_malformed_payload_and_sends_nothing()
    {
        var msg = new OutboxMessage
        {
            Type = "PaymentReceipt",
            Payload = "null",
            CreatedAt = default,
            VisibleAt = default
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler().Handle(msg, TestContext.Current.CancellationToken));

        await _emailSender.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }
}
