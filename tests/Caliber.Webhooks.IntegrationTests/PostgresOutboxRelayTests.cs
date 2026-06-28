using System.Data.Common;
using AwesomeAssertions;
using Caliber.Webhooks.EntityFrameworkCore;
using Caliber.Webhooks.EntityFrameworkCore.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Time.Testing;

namespace Caliber.Webhooks.IntegrationTests;

/// <summary>
/// The outbox (relay) path on real Postgres: a caller-owned <c>caliber_outbox</c> sharing the database with
/// Caliber's migrated <c>messages</c>/<c>endpoints</c>. Proves the relay fans each event out to every
/// subscriber exactly once and that a mid-relay crash (re-drain of the same event) never double-sends —
/// the fan-out idempotency that the at-least-once delivery contract rests on. Exercises both
/// <see cref="EfEndpointStore"/> and <see cref="EfMessageStore"/> against Npgsql.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresOutboxRelayTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _fx;
    private readonly FakeTimeProvider _clock = new(Now);
    private DbContextOptions<CallerDbContext> _callerOptions = null!;
    private CaliberWebhooksOptions _caliber = null!;

    public PostgresOutboxRelayTests(PostgresFixture fx) => _fx = fx;

    public async ValueTask InitializeAsync()
    {
        if (!_fx.Available)
        {
            return; // each test Assert.SkipUnless-es; nothing to provision when Docker is absent
        }

        var builder = new DbContextOptionsBuilder<CallerDbContext>();
        CaliberNpgsqlConfiguration.Apply(builder, _fx.ConnectionString);
        _callerOptions = builder.Options;
        _caliber = new CaliberWebhooksOptions { TimeProvider = _clock };

        await using var context = NewCaller();
        await EnsureOutboxTableAsync(context);

        await _fx.ResetAsync(); // messages + endpoints
        await context.Set<CaliberOutboxMessage>().ExecuteDeleteAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task The_relay_fans_each_event_out_to_every_subscriber_exactly_once()
    {
        Assert.SkipUnless(_fx.Available, PostgresFixture.SkipReason);

        var endpoints = new EfEndpointStore(_fx.MessagesFactory());
        var a = Subscriber("order.shipped");
        var b = Subscriber("order.shipped");
        await endpoints.UpsertAsync(a);
        await endpoints.UpsertAsync(b);

        var eventIds = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in eventIds)
        {
            await StageAsync(id, "order.shipped");
        }

        await DrainAsync(endpoints);

        await using var read = _fx.NewMessagesContext();
        (await read.Messages.CountAsync()).Should().Be(eventIds.Count * 2, "every event fans out to both subscribers");
        (await read.Messages.CountAsync(m => m.EndpointId == a.Id)).Should().Be(eventIds.Count);
        (await read.Messages.CountAsync(m => m.EndpointId == b.Id)).Should().Be(eventIds.Count);
    }

    [Fact]
    public async Task A_redrained_event_after_a_mid_relay_crash_is_not_double_sent()
    {
        Assert.SkipUnless(_fx.Available, PostgresFixture.SkipReason);

        var endpoints = new EfEndpointStore(_fx.MessagesFactory());
        await endpoints.UpsertAsync(Subscriber("order.shipped"));

        var eventId = Guid.NewGuid();
        await StageAsync(eventId, "order.shipped");
        await DrainAsync(endpoints);

        // Simulate a relay that fanned out but crashed before deleting the outbox row: the same event id is
        // staged and redrained. The (event_id, endpoint_id) insert is idempotent, so there is no duplicate.
        await StageAsync(eventId, "order.shipped");
        await DrainAsync(endpoints);

        await using var read = _fx.NewMessagesContext();
        (await read.Messages.CountAsync(m => m.EventId == eventId)).Should().Be(1);
    }

    private async Task DrainAsync(IEndpointStore endpoints)
    {
        var messages = new EfMessageStore(_fx.MessagesFactory(), _clock);
        var processor = new RelayProcessor(messages, endpoints, new MatchingEngine(), _caliber);

        // A fresh caller context per batch (matches the relay host) so tracking never accumulates.
        int processed;
        do
        {
            await using var context = NewCaller();
            processed = await processor.RelayBatchAsync(context);
        }
        while (processed > 0);
    }

    private async Task StageAsync(Guid id, string eventType)
    {
        await using var context = NewCaller();
        context.Add(new CaliberOutboxMessage { Id = id, EventType = eventType, Payload = "{}", CreatedAt = Now });
        await context.SaveChangesAsync();
    }

    // Provisions the caller's caliber_outbox table on first use, mirroring CaliberWebhooksSharedSchemaInitializer:
    // probe the table, and create just this context's model (the outbox) when it is absent.
    private static async Task EnsureOutboxTableAsync(CallerDbContext context)
    {
        try
        {
            await context.Set<CaliberOutboxMessage>().AnyAsync();
            return;
        }
        catch (DbException)
        {
            // caliber_outbox not provisioned yet.
        }

        await context.Database.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }

    private CallerDbContext NewCaller() => new(_callerOptions);

    private static Endpoint Subscriber(params string[] eventTypes) => new()
    {
        Id = Guid.NewGuid(),
        Url = "https://acme.example/hooks",
        Secret = "whsec_x",
        EventTypes = eventTypes,
    };

    private sealed class CallerDbContext(DbContextOptions<CallerDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);
            modelBuilder.AddCaliberOutbox();
        }
    }
}
