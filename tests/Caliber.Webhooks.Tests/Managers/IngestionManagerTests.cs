using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Caliber.Webhooks.Tests;

public sealed class IngestionManagerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);

    private static (IngestionManager Ingestion, InMemoryMessageStore Messages, InMemoryEndpointStore Endpoints) Build()
    {
        var clock = new FakeTimeProvider(Now);
        var options = new CaliberWebhooksOptions { TimeProvider = clock };
        var endpoints = new InMemoryEndpointStore();
        var messages = new InMemoryMessageStore(clock);
        var ingestion = new IngestionManager(endpoints, messages, new MatchingEngine(), options);
        return (ingestion, messages, endpoints);
    }

    private static Endpoint Endpoint(IReadOnlyList<string>? eventTypes = null) => new()
    {
        Id = Guid.NewGuid(),
        Url = "https://acme.example/hooks",
        Secret = "whsec_x",
        EventTypes = eventTypes,
    };

    [Fact]
    public async Task Publish_fans_out_one_message_per_matching_endpoint()
    {
        var (ingestion, messages, endpoints) = Build();
        var exact = Endpoint(["order.shipped"]);
        var all = Endpoint();                    // subscribe-all
        var other = Endpoint(["order.cancelled"]);
        await endpoints.UpsertAsync(exact);
        await endpoints.UpsertAsync(all);
        await endpoints.UpsertAsync(other);

        await ingestion.PublishAsync("order.shipped", new { orderId = 7 });

        var claimed = await messages.ClaimDueAsync(10, Lease, "d");
        claimed.Select(m => m.EndpointId).Should().BeEquivalentTo(new[] { exact.Id, all.Id });
    }

    [Fact]
    public async Task Publish_serializes_the_payload_and_stamps_event_metadata()
    {
        var (ingestion, messages, endpoints) = Build();
        await endpoints.UpsertAsync(Endpoint());

        await ingestion.PublishAsync("order.shipped", new { orderId = 7 });

        var message = (await messages.ClaimDueAsync(10, Lease, "d")).Single();
        message.EventType.Should().Be("order.shipped");
        message.Payload.Should().Be("""{"orderId":7}""");
        message.Status.Should().Be(DeliveryStatus.Pending);
        message.NextAttemptAt.Should().Be(Now);
    }

    [Fact]
    public async Task Publish_shares_one_event_id_across_the_fan_out()
    {
        var (ingestion, messages, endpoints) = Build();
        await endpoints.UpsertAsync(Endpoint());
        await endpoints.UpsertAsync(Endpoint());

        await ingestion.PublishAsync("order.shipped", new { });

        var claimed = await messages.ClaimDueAsync(10, Lease, "d");
        claimed.Select(m => m.EventId).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task Publish_with_no_matching_endpoints_enqueues_nothing()
    {
        var (ingestion, messages, endpoints) = Build();
        await endpoints.UpsertAsync(Endpoint(["order.cancelled"]));

        await ingestion.PublishAsync("order.shipped", new { });

        (await messages.ClaimDueAsync(10, Lease, "d")).Should().BeEmpty();
    }
}
