using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Caliber.Webhooks.Tests;

public sealed class HttpDeliveryChannelTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private static HttpDeliveryChannel Channel(HttpStatusCode status)
    {
        var options = new CaliberWebhooksOptions { TimeProvider = new FakeTimeProvider(Now) };
        return new HttpDeliveryChannel(new SingleClientFactory(new StubHandler(status)), options);
    }

    private static (Endpoint Endpoint, WebhookMessage Message, WebhookSignatureHeaders Headers) Fixtures()
    {
        var endpoint = new Endpoint { Id = Guid.NewGuid(), Url = "https://acme.example/hooks", Secret = WebhookSecret.Generate() };
        var message = new WebhookMessage
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            EndpointId = endpoint.Id,
            EventType = "order.shipped",
            Payload = "{}",
            CreatedAt = Now,
            NextAttemptAt = Now,
        };
        var headers = new WebhookSignatureHeaders(message.Id.ToString(), "1700000000", "v1,signature");
        return (endpoint, message, headers);
    }

    [Fact]
    public async Task A_2xx_response_is_a_successful_delivery()
    {
        var (endpoint, message, headers) = Fixtures();

        var result = await Channel(HttpStatusCode.OK).SendAsync(endpoint, message, headers);

        result.Succeeded.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Error.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, 500)]
    [InlineData(HttpStatusCode.BadRequest, 400)]
    public async Task A_non_2xx_response_is_a_failed_delivery_carrying_the_status(HttpStatusCode status, int code)
    {
        var (endpoint, message, headers) = Fixtures();

        var result = await Channel(status).SendAsync(endpoint, message, headers);

        result.Succeeded.Should().BeFalse();
        result.StatusCode.Should().Be(code);
        result.Error.Should().Be("HTTP " + code);
    }

    [Fact]
    public async Task SendAsync_rejects_a_null_endpoint()
    {
        var (_, message, headers) = Fixtures();
        var act = async () => await Channel(HttpStatusCode.OK).SendAsync(null!, message, headers);
        (await act.Should().ThrowAsync<ArgumentNullException>()).Which.ParamName.Should().Be("endpoint");
    }

    [Fact]
    public async Task SendAsync_rejects_a_null_message()
    {
        var (endpoint, _, headers) = Fixtures();
        var act = async () => await Channel(HttpStatusCode.OK).SendAsync(endpoint, null!, headers);
        (await act.Should().ThrowAsync<ArgumentNullException>()).Which.ParamName.Should().Be("message");
    }
}
