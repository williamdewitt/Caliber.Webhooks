using AwesomeAssertions;

namespace Caliber.Webhooks.Tests;

public sealed class DeliveryStatusTests
{
    [Fact]
    public void Pending_is_the_default_value()
    {
        default(DeliveryStatus).Should().Be(DeliveryStatus.Pending);
    }

    [Theory]
    [InlineData(DeliveryStatus.Pending, 0)]
    [InlineData(DeliveryStatus.Delivered, 1)]
    [InlineData(DeliveryStatus.DeadLettered, 2)]
    public void Numeric_values_are_stable_for_persistence(DeliveryStatus status, int expected)
    {
        // The status is persisted to a column; its numeric mapping must not drift.
        ((int)status).Should().Be(expected);
    }

    [Fact]
    public void Defines_exactly_pending_delivered_and_dead_lettered()
    {
        Enum.GetValues<DeliveryStatus>().Should().BeEquivalentTo(
        [
            DeliveryStatus.Pending,
            DeliveryStatus.Delivered,
            DeliveryStatus.DeadLettered,
        ]);
    }
}
