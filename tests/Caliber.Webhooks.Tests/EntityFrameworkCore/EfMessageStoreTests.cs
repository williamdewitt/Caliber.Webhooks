using AwesomeAssertions;
using Caliber.Webhooks.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Caliber.Webhooks.Tests;

// Mirrors InMemoryMessageStoreTests, but against a real SQLite file so the SQL claim path
// (UPDATE … RETURNING), the idempotent ON CONFLICT insert, and no-double-claim under concurrent
// connections are all exercised end-to-end.
public sealed class EfMessageStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(60);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"caliber-efmsgstore-{Guid.NewGuid():N}.db");
    private readonly FakeTimeProvider _clock = new(Now);
    private FileDbContextFactory _factory = null!;
    private EfMessageStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = new FileDbContextFactory(_dbPath);
        await using var context = _factory.CreateDbContext();
        await context.Database.EnsureCreatedAsync();
        _store = new EfMessageStore(_factory, _clock);
    }

    public ValueTask DisposeAsync()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public void Constructor_rejects_a_null_context_factory()
    {
        var act = () => new EfMessageStore(null!, _clock);
        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("contextFactory");
    }

    [Fact]
    public void Constructor_rejects_a_null_clock()
    {
        var act = () => new EfMessageStore(_factory, null!);
        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("timeProvider");
    }

    [Fact]
    public async Task AddAsync_is_idempotent_on_event_and_endpoint()
    {
        var eventId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();

        (await _store.AddAsync([Message(Now, eventId, endpointId)])).Should().Be(1);
        (await _store.AddAsync([Message(Now, eventId, endpointId)])).Should().Be(0);
    }

    [Fact]
    public async Task AddAsync_returns_zero_for_an_empty_batch()
    {
        (await _store.AddAsync([])).Should().Be(0);
    }

    [Fact]
    public async Task AddAsync_counts_only_the_rows_actually_inserted()
    {
        var eventId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        await _store.AddAsync([Message(Now, eventId, endpointId)]);

        // One duplicate (skipped) and one fresh row → exactly one inserted.
        var inserted = await _store.AddAsync([Message(Now, eventId, endpointId), Message(Now)]);

        inserted.Should().Be(1);
    }

    [Fact]
    public async Task ClaimDueAsync_claims_due_pending_messages_and_leases_them()
    {
        var message = Message(Now);
        await _store.AddAsync([message]);

        var claimed = await _store.ClaimDueAsync(batchSize: 10, Lease, owner: "d1");

        claimed.Should().ContainSingle();
        claimed[0].Id.Should().Be(message.Id);
        claimed[0].Owner.Should().Be("d1");
        claimed[0].LeaseUntil.Should().Be(Now + Lease);
    }

    [Fact]
    public async Task ClaimDueAsync_round_trips_the_full_message()
    {
        var message = Message(Now);
        await _store.AddAsync([message]);

        var claimed = (await _store.ClaimDueAsync(10, Lease, "d1")).Single();

        claimed.Should().BeEquivalentTo(
            message,
            options => options.Excluding(m => m.Owner).Excluding(m => m.LeaseUntil));
    }

    [Fact]
    public async Task ClaimDueAsync_skips_messages_not_yet_due()
    {
        await _store.AddAsync([Message(Now + TimeSpan.FromMinutes(5))]);

        (await _store.ClaimDueAsync(10, Lease, "d1")).Should().BeEmpty();
    }

    [Fact]
    public async Task ClaimDueAsync_does_not_reclaim_an_active_lease()
    {
        await _store.AddAsync([Message(Now)]);
        await _store.ClaimDueAsync(10, Lease, "d1");

        (await _store.ClaimDueAsync(10, Lease, "d2")).Should().BeEmpty();
    }

    [Fact]
    public async Task ClaimDueAsync_reclaims_an_expired_lease()
    {
        await _store.AddAsync([Message(Now)]);
        await _store.ClaimDueAsync(10, Lease, "d1");

        _clock.Advance(Lease + TimeSpan.FromSeconds(1));

        var reclaimed = await _store.ClaimDueAsync(10, Lease, "d2");
        reclaimed.Should().ContainSingle();
        reclaimed[0].Owner.Should().Be("d2");
    }

    [Fact]
    public async Task ClaimDueAsync_does_not_reclaim_at_the_exact_moment_the_lease_expires()
    {
        await _store.AddAsync([Message(Now)]);
        await _store.ClaimDueAsync(10, Lease, "d1");

        _clock.Advance(Lease); // now == LeaseUntil exactly — the lease has not yet lapsed

        (await _store.ClaimDueAsync(10, Lease, "d2")).Should().BeEmpty();
    }

    [Fact]
    public async Task ClaimDueAsync_respects_batch_size_and_orders_by_next_attempt()
    {
        var first = Message(Now - (2 * Minute));
        var second = Message(Now - Minute);
        var third = Message(Now);
        await _store.AddAsync([third, first, second]);

        var claimed = await _store.ClaimDueAsync(batchSize: 2, Lease, "d1");

        claimed.Select(c => c.Id).Should().Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task MarkDeliveredAsync_makes_a_message_terminal()
    {
        await _store.AddAsync([Message(Now)]);
        var claimed = await _store.ClaimDueAsync(10, Lease, "d1");

        await _store.MarkDeliveredAsync(claimed[0].Id);
        _clock.Advance(2 * Lease);

        (await _store.ClaimDueAsync(10, Lease, "d2")).Should().BeEmpty();
    }

    [Fact]
    public async Task RescheduleAsync_returns_a_message_to_pending_at_a_new_time()
    {
        await _store.AddAsync([Message(Now)]);
        var claimed = await _store.ClaimDueAsync(10, Lease, "d1");

        await _store.RescheduleAsync(claimed[0].Id, attemptCount: 1, Now + (5 * Minute), "boom");

        (await _store.ClaimDueAsync(10, Lease, "d2")).Should().BeEmpty();
        _clock.Advance(5 * Minute);
        var again = await _store.ClaimDueAsync(10, Lease, "d2");

        again.Should().ContainSingle();
        again[0].AttemptCount.Should().Be(1);
        again[0].LastError.Should().Be("boom");
    }

    [Fact]
    public async Task DeadLetterAsync_makes_a_message_terminal()
    {
        await _store.AddAsync([Message(Now)]);
        var claimed = await _store.ClaimDueAsync(10, Lease, "d1");

        await _store.DeadLetterAsync(claimed[0].Id, attemptCount: 12, "exhausted");
        _clock.Advance(10 * Minute);

        (await _store.ClaimDueAsync(10, Lease, "d2")).Should().BeEmpty();
    }

    [Fact]
    public async Task ClaimDueAsync_never_double_claims_under_concurrency()
    {
        var messages = Enumerable.Range(0, 200).Select(_ => Message(Now)).ToList();
        await _store.AddAsync(messages);

        var workers = Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
        {
            var mine = new List<Guid>();
            IReadOnlyList<WebhookMessage> batch;
            while ((batch = await _store.ClaimDueAsync(10, TimeSpan.FromMinutes(5), $"d{i}")).Count > 0)
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
        var act = async () => await _store.AddAsync(null!);
        (await act.Should().ThrowAsync<ArgumentNullException>()).Which.ParamName.Should().Be("messages");
    }

    [Fact]
    public async Task ClaimDueAsync_rejects_a_non_positive_batch_size()
    {
        var act = async () => await _store.ClaimDueAsync(0, Lease, "d1");
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task ClaimDueAsync_rejects_a_null_or_empty_owner(string? owner)
    {
        var act = async () => await _store.ClaimDueAsync(10, Lease, owner!);
        await act.Should().ThrowAsync<ArgumentException>();
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

    // Hands the store a fresh context per call (DbContext is not thread-safe); Pooling=False so the
    // file handle is released on dispose and the temp database can be deleted after the test.
    private sealed class FileDbContextFactory(string dbPath) : IDbContextFactory<CaliberWebhooksDbContext>
    {
        private readonly DbContextOptions<CaliberWebhooksDbContext> _options =
            new DbContextOptionsBuilder<CaliberWebhooksDbContext>()
                .UseSqlite($"Data Source={dbPath};Pooling=False")
                .Options;

        public CaliberWebhooksDbContext CreateDbContext() => new(_options);
    }
}
