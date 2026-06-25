using AwesomeAssertions;

namespace Caliber.Webhooks.Tests;

public sealed class WebhookMessageTests
{
    private static WebhookMessage NewMessage() => new()
    {
        Id = Guid.NewGuid(),
        EventId = Guid.NewGuid(),
        EndpointId = Guid.NewGuid(),
        EventType = "order.shipped",
        Payload = """{"orderId":42}""",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void New_message_starts_pending_with_no_attempts_and_no_lease()
    {
        var message = NewMessage();

        message.Status.Should().Be(DeliveryStatus.Pending);
        message.AttemptCount.Should().Be(0);
        message.Owner.Should().BeNull();
        message.LeaseUntil.Should().BeNull();
        message.LastError.Should().BeNull();
    }

    [Fact]
    public void Identity_and_payload_fields_round_trip()
    {
        var id = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var message = new WebhookMessage
        {
            Id = id,
            EventId = eventId,
            EndpointId = endpointId,
            EventType = "order.shipped",
            Payload = "body",
            CreatedAt = createdAt,
        };

        message.Id.Should().Be(id);
        message.EventId.Should().Be(eventId);
        message.EndpointId.Should().Be(endpointId);
        message.EventType.Should().Be("order.shipped");
        message.Payload.Should().Be("body");
        message.CreatedAt.Should().Be(createdAt);
    }

    [Fact]
    public void Delivery_state_is_mutable()
    {
        var message = NewMessage();
        var nextAttempt = message.CreatedAt.AddMinutes(5);

        message.Status = DeliveryStatus.DeadLettered;
        message.AttemptCount = 12;
        message.NextAttemptAt = nextAttempt;
        message.Owner = "dispatcher-1";
        message.LeaseUntil = nextAttempt;
        message.LastError = "503 Service Unavailable";

        message.Status.Should().Be(DeliveryStatus.DeadLettered);
        message.AttemptCount.Should().Be(12);
        message.NextAttemptAt.Should().Be(nextAttempt);
        message.Owner.Should().Be("dispatcher-1");
        message.LeaseUntil.Should().Be(nextAttempt);
        message.LastError.Should().Be("503 Service Unavailable");
    }
}
