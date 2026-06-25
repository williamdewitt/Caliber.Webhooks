using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Caliber.Webhooks;

/// <summary>
/// Produces Standard Webhooks signature headers using HMAC-SHA256. Encapsulates the signing-algorithm
/// volatility (V2): ed25519 and secret rotation slot in behind this same contract later.
/// </summary>
internal sealed class SigningEngine
{
    /// <summary>The Standard Webhooks message-id header name.</summary>
    internal const string IdHeader = "webhook-id";

    /// <summary>The Standard Webhooks timestamp header name.</summary>
    internal const string TimestampHeader = "webhook-timestamp";

    /// <summary>The Standard Webhooks signature header name.</summary>
    internal const string SignatureHeader = "webhook-signature";

    private const string Version = "v1";

    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates a signing engine that stamps each signature with the supplied clock (default the
    /// system clock).
    /// </summary>
    /// <param name="timeProvider">The clock used for the signature timestamp.</param>
    public SigningEngine(TimeProvider? timeProvider = null)
        => _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Signs a payload for a delivery whose stable id is <paramref name="webhookId"/>.
    /// </summary>
    public WebhookSignatureHeaders Sign(Guid webhookId, string payload, string secret)
        => Sign(webhookId.ToString(), payload, secret);

    /// <summary>
    /// Signs a payload for a delivery identified by an arbitrary stable id string.
    /// </summary>
    /// <param name="webhookId">The stable <c>webhook-id</c> echoed into the signed content.</param>
    /// <param name="payload">The serialized payload to sign.</param>
    /// <param name="secret">The endpoint signing secret.</param>
    /// <returns>The id, timestamp, and <c>v1</c> signature headers to send.</returns>
    public WebhookSignatureHeaders Sign(string webhookId, string payload, string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(webhookId);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrEmpty(secret);

        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = ComputeSignature(webhookId, timestamp, payload, secret);
        return new WebhookSignatureHeaders(webhookId, timestamp, signature);
    }

    /// <summary>
    /// Computes the <c>v1,&lt;base64&gt;</c> signature over the Standard Webhooks signed content
    /// <c>{id}.{timestamp}.{payload}</c>.
    /// </summary>
    internal static string ComputeSignature(string id, string timestamp, string payload, string secret)
    {
        var content = $"{id}.{timestamp}.{payload}";
        var key = WebhookSecret.DecodeKey(secret);
        try
        {
            Span<byte> hash = stackalloc byte[32];
            HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(content), hash);
            return Version + "," + Convert.ToBase64String(hash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// Compares two signature strings in constant time, so a near-miss cannot be detected by timing.
    /// </summary>
    internal static bool SignaturesMatch(string expected, string provided)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(provided));
}
