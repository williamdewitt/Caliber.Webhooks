using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Caliber.Webhooks.Tests;

public sealed class InMemoryMessageStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(60);

    private static WebhookMessage Message(DateTimeOffset nextAttemptAt, Guid? eventId = null, Guid? endpointId = null) => new()
    {
        Id = Guid.NewGuid(),
        EventId = eventId ?? Guid.NewGuid(),
        EndpointId = endpointId ?? Guid.NewGuid(),
        EventType = "order.shipped",
        Payload = "{}",
        CreatedAt = Now,
        NextAttemptAt = nextAttemptAt,
    };

    [Fact]
    public async Task AddAsync_is_idempotent_on_event_and_endpoint()
    {
        var store = new InMemoryMessageStore(new FakeTimeProvider(Now));
        var eventId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();

        (await store.AddAsync([Message(Now, eventId, endpointId)])).Should().Be(1);
        (await store.AddAsync([Message(Now, eventId, endpointId)])).Should().Be(0);
    }

    [Fact]
    public async Task ClaimDueAsync_claims_due_pending_messages_and_leases_them()
    {
        var store = new InMemoryMessageStore(new FakeTimeProvider(Now));
        var message = Message(Now);
        await store.AddAsync([message]);

        var claimed = await store.ClaimDueAsync(batchSize: 10, Lease, owner: "d1");

        claimed.Should().ContainSingle();
        claimed[0].Id.Should().Be(message.Id);
        claimed[0].Owner.Should().Be("d1");
        claimed[0].LeaseUntil.Should().Be(Now + Lease);
    }

    [Fact]
    public async Task ClaimDueAsync_skips_messages_not_yet_due()
    {
        var store = new InMemoryMessageStore(new FakeTimeProvider(Now));
        await store.AddAsync([Message(Now + TimeSpan.FromMinutes(5))]);

        (await store.ClaimDueAsync(10, Lease, "d1")).Should().BeEmpty();
    }

    [Fact]
    public async Task ClaimDueAsync_does_not_reclaim_an_active_lease()
    {
        var store = new InMemoryMessageStore(new FakeTimeProvider(Now));
        await store.AddAsync([Message(Now)]);
        await store.ClaimDueAsync(10, Lease, "d1");

        (await store.ClaimDueAsync(10, Lease, "d2")).Should().BeEmpty();
    }

    [Fact]
    public async Task ClaimDueAsync_reclaims_an_expired_lease()
    {
        var clock = new FakeTimeProvider(Now);
        var store = new InMemoryMessageStore(clock);
        await store.AddAsync([Message(Now)]);
        await store.ClaimDueAsync(10, Lease, "d1");

        clock.Advance(Lease + TimeSpan.FromSeconds(1));

        var reclaimed = await store.ClaimDueAsync(10, Lease, "d2");
        reclaimed.Should().ContainSingle();
        reclaimed[0].Owner.Should().Be("d2");
    }

    [Fact]
    public async Task ClaimDueAsync_respects_batch_size_and_orders_by_next_attempt()
    {
        var store = new InMemoryMessageStore(new FakeTimeProvider(Now));
        var first = Message(Now - (2 * Minute));
        var second = Message(Now - Minute);
        var third = Message(Now);
        await store.AddAsync([third, first, second]);

        var claimed = await store.ClaimDueAsync(batchSize: 2, Lease, "d1");

        claimed.Select(c => c.Id).Should().Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task MarkDeliveredAsync_makes_a_message_terminal()
    {
        var clock = new FakeTimeProvider(Now);
        var store = new InMemoryMessageStore(clock);
        await store.AddAsync([Message(Now)]);
        var claimed = await store.ClaimDueAsync(10, Lease, "d1");

        await store.MarkDeliveredAsync(claimed[0].Id);
        clock.Advance(2 * Lease);

        (await store.ClaimDueAsync(10, Lease, "d2")).Should().BeEmpty();
    }

    [Fact]
    public async Task RescheduleAsync_returns_a_message_to_pending_at_a_new_time()
    {
        var clock = new FakeTimeProvider(Now);
        var store = new InMemoryMessageStore(clock);
        await store.AddAsync([Message(Now)]);
        var claimed = await store.ClaimDueAsync(10, Lease, "d1");

        await store.RescheduleAsync(claimed[0].Id, attemptCount: 1, Now + (5 * Minute), "boom");

        (await store.ClaimDueAsync(10, Lease, "d2")).Should().BeEmpty();
        clock.Advance(5 * Minute);
        var again = await store.ClaimDueAsync(10, Lease, "d2");

        again.Should().ContainSingle();
        again[0].AttemptCount.Should().Be(1);
        again[0].LastError.Should().Be("boom");
    }

    [Fact]
    public async Task DeadLetterAsync_makes_a_message_terminal()
    {
        var clock = new FakeTimeProvider(Now);
        var store = new InMemoryMessageStore(clock);
        await store.AddAsync([Message(Now)]);
        var claimed = await store.ClaimDueAsync(10, Lease, "d1");

        await store.DeadLetterAsync(claimed[0].Id, attemptCount: 12, "exhausted");
        clock.Advance(10 * Minute);

        (await store.ClaimDueAsync(10, Lease, "d2")).Should().BeEmpty();
    }

    [Fact]
    public async Task ClaimDueAsync_never_double_claims_under_concurrency()
    {
        var store = new InMemoryMessageStore(new FakeTimeProvider(Now));
        var messages = Enumerable.Range(0, 200).Select(_ => Message(Now)).ToList();
        await store.AddAsync(messages);

        var workers = Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
        {
            var mine = new List<Guid>();
            IReadOnlyList<WebhookMessage> batch;
            while ((batch = await store.ClaimDueAsync(10, TimeSpan.FromMinutes(5), $"d{i}")).Count > 0)
            {
                mine.AddRange(batch.Select(b => b.Id));
            }

            return mine;
        })).ToList();

        var claimedIds = (await Task.WhenAll(workers)).SelectMany(ids => ids).ToList();

        claimedIds.Should().HaveCount(200);
        claimedIds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task AddAsync_rejects_null_messages()
    {
        var store = new InMemoryMessageStore(new FakeTimeProvider(Now));
        var act = async () => await store.AddAsync(null!);
        (await act.Should().ThrowAsync<ArgumentNullException>()).Which.ParamName.Should().Be("messages");
    }

    [Fact]
    public async Task ClaimDueAsync_rejects_a_non_positive_batch_size()
    {
        var store = new InMemoryMessageStore(new FakeTimeProvider(Now));
        var act = async () => await store.ClaimDueAsync(0, Lease, "d1");
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task ClaimDueAsync_rejects_a_null_or_empty_owner(string? owner)
    {
        var store = new InMemoryMessageStore(new FakeTimeProvider(Now));
        var act = async () => await store.ClaimDueAsync(10, Lease, owner!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ClaimDueAsync_does_not_reclaim_at_the_exact_moment_the_lease_expires()
    {
        var clock = new FakeTimeProvider(Now);
        var store = new InMemoryMessageStore(clock);
        await store.AddAsync([Message(Now)]);
        await store.ClaimDueAsync(10, Lease, "d1");

        clock.Advance(Lease); // now == LeaseUntil exactly — the lease has not yet lapsed

        (await store.ClaimDueAsync(10, Lease, "d2")).Should().BeEmpty();
    }
}
