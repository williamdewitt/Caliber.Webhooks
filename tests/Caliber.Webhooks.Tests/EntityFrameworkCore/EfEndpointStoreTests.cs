using AwesomeAssertions;
using Caliber.Webhooks.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Caliber.Webhooks.Tests;

// Mirrors InMemoryEndpointStoreTests, but against a real SQLite file so the ON CONFLICT upsert,
// the JSON round-trip for subscribed_event_types, and the ExecuteUpdateAsync disable path are all
// exercised end-to-end.
public sealed class EfEndpointStoreTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"caliber-efepstore-{Guid.NewGuid():N}.db");
    private FileDbContextFactory _factory = null!;
    private EfEndpointStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = new FileDbContextFactory(_dbPath);
        await using var context = _factory.CreateDbContext();
        await context.Database.EnsureCreatedAsync();
        _store = new EfEndpointStore(_factory);
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
        var act = () => new EfEndpointStore(null!);
        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("contextFactory");
    }

    [Fact]
    public async Task UpsertAsync_then_GetAsync_round_trips_all_fields()
    {
        var id = Guid.NewGuid();
        var endpoint = new Endpoint
        {
            Id = id,
            TenantKey = "tenant-1",
            Url = "https://acme.example/hooks",
            Secret = "whsec_abc",
            EventTypes = ["order.shipped", "order.cancelled"],
            Enabled = true,
            Description = "ACME orders",
        };

        await _store.UpsertAsync(endpoint);

        var got = await _store.GetAsync(id);
        got.Should().NotBeNull();
        got!.Id.Should().Be(endpoint.Id);
        got.TenantKey.Should().Be(endpoint.TenantKey);
        got.Url.Should().Be(endpoint.Url);
        got.Secret.Should().Be(endpoint.Secret);
        got.EventTypes.Should().BeEquivalentTo(endpoint.EventTypes);
        got.Enabled.Should().BeTrue();
        got.Description.Should().Be(endpoint.Description);
    }

    [Fact]
    public async Task UpsertAsync_round_trips_null_event_types_as_subscribe_all()
    {
        var id = Guid.NewGuid();
        await _store.UpsertAsync(new Endpoint { Id = id, Url = "https://a.example", Secret = "s", EventTypes = null });

        (await _store.GetAsync(id))!.EventTypes.Should().BeNull();
    }

    [Fact]
    public async Task UpsertAsync_replaces_an_existing_endpoint()
    {
        var id = Guid.NewGuid();
        await _store.UpsertAsync(new Endpoint { Id = id, Url = "https://old.example", Secret = "s1" });

        await _store.UpsertAsync(new Endpoint { Id = id, Url = "https://new.example", Secret = "s2" });

        (await _store.GetAsync(id))!.Url.Should().Be("https://new.example");
    }

    [Fact]
    public async Task GetAsync_returns_null_for_a_missing_endpoint()
    {
        (await _store.GetAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task DisableAsync_marks_an_endpoint_disabled()
    {
        var id = Guid.NewGuid();
        await _store.UpsertAsync(new Endpoint { Id = id, Url = "https://a.example", Secret = "s" });

        await _store.DisableAsync(id);

        (await _store.GetAsync(id))!.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task DisableAsync_on_a_non_existent_endpoint_is_a_no_op()
    {
        var act = async () => await _store.DisableAsync(Guid.NewGuid());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisableAsync_on_already_disabled_endpoint_is_a_no_op()
    {
        var id = Guid.NewGuid();
        await _store.UpsertAsync(new Endpoint { Id = id, Url = "https://a.example", Secret = "s" });
        await _store.DisableAsync(id);

        await _store.DisableAsync(id);

        (await _store.GetAsync(id))!.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task ListEnabledAsync_returns_only_enabled_endpoints()
    {
        var enabled = new Endpoint { Id = Guid.NewGuid(), Url = "https://a.example", Secret = "s1" };
        var disabled = new Endpoint { Id = Guid.NewGuid(), Url = "https://b.example", Secret = "s2", Enabled = false };
        await _store.UpsertAsync(enabled);
        await _store.UpsertAsync(disabled);

        var listed = await _store.ListEnabledAsync();

        listed.Should().ContainSingle().Which.Id.Should().Be(enabled.Id);
    }

    [Fact]
    public async Task ListEnabledAsync_returns_empty_when_all_disabled()
    {
        await _store.UpsertAsync(new Endpoint { Id = Guid.NewGuid(), Url = "https://a.example", Secret = "s", Enabled = false });

        (await _store.ListEnabledAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task UpsertAsync_rejects_null_endpoint()
    {
        var act = async () => await _store.UpsertAsync(null!);

        (await act.Should().ThrowAsync<ArgumentNullException>())
            .Which.ParamName.Should().Be("endpoint");
    }

    // Hands the store a fresh context per call; Pooling=False so the file handle is released on
    // dispose and the temp database can be deleted after the test.
    private sealed class FileDbContextFactory(string dbPath) : IDbContextFactory<CaliberWebhooksDbContext>
    {
        private readonly DbContextOptions<CaliberWebhooksDbContext> _options =
            new DbContextOptionsBuilder<CaliberWebhooksDbContext>()
                .UseSqlite($"Data Source={dbPath};Pooling=False")
                .Options;

        public CaliberWebhooksDbContext CreateDbContext() => new(_options);
    }
}
