using System.Net;
using Stripe;

namespace API.Tests.Payments.Fakes;

/// <summary>
/// Fake Stripe <see cref="IHttpClient"/> backend. Instead of talking to Stripe over HTTP it
/// returns a canned JSON body (which the SDK deserializes into a real <c>Session</c>) and records
/// the outgoing <see cref="StripeRequest"/> so tests can assert amount / metadata / idempotency key.
/// Optionally throws to exercise the <c>StripeException</c> failure path.
/// </summary>
public class CapturingStripeHttpClient : IHttpClient
{
    private readonly string _responseJson;
    private readonly Exception? _throw;

    public StripeRequest? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    public CapturingStripeHttpClient(string responseJson) => _responseJson = responseJson;

    private CapturingStripeHttpClient(Exception toThrow)
    {
        _throw = toThrow;
        _responseJson = string.Empty;
    }

    public static CapturingStripeHttpClient Throwing(Exception ex) => new(ex);

    public async Task<StripeResponse> MakeRequestAsync(StripeRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        if (request.Content != null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        if (_throw != null) throw _throw;

        var headers = new HttpResponseMessage().Headers;
        return new StripeResponse(HttpStatusCode.OK, headers, _responseJson);
    }

    // Not exercised by Checkout Session creation; present only to satisfy the interface.
    public Task<StripeStreamedResponse> MakeStreamingRequestAsync(StripeRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
