using AwesomeAssertions;
using Caliber.Webhooks.EntityFrameworkCore;
using Caliber.Webhooks.EntityFrameworkCore.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Caliber.Webhooks.Tests;

// Exercises the transactional-outbox path end-to-end: OutboxPublisher staging into a real SQLite-backed
// caller DbContext (AddCaliberOutbox), and the RelayProcessor fanning out into the messages store,
// including the idempotent re-drain that makes a mid-relay crash safe.
public sealed class OutboxRelayTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"caliber-outbox-{Guid.NewGuid():N}.db");
    private readonly FakeTimeProvider _clock = new(Now);
    private DbContextOptions<TestOutboxDbContext> _options = null!;
    private CaliberWebhooksOptions _caliber = null!;

    public async ValueTask InitializeAsync()
    {
        _options = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=False")
            .Options;
        _caliber = new CaliberWebhooksOptions { TimeProvider = _clock };

        await using var context = NewContext();
        await context.Database.EnsureCreatedAsync();
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
    public async Task PublishAsync_stages_the_event_but_does_not_commit_it()
    {
        await using var context = NewContext();
        var publisher = new OutboxPublisher<TestOutboxDbContext>(context, _caliber);

        await publisher.PublishAsync("order.shipped", new { orderId = 1 });

        // Staged into the change tracker, not the database — the caller's SaveChangesAsync commits it.
        (await context.Set<CaliberOutboxMessage>().CountAsync()).Should().Be(0);

        await context.SaveChangesAsync();

        (await context.Set<CaliberOutboxMessage>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_serializes_the_payload_and_stamps_the_clock()
    {
        await using (var context = NewContext())
        {
            await new OutboxPublisher<TestOutboxDbContext>(context, _caliber)
                .PublishAsync("order.shipped", new { orderId = 7 });
            await context.SaveChangesAsync();
        }

        await using var read = NewContext();
        var row = await read.Set<CaliberOutboxMessage>().SingleAsync();
        row.EventType.Should().Be("order.shipped");
        row.Payload.Should().Be("""{"orderId":7}""");
        row.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public async Task RelayBatchAsync_fans_out_a_staged_event_and_drains_the_outbox()
    {
        var messages = new InMemoryMessageStore(_clock);
        var endpoints = new InMemoryEndpointStore();
        var endpoint = Subscriber("order.shipped");
        await endpoints.UpsertAsync(endpoint);
        await StageAsync(Guid.NewGuid(), "order.shipped");

        int processed;
        await using (var context = NewContext())
        {
            processed = await NewProcessor(messages, endpoints).RelayBatchAsync(context);
        }

        processed.Should().Be(1);
        await using (var context = NewContext())
        {
            (await context.Set<CaliberOutboxMessage>().CountAsync()).Should().Be(0);
        }

        var claimed = await messages.ClaimDueAsync(10, TimeSpan.FromMinutes(1), "d1");
        claimed.Should().ContainSingle();
        claimed[0].EndpointId.Should().Be(endpoint.Id);
        claimed[0].EventType.Should().Be("order.shipped");
    }

    [Fact]
    public async Task RelayBatchAsync_fans_out_to_every_matching_endpoint()
    {
        var messages = new InMemoryMessageStore(_clock);
        var endpoints = new InMemoryEndpointStore();
        var a = Subscriber("order.shipped");
        var b = Subscriber("order.shipped");
        await endpoints.UpsertAsync(a);
        await endpoints.UpsertAsync(b);
        var eventId = Guid.NewGuid();
        await StageAsync(eventId, "order.shipped");

        await using (var context = NewContext())
        {
            await NewProcessor(messages, endpoints).RelayBatchAsync(context);
        }

        var claimed = await messages.ClaimDueAsync(10, TimeSpan.FromMinutes(1), "d1");
        claimed.Should().HaveCount(2);
        claimed.Should().OnlyContain(m => m.EventId == eventId);
        claimed.Select(m => m.EndpointId).Should().BeEquivalentTo([a.Id, b.Id]);
    }

    [Fact]
    public async Task RelayBatchAsync_is_idempotent_when_the_same_event_is_redrained()
    {
        var messages = new InMemoryMessageStore(_clock);
        var endpoints = new InMemoryEndpointStore();
        await endpoints.UpsertAsync(Subscriber("order.shipped"));
        var processor = NewProcessor(messages, endpoints);
        var eventId = Guid.NewGuid();

        await StageAsync(eventId, "order.shipped");
        await using (var context = NewContext())
        {
            await processor.RelayBatchAsync(context);
        }

        // Simulate a relay that fanned out but crashed before deleting the outbox row: the same event id
        // is staged again and redrained. The (event_id, endpoint_id) insert is idempotent, so no duplicate.
        await StageAsync(eventId, "order.shipped");
        await using (var context = NewContext())
        {
            await processor.RelayBatchAsync(context);
        }

        var claimed = await messages.ClaimDueAsync(100, TimeSpan.FromMinutes(1), "d1");
        claimed.Where(m => m.EventId == eventId).Should().ContainSingle();
    }

    [Fact]
    public async Task RelayBatchAsync_drains_an_unmatched_event_without_enqueuing()
    {
        var messages = new InMemoryMessageStore(_clock);
        var endpoints = new InMemoryEndpointStore();
        await endpoints.UpsertAsync(Subscriber("order.cancelled"));
        await StageAsync(Guid.NewGuid(), "order.shipped");

        int processed;
        await using (var context = NewContext())
        {
            processed = await NewProcessor(messages, endpoints).RelayBatchAsync(context);
        }

        processed.Should().Be(1); // the row is consumed so it can never be relayed twice
        await using (var context = NewContext())
        {
            (await context.Set<CaliberOutboxMessage>().CountAsync()).Should().Be(0);
        }

        (await messages.ClaimDueAsync(10, TimeSpan.FromMinutes(1), "d1")).Should().BeEmpty();
    }

    [Fact]
    public async Task RelayBatchAsync_returns_zero_for_an_empty_outbox()
    {
        await using var context = NewContext();
        (await NewProcessor(new InMemoryMessageStore(_clock), new InMemoryEndpointStore())
            .RelayBatchAsync(context)).Should().Be(0);
    }

    [Fact]
    public async Task RelayBatchAsync_orders_the_drain_oldest_first()
    {
        var messages = new InMemoryMessageStore(_clock);
        var endpoints = new InMemoryEndpointStore();
        await endpoints.UpsertAsync(Subscriber("order.shipped"));
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        await StageAsync(newer, "order.shipped", Now + TimeSpan.FromMinutes(1));
        await StageAsync(older, "order.shipped", Now);

        var caliber = new CaliberWebhooksOptions { TimeProvider = _clock, BatchSize = 1 };
        await using (var context = NewContext())
        {
            await new RelayProcessor(messages, endpoints, new MatchingEngine(), caliber).RelayBatchAsync(context);
        }

        var claimed = await messages.ClaimDueAsync(10, TimeSpan.FromMinutes(1), "d1");
        claimed.Should().ContainSingle().Which.EventId.Should().Be(older);
    }

    [Fact]
    public async Task RelayBatchAsync_rejects_a_null_context()
    {
        var act = async () => await NewProcessor(new InMemoryMessageStore(_clock), new InMemoryEndpointStore())
            .RelayBatchAsync(null!);
        (await act.Should().ThrowAsync<ArgumentNullException>()).Which.ParamName.Should().Be("context");
    }

    [Fact]
    public void Constructors_reject_null_dependencies()
    {
        var messages = new InMemoryMessageStore(_clock);
        var endpoints = new InMemoryEndpointStore();
        var matching = new MatchingEngine();

        ((Func<RelayProcessor>)(() => new RelayProcessor(null!, endpoints, matching, _caliber)))
            .Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("messages");
        ((Func<RelayProcessor>)(() => new RelayProcessor(messages, null!, matching, _caliber)))
            .Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("endpoints");
        ((Func<RelayProcessor>)(() => new RelayProcessor(messages, endpoints, null!, _caliber)))
            .Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("matching");
        ((Func<RelayProcessor>)(() => new RelayProcessor(messages, endpoints, matching, null!)))
            .Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("options");
    }

    [Fact]
    public void UseEntityFramework_wires_outbox_mode()
    {
        var path = Path.Combine(Path.GetTempPath(), $"caliber-outbox-wiring-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<TestOutboxDbContext>(o => o.UseSqlite($"Data Source={path};Pooling=False"));
            services.AddCaliberWebhooks(o => o.UseEntityFramework<TestOutboxDbContext>());

            using var provider = services.BuildServiceProvider();

            provider.GetRequiredService<IMessageStore>().Should().BeOfType<EfMessageStore>();
            provider.GetRequiredService<IEndpointStore>().Should().BeOfType<EfEndpointStore>();
            var hosted = provider.GetServices<IHostedService>().ToList();
            hosted.Should().Contain(s => s is RelayHost<TestOutboxDbContext>);
            hosted.Should().Contain(s => s is CaliberWebhooksSharedSchemaInitializer);

            // The publisher is scoped (it stages into the caller's scoped context), so resolve within a scope.
            using var scope = provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<IWebhookPublisher>()
                .Should().BeOfType<OutboxPublisher<TestOutboxDbContext>>();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private TestOutboxDbContext NewContext() => new(_options);

    private RelayProcessor NewProcessor(IMessageStore messages, IEndpointStore endpoints)
        => new(messages, endpoints, new MatchingEngine(), _caliber);

    private static Endpoint Subscriber(params string[] eventTypes) => new()
    {
        Id = Guid.NewGuid(),
        Url = "https://acme.example/hooks",
        Secret = "whsec_x",
        EventTypes = eventTypes,
    };

    private async Task StageAsync(Guid id, string eventType, DateTimeOffset? createdAt = null)
    {
        await using var context = NewContext();
        context.Add(new CaliberOutboxMessage
        {
            Id = id,
            EventType = eventType,
            Payload = "{}",
            CreatedAt = createdAt ?? Now,
        });
        await context.SaveChangesAsync();
    }

    private sealed class TestOutboxDbContext(DbContextOptions<TestOutboxDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);
            modelBuilder.AddCaliberOutbox();
        }
    }
}
