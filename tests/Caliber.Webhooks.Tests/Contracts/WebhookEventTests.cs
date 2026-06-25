using AwesomeAssertions;

namespace Caliber.Webhooks.Tests;

public sealed class WebhookEventTests
{
    [Fact]
    public void Initializes_all_fields()
    {
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var @event = new WebhookEvent
        {
            Id = id,
            EventType = "order.shipped",
            Payload = """{"orderId":42}""",
            CreatedAt = createdAt,
        };

        @event.Id.Should().Be(id);
        @event.EventType.Should().Be("order.shipped");
        @event.Payload.Should().Be("""{"orderId":42}""");
        @event.CreatedAt.Should().Be(createdAt);
    }
}
