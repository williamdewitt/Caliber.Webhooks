using AwesomeAssertions;
using Caliber.Webhooks.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Caliber.Webhooks.IntegrationTests;

/// <summary>
/// The cross-instance correctness proof for the durable Postgres store (#38/#39): real concurrent
/// dispatchers against a real server, where <c>FOR UPDATE SKIP LOCKED</c> is the actual mechanism that
/// keeps claims disjoint — unlike SQLite, whose single writer serialises the equivalent statement.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresMessageStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private readonly PostgresFixture _fx;

    public PostgresMessageStoreTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Concurrent_dispatchers_claim_every_message_exactly_once_no_double_send()
    {
        Assert.SkipUnless(_fx.Available, PostgresFixture.SkipReason);
        await _fx.ResetAsync();

        var clock = new FakeTimeProvider(Now);
        var factory = _fx.MessagesFactory();

        // One shared backlog of due messages.
        var seeded = Enumerable.Range(0, 500).Select(_ => Message(Now)).ToList();
        await new EfMessageStore(factory, clock).AddAsync(seeded);

        // N independent dispatcher instances drain the same Postgres concurrently. FOR UPDATE SKIP LOCKED
        // must partition the backlog so the union of every claim is the whole backlog with no overlap.
        const int dispatchers = 8;
        var workers = Enumerable.Range(0, dispatchers).Select(i => Task.Run(async () =>
        {
            var store = new EfMessageStore(factory, clock);
            var mine = new List<Guid>();
            IReadOnlyList<WebhookMessage> batch;
            while ((batch = await store.ClaimDueAsync(batchSize: 16, Lease, $"d{i}")).Count > 0)
            {
                mine.AddRange(batch.Select(m => m.Id));
            }

            return mine;
        })).ToList();

        var claimed = (await Task.WhenAll(workers)).SelectMany(ids => ids).ToList();

        claimed.Should().HaveCount(seeded.Count, "every seeded message is claimed exactly once");
        claimed.Should().OnlyHaveUniqueItems("no message is claimed by two dispatchers (no double-send)");
        claimed.Should().BeEquivalentTo(seeded.Select(m => m.Id));
    }

    [Fact]
    public async Task A_crashed_dispatchers_lease_is_reclaimed_and_its_work_redelivered()
    {
        Assert.SkipUnless(_fx.Available, PostgresFixture.SkipReason);
        await _fx.ResetAsync();

        var clock = new FakeTimeProvider(Now);
        var store = new EfMessageStore(_fx.MessagesFactory(), clock);
        var message = Message(Now);
        await store.AddAsync([message]);

        // d1 claims, then "crashes" — it never marks the message delivered.
        (await store.ClaimDueAsync(10, Lease, "d1")).Should().ContainSingle().Which.Id.Should().Be(message.Id);

        // While d1's lease holds, no other dispatcher can take the in-flight work.
        (await store.ClaimDueAsync(10, Lease, "d2")).Should().BeEmpty("the lease is still active");

        // Once the lease lapses, the claim predicate alone makes it reclaimable — d2 redelivers it.
        clock.Advance(Lease + TimeSpan.FromSeconds(1));
        var reclaimed = await store.ClaimDueAsync(10, Lease, "d2");
        reclaimed.Should().ContainSingle();
        reclaimed[0].Id.Should().Be(message.Id);
        reclaimed[0].Owner.Should().Be("d2");
    }

    [Fact]
    public async Task Concurrent_fan_out_of_the_same_event_inserts_exactly_one_row()
    {
        Assert.SkipUnless(_fx.Available, PostgresFixture.SkipReason);
        await _fx.ResetAsync();

        var clock = new FakeTimeProvider(Now);
        var eventId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();

        // Many relays race to fan the same (event_id, endpoint_id) out: ON CONFLICT DO NOTHING admits one
        // and no-ops the rest — idempotent fan-out under real write concurrency.
        var inserted = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
            await new EfMessageStore(_fx.MessagesFactory(), clock).AddAsync([Message(Now, eventId, endpointId)]))));

        inserted.Sum().Should().Be(1, "the unique (event_id, endpoint_id) index admits exactly one insert");

        await using var context = _fx.NewMessagesContext();
        (await context.Messages.CountAsync()).Should().Be(1);
    }

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
}
