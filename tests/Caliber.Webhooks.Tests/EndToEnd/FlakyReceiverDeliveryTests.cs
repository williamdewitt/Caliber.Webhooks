using System.Collections.Concurrent;
using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Caliber.Webhooks.Tests;

/// <summary>
/// End-to-end: publish through the real registration, dispatcher, signing, and HTTP pipeline into an
/// in-process flaky receiver that fails a few times then succeeds — proving retry, eventual success,
/// exactly-one successful delivery per webhook-id, and a correctly signed wire request.
/// </summary>
public sealed class FlakyReceiverDeliveryTests
{
    private sealed class FlakyReceiver(int failuresBeforeSuccess, string secret) : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.Ordinal);
        private int _signatureFailures;

        public ConcurrentBag<string> Successes { get; } = [];

        public int SignatureFailures => _signatureFailures;

        public int AttemptsFor(string id) => _attempts.TryGetValue(id, out var n) ? n : 0;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var id = request.Headers.GetValues(SigningEngine.IdHeader).Single();
            var timestamp = request.Headers.GetValues(SigningEngine.TimestampHeader).Single();
            var signature = request.Headers.GetValues(SigningEngine.SignatureHeader).Single();
            var body = await request.Content!.ReadAsStringAsync(ct).ConfigureAwait(false);

            var expected = SigningEngine.ComputeSignature(id, timestamp, body, secret);
            if (!SigningEngine.SignaturesMatch(expected, signature))
            {
                Interlocked.Increment(ref _signatureFailures);
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            var attempt = _attempts.AddOrUpdate(id, 1, (_, n) => n + 1);
            if (attempt <= failuresBeforeSuccess)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            Successes.Add(id);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(20).ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task A_flaky_endpoint_is_retried_then_delivered_exactly_once()
    {
        var secret = WebhookSecret.Generate();
        var receiver = new FlakyReceiver(failuresBeforeSuccess: 2, secret);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCaliberWebhooks(options =>
        {
            options.PollInterval = TimeSpan.FromMilliseconds(25);
            options.HttpTimeout = TimeSpan.FromSeconds(2);
            options.LeaseDuration = TimeSpan.FromSeconds(5);
            options.RetrySchedule = RetrySchedule.FromDelays(
                [.. Enumerable.Repeat(TimeSpan.FromMilliseconds(10), 11)]);
        });
        services.AddHttpClient(HttpDeliveryChannel.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => receiver);

        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IWebhookEndpoints>()
            .CreateAsync(new Endpoint { Url = "https://receiver.test/hook", Secret = secret });

        var hosted = provider.GetServices<IHostedService>().ToList();
        foreach (var service in hosted)
        {
            await service.StartAsync(CancellationToken.None);
        }

        try
        {
            await provider.GetRequiredService<IWebhookPublisher>().PublishAsync("order.shipped", new { orderId = 1 });

            await WaitUntilAsync(() => !receiver.Successes.IsEmpty, TimeSpan.FromSeconds(10));
            // Give the dispatcher extra ticks to (not) re-deliver an already-delivered message.
            await Task.Delay(150);

            var deliveredId = receiver.Successes.Should().ContainSingle().Subject;
            receiver.AttemptsFor(deliveredId).Should().Be(3); // two failures, then success
            receiver.SignatureFailures.Should().Be(0);
        }
        finally
        {
            foreach (var service in hosted)
            {
                await service.StopAsync(CancellationToken.None);
            }
        }
    }
}
