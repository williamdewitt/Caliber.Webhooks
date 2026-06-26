using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Caliber.Webhooks.Tests;

public sealed class DeliveryManagerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class FakeDeliveryChannel(Func<WebhookMessage, DeliveryResult> respond) : IDeliveryChannel
    {
        public List<WebhookSignatureHeaders> Sent { get; } = [];

        public Task<DeliveryResult> SendAsync(
            Endpoint endpoint, WebhookMessage message, WebhookSignatureHeaders headers, CancellationToken ct = default)
        {
            Sent.Add(headers);
            return Task.FromResult(respond(message));
        }
    }

    // Wraps a real store, capturing the owner the manager claims with — proves the configured claim
    // token (not a fresh random one) is used, which is what keeps multiple dispatchers disjoint.
    private sealed class OwnerCapturingStore(IMessageStore inner) : IMessageStore
    {
        public string? LastOwner { get; private set; }

        public Task<int> AddAsync(IReadOnlyCollection<WebhookMessage> messages, CancellationToken ct = default)
            => inner.AddAsync(messages, ct);

        public Task<IReadOnlyList<WebhookMessage>> ClaimDueAsync(int batchSize, TimeSpan lease, string owner, CancellationToken ct = default)
        {
            LastOwner = owner;
            return inner.ClaimDueAsync(batchSize, lease, owner, ct);
        }

        public Task MarkDeliveredAsync(Guid messageId, CancellationToken ct = default) => inner.MarkDeliveredAsync(messageId, ct);

        public Task RescheduleAsync(Guid messageId, int attemptCount, DateTimeOffset nextAttemptAt, string error, CancellationToken ct = default)
            => inner.RescheduleAsync(messageId, attemptCount, nextAttemptAt, error, ct);

        public Task DeadLetterAsync(Guid messageId, int attemptCount, string error, CancellationToken ct = default)
            => inner.DeadLetterAsync(messageId, attemptCount, error, ct);
    }

    private sealed record Harness(
        DeliveryManager Manager,
        InMemoryMessageStore Messages,
        InMemoryEndpointStore Endpoints,
        FakeDeliveryChannel Channel,
        FakeTimeProvider Clock,
        Endpoint Endpoint);

    private static async Task<Harness> BuildAsync(Func<WebhookMessage, DeliveryResult> respond, int maxAttempts = 12)
    {
        var clock = new FakeTimeProvider(Now);
        var options = new CaliberWebhooksOptions { TimeProvider = clock, MaxAttempts = maxAttempts };
        var messages = new InMemoryMessageStore(clock);
        var endpoints = new InMemoryEndpointStore();
        var endpoint = new Endpoint { Id = Guid.NewGuid(), Url = "https://acme.example/hooks", Secret = WebhookSecret.Generate() };
        await endpoints.UpsertAsync(endpoint);
        var channel = new FakeDeliveryChannel(respond);
        var manager = new DeliveryManager(
            messages, endpoints, new SigningEngine(clock), new RetryEngine(options, () => 0.5), channel, options, owner: "test");
        return new Harness(manager, messages, endpoints, channel, clock, endpoint);
    }

    private static async Task<WebhookMessage> EnqueueAsync(Harness h)
    {
        var message = new WebhookMessage
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            EndpointId = h.Endpoint.Id,
            EventType = "order.shipped",
            Payload = "{}",
            CreatedAt = Now,
            NextAttemptAt = Now,
        };
        await h.Messages.AddAsync([message]);
        return message;
    }

    [Fact]
    public async Task Successful_delivery_marks_the_message_delivered()
    {
        var h = await BuildAsync(_ => new DeliveryResult(true, 200, null));
        var message = await EnqueueAsync(h);

        var count = await h.Manager.DeliverDueAsync();

        count.Should().Be(1);
        message.Status.Should().Be(DeliveryStatus.Delivered);
    }

    [Fact]
    public async Task Failed_delivery_reschedules_and_increments_the_attempt()
    {
        var h = await BuildAsync(_ => new DeliveryResult(false, 500, "HTTP 500"));
        var message = await EnqueueAsync(h);

        await h.Manager.DeliverDueAsync();

        message.Status.Should().Be(DeliveryStatus.Pending);
        message.AttemptCount.Should().Be(1);
        message.LastError.Should().Be("HTTP 500");
        message.NextAttemptAt.Should().BeAfter(Now);
    }

    [Fact]
    public async Task Failure_on_the_final_attempt_dead_letters()
    {
        var h = await BuildAsync(_ => new DeliveryResult(false, 500, "HTTP 500"), maxAttempts: 1);
        var message = await EnqueueAsync(h);

        await h.Manager.DeliverDueAsync();

        message.Status.Should().Be(DeliveryStatus.DeadLettered);
        message.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task A_missing_endpoint_dead_letters_the_message()
    {
        var h = await BuildAsync(_ => new DeliveryResult(true, 200, null));
        await h.Endpoints.DisableAsync(h.Endpoint.Id); // disabled drops from matching, but a queued message still resolves it...
        var message = new WebhookMessage
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            EndpointId = Guid.NewGuid(), // endpoint that does not exist
            EventType = "order.shipped",
            Payload = "{}",
            CreatedAt = Now,
            NextAttemptAt = Now,
        };
        await h.Messages.AddAsync([message]);

        await h.Manager.DeliverDueAsync();

        message.Status.Should().Be(DeliveryStatus.DeadLettered);
        message.LastError.Should().Contain("Endpoint");
    }

    [Fact]
    public async Task An_exception_from_the_channel_is_recorded_as_a_failed_attempt()
    {
        var h = await BuildAsync(_ => throw new InvalidOperationException("boom"));
        var message = await EnqueueAsync(h);

        await h.Manager.DeliverDueAsync();

        message.Status.Should().Be(DeliveryStatus.Pending);
        message.LastError.Should().Be("boom");
    }

    [Fact]
    public async Task Webhook_id_is_stable_across_retries()
    {
        var h = await BuildAsync(_ => new DeliveryResult(false, 500, "HTTP 500"));
        var message = await EnqueueAsync(h);

        await h.Manager.DeliverDueAsync();        // attempt 1 -> reschedule (+5s at jitter midpoint)
        h.Clock.Advance(TimeSpan.FromHours(1));   // make it due again
        await h.Manager.DeliverDueAsync();        // attempt 2

        h.Channel.Sent.Should().HaveCount(2);
        h.Channel.Sent.Select(s => s.Id).Distinct(StringComparer.Ordinal).Should().ContainSingle()
            .Which.Should().Be(message.Id.ToString());
    }

    [Fact]
    public async Task With_no_due_messages_it_delivers_nothing()
    {
        var h = await BuildAsync(_ => new DeliveryResult(true, 200, null));

        var count = await h.Manager.DeliverDueAsync();

        count.Should().Be(0);
        h.Channel.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task A_successful_delivery_does_not_reschedule_or_dead_letter()
    {
        var h = await BuildAsync(_ => new DeliveryResult(true, 200, null));
        var message = await EnqueueAsync(h);

        await h.Manager.DeliverDueAsync();

        message.Status.Should().Be(DeliveryStatus.Delivered);
        message.AttemptCount.Should().Be(0);
        message.NextAttemptAt.Should().Be(Now);
        message.LastError.Should().BeNull();
    }

    [Fact]
    public async Task The_configured_owner_is_used_as_the_claim_token()
    {
        var clock = new FakeTimeProvider(Now);
        var options = new CaliberWebhooksOptions { TimeProvider = clock };
        var spy = new OwnerCapturingStore(new InMemoryMessageStore(clock));
        var endpoints = new InMemoryEndpointStore();
        var manager = new DeliveryManager(
            spy, endpoints, new SigningEngine(clock), new RetryEngine(options, () => 0.5),
            new FakeDeliveryChannel(_ => new DeliveryResult(true, 200, null)), options, owner: "dispatcher-A");

        await manager.DeliverDueAsync();

        spy.LastOwner.Should().Be("dispatcher-A");
    }

    [Fact]
    public async Task A_batch_larger_than_the_concurrency_limit_is_fully_delivered()
    {
        // MaxConcurrency = 1 forces every delivery through the same semaphore permit; if the permit is
        // not released after each send the second claim would block forever, so a full drain proves it is.
        var clock = new FakeTimeProvider(Now);
        var options = new CaliberWebhooksOptions { TimeProvider = clock, MaxConcurrency = 1 };
        var messages = new InMemoryMessageStore(clock);
        var endpoints = new InMemoryEndpointStore();
        var endpoint = new Endpoint { Id = Guid.NewGuid(), Url = "https://acme.example/hooks", Secret = WebhookSecret.Generate() };
        await endpoints.UpsertAsync(endpoint);
        var manager = new DeliveryManager(
            messages, endpoints, new SigningEngine(clock), new RetryEngine(options, () => 0.5),
            new FakeDeliveryChannel(_ => new DeliveryResult(true, 200, null)), options, owner: "t");
        var jobs = Enumerable.Range(0, 3).Select(_ => new WebhookMessage
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            EndpointId = endpoint.Id,
            EventType = "order.shipped",
            Payload = "{}",
            CreatedAt = Now,
            NextAttemptAt = Now,
        }).ToArray();
        await messages.AddAsync(jobs);

        var count = await manager.DeliverDueAsync();

        count.Should().Be(3);
        jobs.Should().AllSatisfy(j => j.Status.Should().Be(DeliveryStatus.Delivered));
    }
}
