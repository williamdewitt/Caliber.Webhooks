using AwesomeAssertions;

namespace Caliber.Webhooks.Tests;

public sealed class SubscriptionManagerTests
{
    private static Endpoint Endpoint(Guid id = default) => new()
    {
        Id = id,
        Url = "https://acme.example/hooks",
        Secret = "whsec_x",
    };

    [Fact]
    public async Task CreateAsync_assigns_an_id_when_none_is_supplied()
    {
        var store = new InMemoryEndpointStore();
        var manager = new SubscriptionManager(store);

        var created = await manager.CreateAsync(Endpoint());

        created.Id.Should().NotBe(Guid.Empty);
        (await store.GetAsync(created.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAsync_keeps_a_supplied_id()
    {
        var id = Guid.NewGuid();

        var created = await new SubscriptionManager(new InMemoryEndpointStore()).CreateAsync(Endpoint(id));

        created.Id.Should().Be(id);
    }

    [Fact]
    public async Task UpdateAsync_replaces_the_endpoint()
    {
        var store = new InMemoryEndpointStore();
        var manager = new SubscriptionManager(store);
        var id = Guid.NewGuid();
        await manager.CreateAsync(Endpoint(id));

        await manager.UpdateAsync(new Endpoint { Id = id, Url = "https://new.example/hooks", Secret = "whsec_y" });

        (await store.GetAsync(id))!.Url.Should().Be("https://new.example/hooks");
    }

    [Fact]
    public async Task DisableAsync_disables_the_endpoint()
    {
        var store = new InMemoryEndpointStore();
        var manager = new SubscriptionManager(store);
        var created = await manager.CreateAsync(Endpoint());

        await manager.DisableAsync(created.Id);

        (await store.GetAsync(created.Id))!.Enabled.Should().BeFalse();
    }
}
