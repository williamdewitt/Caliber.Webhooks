using AwesomeAssertions;

namespace Caliber.Webhooks.Tests;

public sealed class RetryScheduleTests
{
    [Fact]
    public void Default_has_the_eleven_locked_delays()
    {
        RetrySchedule.Default.Delays.Should().Equal(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(2),
            TimeSpan.FromHours(4),
            TimeSpan.FromHours(8),
            TimeSpan.FromHours(12));
    }

    [Fact]
    public void Default_gives_twelve_attempts()
    {
        // Eleven inter-attempt delays means twelve attempts before dead-lettering.
        (RetrySchedule.Default.Delays.Count + 1).Should().Be(12);
    }

    [Fact]
    public void Default_is_a_cached_singleton()
    {
        RetrySchedule.Default.Should().BeSameAs(RetrySchedule.Default);
    }

    [Fact]
    public void FromDelays_round_trips_the_delays()
    {
        var schedule = RetrySchedule.FromDelays(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        schedule.Delays.Should().Equal(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void FromDelays_rejects_null()
    {
        var act = () => RetrySchedule.FromDelays(null!);

        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("delays");
    }

    [Fact]
    public void FromDelays_rejects_an_empty_schedule()
    {
        var act = () => RetrySchedule.FromDelays();

        var exception = act.Should().Throw<ArgumentException>().Which;
        exception.ParamName.Should().Be("delays");
        exception.Message.Should().StartWith("A retry schedule needs at least one delay.");
    }

    [Fact]
    public void FromDelays_accepts_zero_delay()
    {
        var schedule = RetrySchedule.FromDelays(TimeSpan.Zero);

        schedule.Delays.Should().Equal(TimeSpan.Zero);
    }

    [Fact]
    public void FromDelays_rejects_a_negative_delay()
    {
        var act = () => RetrySchedule.FromDelays(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(-1));

        var exception = act.Should().Throw<ArgumentException>().Which;
        exception.ParamName.Should().Be("delays");
        exception.Message.Should().StartWith("Retry delays must be non-negative.");
    }
}
