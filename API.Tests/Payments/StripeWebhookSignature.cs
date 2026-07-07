using Stripe;

namespace API.Tests.Payments;

/// <summary>
/// Builds real, valid Stripe webhook payloads + signature headers so tests exercise the genuine
/// <see cref="EventUtility.ConstructEvent(string, string, string)"/> verification path (no mocking
/// of the static). The signing secret is always passed in — it must be the same value the service
/// reads from <c>Stripe:WebhookSecret</c>, or verification fails.
/// </summary>
public static class StripeWebhookSignature
{
    public static string Sign(string json, string secret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = EventUtility.ComputeSignature(secret, timestamp, json);
        return $"t={timestamp},v1={signature}";
    }

    public static string CompletedEventJson(string sessionId) =>
        EventJson("checkout.session.completed", sessionId);

    public static string ExpiredEventJson(string sessionId) =>
        EventJson("checkout.session.expired", sessionId);

    /// <param name="objectType">
    /// The <c>data.object.object</c> discriminator. "checkout.session" makes
    /// <c>Event.Data.Object</c> deserialize to a <c>Session</c>; anything else exercises the
    /// service's "not a Session, ignore" guard.
    /// </param>
    // api_version must be present and compatible with the pinned Stripe.net API version, or
    // EventUtility.ConstructEvent throws while checking version compatibility.
    private const string ApiVersion = "2026-06-24.dahlia";

    public static string EventJson(string type, string sessionId, string objectType = "checkout.session") => $$"""
        {
          "id": "evt_test_{{Guid.NewGuid():N}}",
          "object": "event",
          "api_version": "{{ApiVersion}}",
          "type": "{{type}}",
          "data": { "object": { "id": "{{sessionId}}", "object": "{{objectType}}" } }
        }
        """;
}
