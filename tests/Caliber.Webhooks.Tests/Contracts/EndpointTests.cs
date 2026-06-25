using AwesomeAssertions;

namespace Caliber.Webhooks.Tests;

public sealed class EndpointTests
{
    [Fact]
    public void Enabled_defaults_to_true()
    {
        var endpoint = new Endpoint { Url = "https://acme.example/hooks", Secret = "whsec_x" };

        endpoint.Enabled.Should().BeTrue();
    }

    [Fact]
    public void EventTypes_defaults_to_null_meaning_subscribe_all()
    {
        var endpoint = new Endpoint { Url = "https://acme.example/hooks", Secret = "whsec_x" };

        endpoint.EventTypes.Should().BeNull();
    }

    [Fact]
    public void Initializes_all_fields()
    {
        var id = Guid.NewGuid();

        var endpoint = new Endpoint
        {
            Id = id,
            TenantKey = "tenant-7",
            Url = "https://acme.example/hooks",
            Secret = "whsec_abc",
            EventTypes = ["order.shipped", "order.cancelled"],
            Enabled = false,
            Description = "Acme production",
        };

        endpoint.Id.Should().Be(id);
        endpoint.TenantKey.Should().Be("tenant-7");
        endpoint.Url.Should().Be("https://acme.example/hooks");
        endpoint.Secret.Should().Be("whsec_abc");
        endpoint.EventTypes.Should().Equal("order.shipped", "order.cancelled");
        endpoint.Enabled.Should().BeFalse();
        endpoint.Description.Should().Be("Acme production");
    }
}
