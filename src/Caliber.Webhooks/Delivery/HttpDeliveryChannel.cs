using System.Globalization;
using System.Text;

namespace Caliber.Webhooks;

/// <summary>
/// Delivers webhooks over HTTPS using a named <see cref="HttpClient"/>. Applies a per-attempt timeout
/// driven by the configured clock and converts delivery failures into a <see cref="DeliveryResult"/>
/// rather than throwing. In M1 there is no SSRF guard in the pipeline — that arrives in M3.
/// </summary>
internal sealed class HttpDeliveryChannel : IDeliveryChannel
{
    /// <summary>The named <see cref="HttpClient"/> registered for webhook delivery.</summary>
    internal const string HttpClientName = "Caliber.Webhooks";

    private const string MediaType = "application/json";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CaliberWebhooksOptions _options;

    public HttpDeliveryChannel(IHttpClientFactory httpClientFactory, CaliberWebhooksOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public async Task<DeliveryResult> SendAsync(
        Endpoint endpoint, WebhookMessage message, WebhookSignatureHeaders headers, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(message);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
        {
            Content = new StringContent(message.Payload, Encoding.UTF8, MediaType),
        };
        request.Headers.TryAddWithoutValidation(SigningEngine.IdHeader, headers.Id);
        request.Headers.TryAddWithoutValidation(SigningEngine.TimestampHeader, headers.Timestamp);
        request.Headers.TryAddWithoutValidation(SigningEngine.SignatureHeader, headers.Signature);

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        using var timeout = new CancellationTokenSource(_options.HttpTimeout, _options.TimeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        try
        {
            using var response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token)
                .ConfigureAwait(false);

            var status = (int)response.StatusCode;
            return response.IsSuccessStatusCode
                ? new DeliveryResult(true, status, null)
                : new DeliveryResult(false, status, "HTTP " + status.ToString(CultureInfo.InvariantCulture));
        }
        catch (HttpRequestException ex)
        {
            return new DeliveryResult(false, null, ex.Message);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return new DeliveryResult(false, null, "The delivery attempt timed out.");
        }
    }
}
