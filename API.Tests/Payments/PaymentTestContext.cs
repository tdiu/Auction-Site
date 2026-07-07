using API.Entities;
using API.Interfaces;
using API.Services;
using API.Tests.Payments.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Stripe;

namespace API.Tests.Payments;

/// <summary>
/// Wires a <see cref="PaymentService"/> for fast tests: NSubstitute <see cref="IUnitOfWork"/> for
/// the DB seam, a fake <see cref="IHttpClient"/> for the Stripe seam, and an in-memory config.
/// The <c>CompleteAsync()</c> stub emulates <c>SaveChanges</c> assigning identity keys, so the
/// metadata / idempotency-key assertions that read <c>PaymentId</c> / <c>AttemptId</c> are meaningful.
/// </summary>
public class PaymentTestContext
{
    public const string WebhookSecret = "whsec_test_secret";
    public const string ClientAppUrl = "https://client.test";

    public const string DefaultSessionJson =
        "{\"id\":\"cs_test_123\",\"object\":\"checkout.session\",\"url\":\"https://checkout.stripe.test/pay/cs_test_123\"}";

    public IUnitOfWork UnitOfWork { get; }
    public IAuctionRepository AuctionRepo { get; }
    public IPaymentRepository PaymentRepo { get; }
    public CapturingStripeHttpClient Http { get; }
    public StripeClient StripeClient { get; }
    public PaymentService Service { get; }

    private Payment? _tracked;
    private int _nextPaymentId = 100;
    private int _nextAttemptId = 500;

    public PaymentTestContext(string? stripeResponseJson = null, Exception? stripeThrows = null)
    {
        AuctionRepo = Substitute.For<IAuctionRepository>();
        PaymentRepo = Substitute.For<IPaymentRepository>();
        UnitOfWork = Substitute.For<IUnitOfWork>();
        UnitOfWork.Auctions.Returns(AuctionRepo);
        UnitOfWork.Payments.Returns(PaymentRepo);

        // Capture the payment the service inserts so the CompleteAsync stub can assign keys to it.
        PaymentRepo.When(r => r.Add(Arg.Any<Payment>())).Do(ci => _tracked = ci.Arg<Payment>());

        // Emulate SaveChanges generating identity keys (the real DB assigns AttemptId/PaymentId).
        UnitOfWork.CompleteAsync().Returns(_ =>
        {
            AssignIds(_tracked);
            return Task.FromResult(true);
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ClientAppUrl"] = ClientAppUrl,
                ["Stripe:WebhookSecret"] = WebhookSecret
            })
            .Build();

        Http = stripeThrows != null
            ? CapturingStripeHttpClient.Throwing(stripeThrows)
            : new CapturingStripeHttpClient(stripeResponseJson ?? DefaultSessionJson);
        StripeClient = new StripeClient("sk_test_123", httpClient: Http);

        Service = new PaymentService(UnitOfWork, config, StripeClient, Substitute.For<ILogger<PaymentService>>());
    }

    /// <summary>The payment the service added via the repository, if any (for asserting attempt state).</summary>
    public Payment? AddedPayment => _tracked;

    /// <summary>Register an existing payment (returned by GetByAuctionIdAsync) for key assignment.</summary>
    public void TrackForIdAssignment(Payment payment) => _tracked = payment;

    private void AssignIds(Payment? payment)
    {
        if (payment == null) return;
        if (payment.PaymentId == 0) payment.PaymentId = _nextPaymentId++;
        foreach (var attempt in payment.Attempts)
        {
            if (attempt.PaymentId == 0) attempt.PaymentId = payment.PaymentId;
            if (attempt.AttemptId == 0) attempt.AttemptId = _nextAttemptId++;
        }
    }
}
