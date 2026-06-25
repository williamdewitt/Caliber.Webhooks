using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Caliber.Webhooks.Tests;

public sealed class DispatcherHostTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class SignalingChannel(DeliveryResult result, Action onSend) : IDeliveryChannel
    {
        public Task<DeliveryResult> SendAsync(
            Endpoint endpoint, WebhookMessage message, WebhookSignatureHeaders headers, CancellationToken ct = default)
        {
            onSend();
            return Task.FromResult(result);
        }
    }

    private static DeliveryManager Delivery(
        InMemoryMessageStore messages, InMemoryEndpointStore endpoints, IDeliveryChannel channel, CaliberWebhooksOptions options)
        => new(messages, endpoints, new SigningEngine(options.TimeProvider), new RetryEngine(options, () => 0.5), channel, options, owner: "test");

    [Fact]
    public async Task Delivers_enqueued_messages_after_starting()
    {
        var clock = new FakeTimeProvider(Now);
        var options = new CaliberWebhooksOptions { TimeProvider = clock };
        var messages = new InMemoryMessageStore(clock);
        var endpoints = new InMemoryEndpointStore();
        var endpoint = new Endpoint { Id = Guid.NewGuid(), Url = "https://acme.example/hooks", Secret = WebhookSecret.Generate() };
        await endpoints.UpsertAsync(endpoint);
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
        await messages.AddAsync([message]);

        var delivered = new TaskCompletionSource();
        var channel = new SignalingChannel(new DeliveryResult(true, 200, null), () => delivered.TrySetResult());
        var host = new DispatcherHost(Delivery(messages, endpoints, channel, options), options, NullLogger<DispatcherHost>.Instance);

        await host.StartAsync(CancellationToken.None);
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await host.StopAsync(CancellationToken.None);

        message.Status.Should().Be(DeliveryStatus.Delivered);
    }

    [Fact]
    public async Task Starts_and_stops_cleanly_with_no_work()
    {
        var clock = new FakeTimeProvider(Now);
        var options = new CaliberWebhooksOptions { TimeProvider = clock };
        var channel = new SignalingChannel(new DeliveryResult(true, 200, null), () => { });
        var host = new DispatcherHost(
            Delivery(new InMemoryMessageStore(clock), new InMemoryEndpointStore(), channel, options),
            options,
            NullLogger<DispatcherHost>.Instance);

        await host.StartAsync(CancellationToken.None);
        await host.StopAsync(CancellationToken.None);

        host.ExecuteTask.Should().NotBeNull();
    }
}
