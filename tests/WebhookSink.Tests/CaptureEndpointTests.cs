using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WebhookSink.Tests;

public sealed class CaptureEndpointTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public CaptureEndpointTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseSetting("WebhookSink:StoreCapacity", "3"));
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task POST_in_bucket_captures_hook_and_GET_api_hooks_returns_it()
    {
        var body = """{"event":"order.placed"}""";

        var postResponse = await _client.PostAsync(
            "/in/orders",
            new StringContent(body, Encoding.UTF8, "application/json"));

        postResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var getResponse = await _client.GetAsync("/api/hooks");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var hooks = JsonSerializer.Deserialize<List<HookDto>>(
            await getResponse.Content.ReadAsStringAsync(), JsonOptions)!;

        hooks.Should().ContainSingle(h => h.Bucket == "orders" && h.Body == body && h.Method == "POST");
    }

    [Fact]
    public async Task GET_api_hooks_by_id_returns_hook_and_404_for_missing_id()
    {
        await _client.PostAsync("/in/payments",
            new StringContent("ping", Encoding.UTF8, "text/plain"));

        var hooksResponse = await _client.GetAsync("/api/hooks");
        var hooks = JsonSerializer.Deserialize<List<HookDto>>(
            await hooksResponse.Content.ReadAsStringAsync(), JsonOptions)!;

        var id = hooks[0].Id;

        var found = await _client.GetAsync($"/api/hooks/{id}");
        found.StatusCode.Should().Be(HttpStatusCode.OK);

        var missing = await _client.GetAsync($"/api/hooks/{Guid.NewGuid()}");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Ring_evicts_oldest_when_capacity_is_exceeded()
    {
        for (var i = 1; i <= 4; i++)
        {
            await _client.PostAsync("/in/bucket",
                new StringContent($"hook-{i}", Encoding.UTF8, "text/plain"));
        }

        var response = await _client.GetAsync("/api/hooks");
        var hooks = JsonSerializer.Deserialize<List<HookDto>>(
            await response.Content.ReadAsStringAsync(), JsonOptions)!;

        // capacity=3: hook-1 evicted; newest first → hook-4, hook-3, hook-2
        hooks.Should().HaveCount(3);
        hooks.Select(h => h.Body).Should().Equal("hook-4", "hook-3", "hook-2");
    }

    private sealed record HookDto(Guid Id, string Bucket, string Method, string Body, DateTimeOffset ReceivedAt);
}
