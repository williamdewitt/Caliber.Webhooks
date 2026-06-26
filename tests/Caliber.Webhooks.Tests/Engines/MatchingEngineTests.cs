using AwesomeAssertions;

namespace Caliber.Webhooks.Tests;

public sealed class MatchingEngineTests
{
    private static Endpoint MakeEndpoint(IReadOnlyList<string>? eventTypes, bool enabled = true) => new()
    {
        Id = Guid.NewGuid(),
        Url = "https://acme.example/hooks",
        Secret = "whsec_x",
        EventTypes = eventTypes,
        Enabled = enabled,
    };

    [Theory]
    [InlineData(new[] { "order.shipped" }, "order.shipped", true)]      // exact match
    [InlineData(new[] { "order.shipped" }, "order.cancelled", false)]   // no match
    [InlineData(new[] { "order.shipped", "order.paid" }, "order.paid", true)] // exact within set
    [InlineData(null, "anything", true)]                                // subscribe-all (null)
    [InlineData(new string[0], "anything", true)]                       // subscribe-all (empty)
    [InlineData(new[] { "Order.Shipped" }, "order.shipped", false)]     // case-sensitive
    public void Matches_on_exact_type_or_subscribe_all(string[]? subscribed, string eventType, bool expected)
    {
        var endpoint = MakeEndpoint(subscribed);

        var matched = new MatchingEngine().Match(eventType, [endpoint]);

        matched.Should().HaveCount(expected ? 1 : 0);
    }

    [Fact]
    public void Disabled_endpoints_never_match_even_when_subscribed_to_all()
    {
        var endpoint = MakeEndpoint(eventTypes: null, enabled: false);

        new MatchingEngine().Match("order.shipped", [endpoint]).Should().BeEmpty();
    }

    [Fact]
    public void Returns_only_the_matching_subset_in_order()
    {
        var shipped = MakeEndpoint(["order.shipped"]);
        var cancelled = MakeEndpoint(["order.cancelled"]);
        var all = MakeEndpoint(eventTypes: null);

        var matched = new MatchingEngine().Match("order.shipped", [shipped, cancelled, all]);

        matched.Should().Equal(shipped, all);
    }

    [Fact]
    public void No_endpoints_yields_no_matches()
    {
        new MatchingEngine().Match("order.shipped", []).Should().BeEmpty();
    }

    [Fact]
    public void Honours_a_custom_event_type_comparer()
    {
        var endpoint = MakeEndpoint(["Order.Shipped"]);

        var engine = new MatchingEngine(StringComparer.OrdinalIgnoreCase);

        engine.Match("order.shipped", [endpoint]).Should().ContainSingle();
    }

    [Fact]
    public void Match_rejects_null_event_type()
    {
        var act = () => new MatchingEngine().Match(null!, []);

        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("eventType");
    }

    [Fact]
    public void Match_rejects_null_endpoints()
    {
        var act = () => new MatchingEngine().Match("order.shipped", null!);

        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("endpoints");
    }
}
