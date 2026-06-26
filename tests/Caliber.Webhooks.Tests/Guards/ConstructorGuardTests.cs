using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Caliber.Webhooks.Tests;

/// <summary>
/// Every internal component fails fast on a null collaborator. These tests pin the constructor guards
/// so the <c>ArgumentNullException.ThrowIfNull</c> statements can't be silently removed. Each case
/// nulls one argument (by position) and asserts the matching parameter name is reported.
/// </summary>
public sealed class ConstructorGuardTests
{
    private sealed class StubChannel : IDeliveryChannel
    {
        public Task<DeliveryResult> SendAsync(
            Endpoint endpoint, WebhookMessage message, WebhookSignatureHeaders headers, CancellationToken ct = default)
            => Task.FromResult(new DeliveryResult(true, 200, null));
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static IMessageStore Messages => new InMemoryMessageStore(TimeProvider.System);
    private static IEndpointStore Endpoints => new InMemoryEndpointStore();
    private static CaliberWebhooksOptions Options => new();
    private static SigningEngine Signing => new();
    private static RetryEngine Retry => new(new CaliberWebhooksOptions());
    private static MatchingEngine Matching => new();
    private static IDeliveryChannel Channel => new StubChannel();

    private static DeliveryManager Delivery => new(
        Messages, Endpoints, Signing, Retry, Channel, Options, owner: "guard");

    private static void AssertRejectsNullAt(Func<object> construct, string expectedParam)
    {
        var act = construct;
        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be(expectedParam);
    }

    [Fact]
    public void InMemoryMessageStore_rejects_a_null_clock()
        => AssertRejectsNullAt(() => new InMemoryMessageStore(null!), "timeProvider");

    [Fact]
    public void SubscriptionManager_rejects_a_null_store()
        => AssertRejectsNullAt(() => new SubscriptionManager(null!), "endpoints");

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void HttpDeliveryChannel_rejects_null_collaborators(int nullIndex)
    {
        string[] names = ["httpClientFactory", "options"];
        AssertRejectsNullAt(
            () => new HttpDeliveryChannel(
                nullIndex == 0 ? null! : new StubHttpClientFactory(),
                nullIndex == 1 ? null! : Options),
            names[nullIndex]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DispatcherHost_rejects_null_collaborators(int nullIndex)
    {
        string[] names = ["delivery", "options", "logger"];
        AssertRejectsNullAt(
            () => new DispatcherHost(
                nullIndex == 0 ? null! : Delivery,
                nullIndex == 1 ? null! : Options,
                nullIndex == 2 ? null! : NullLogger<DispatcherHost>.Instance),
            names[nullIndex]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void IngestionManager_rejects_null_collaborators(int nullIndex)
    {
        string[] names = ["endpoints", "messages", "matching", "options"];
        AssertRejectsNullAt(
            () => new IngestionManager(
                nullIndex == 0 ? null! : Endpoints,
                nullIndex == 1 ? null! : Messages,
                nullIndex == 2 ? null! : Matching,
                nullIndex == 3 ? null! : Options),
            names[nullIndex]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void DeliveryManager_rejects_null_collaborators(int nullIndex)
    {
        string[] names = ["messages", "endpoints", "signing", "retry", "channel", "options"];
        AssertRejectsNullAt(
            () => new DeliveryManager(
                nullIndex == 0 ? null! : Messages,
                nullIndex == 1 ? null! : Endpoints,
                nullIndex == 2 ? null! : Signing,
                nullIndex == 3 ? null! : Retry,
                nullIndex == 4 ? null! : Channel,
                nullIndex == 5 ? null! : Options),
            names[nullIndex]);
    }
}
