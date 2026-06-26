using AwesomeAssertions;

namespace Caliber.Webhooks.Tests;

public sealed class InMemoryEndpointStoreTests
{
    private static Endpoint MakeEndpoint(Guid id, bool enabled = true) => new()
    {
        Id = id,
        Url = "https://acme.example/hooks",
        Secret = "whsec_x",
        Enabled = enabled,
    };

    [Fact]
    public async Task UpsertAsync_then_GetAsync_round_trips()
    {
        var store = new InMemoryEndpointStore();
        var id = Guid.NewGuid();
        var endpoint = MakeEndpoint(id);

        await store.UpsertAsync(endpoint);

        (await store.GetAsync(id)).Should().BeSameAs(endpoint);
    }

    [Fact]
    public async Task UpsertAsync_replaces_an_existing_endpoint()
    {
        var store = new InMemoryEndpointStore();
        var id = Guid.NewGuid();
        await store.UpsertAsync(MakeEndpoint(id));
        var replacement = new Endpoint { Id = id, Url = "https://new.example/hooks", Secret = "whsec_y" };

        await store.UpsertAsync(replacement);

        (await store.GetAsync(id))!.Url.Should().Be("https://new.example/hooks");
    }

    [Fact]
    public async Task GetAsync_returns_null_for_a_missing_endpoint()
    {
        (await new InMemoryEndpointStore().GetAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task DisableAsync_marks_an_endpoint_disabled()
    {
        var store = new InMemoryEndpointStore();
        var id = Guid.NewGuid();
        await store.UpsertAsync(MakeEndpoint(id));

        await store.DisableAsync(id);

        (await store.GetAsync(id))!.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task ListEnabledAsync_returns_only_enabled_endpoints()
    {
        var store = new InMemoryEndpointStore();
        var enabled = MakeEndpoint(Guid.NewGuid());
        var disabled = MakeEndpoint(Guid.NewGuid(), enabled: false);
        await store.UpsertAsync(enabled);
        await store.UpsertAsync(disabled);

        var listed = await store.ListEnabledAsync();

        listed.Should().ContainSingle().Which.Id.Should().Be(enabled.Id);
    }

    [Fact]
    public async Task UpsertAsync_rejects_null_endpoint()
    {
        var store = new InMemoryEndpointStore();

        Func<Task> act = async () => await store.UpsertAsync(null!);

        (await act.Should().ThrowAsync<ArgumentNullException>())
            .Which.ParamName.Should().Be("endpoint");
    }

    [Fact]
    public async Task DisableAsync_on_already_disabled_endpoint_is_a_no_op()
    {
        var store = new InMemoryEndpointStore();
        var id = Guid.NewGuid();
        await store.UpsertAsync(MakeEndpoint(id));
        await store.DisableAsync(id);
        var afterFirstDisable = await store.GetAsync(id);

        await store.DisableAsync(id);

        (await store.GetAsync(id)).Should().BeSameAs(afterFirstDisable);
    }
}
