using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Caliber.Webhooks.Tests;

public sealed class RetryEngineTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static CaliberWebhooksOptions Options(
        RetrySchedule? schedule = null,
        int maxAttempts = 12,
        TimeProvider? clock = null) => new()
        {
            RetrySchedule = schedule ?? RetrySchedule.Default,
            MaxAttempts = maxAttempts,
            TimeProvider = clock ?? new FakeTimeProvider(Start),
        };

    [Fact]
    public void Next_schedules_each_attempt_from_the_table_without_jitter_at_the_midpoint()
    {
        var engine = new RetryEngine(Options(), sampleUnitInterval: () => 0.5);
        var delays = RetrySchedule.Default.Delays;

        for (var attemptsMade = 1; attemptsMade < 12; attemptsMade++)
        {
            engine.Next(attemptsMade).Should().Be(Start + delays[attemptsMade - 1]);
        }
    }

    [Theory]
    [InlineData(0.0, 0.8)]  // lower jitter bound
    [InlineData(0.5, 1.0)]  // no jitter
    [InlineData(1.0, 1.2)]  // upper jitter bound
    public void Jitter_scales_the_base_delay_within_twenty_percent(double sample, double factor)
    {
        var engine = new RetryEngine(Options(), sampleUnitInterval: () => sample);
        var firstDelay = RetrySchedule.Default.Delays[0];

        engine.Next(1).Should().Be(Start + (firstDelay * factor));
    }

    [Theory]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(100)]
    public void Next_dead_letters_once_the_attempt_budget_is_spent(int attemptsMade)
    {
        var engine = new RetryEngine(Options(), () => 0.5);

        engine.Next(attemptsMade).Should().BeNull();
    }

    [Fact]
    public void Next_dead_letters_when_the_schedule_is_exhausted_before_max_attempts()
    {
        var options = Options(schedule: RetrySchedule.FromDelays(TimeSpan.FromSeconds(1)), maxAttempts: 12);
        var engine = new RetryEngine(options, () => 0.5);

        engine.Next(1).Should().Be(Start + TimeSpan.FromSeconds(1));
        engine.Next(2).Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Next_rejects_non_positive_attempt_counts(int attemptsMade)
    {
        var engine = new RetryEngine(Options(), () => 0.5);

        var act = () => engine.Next(attemptsMade);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("attemptsMade");
    }

    [Fact]
    public void Constructor_rejects_null_options()
    {
        var act = () => new RetryEngine(null!);

        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("options");
    }
}
