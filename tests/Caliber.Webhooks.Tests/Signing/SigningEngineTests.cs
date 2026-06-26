using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Caliber.Webhooks.Tests;

public sealed class SigningEngineTests
{
    private const string Secret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";
    private const string Payload = """{"test": 2432232314}""";
    private static readonly DateTimeOffset VectorTime = DateTimeOffset.FromUnixTimeSeconds(1614265330);

    private static SigningEngine EngineAt(DateTimeOffset now) => new(new FakeTimeProvider(now));

    [Fact]
    public void Matches_the_published_standard_webhooks_vector()
    {
        var headers = EngineAt(VectorTime).Sign("msg_p5jXN8AQM9LWM0D4loKWxJek", Payload, Secret);

        headers.Id.Should().Be("msg_p5jXN8AQM9LWM0D4loKWxJek");
        headers.Timestamp.Should().Be("1614265330");
        headers.Signature.Should().Be("v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=");
    }

    [Fact]
    public void Signs_the_guid_overload_against_an_independent_oracle()
    {
        var id = new Guid("11111111-1111-1111-1111-111111111111");

        var headers = EngineAt(VectorTime).Sign(id, Payload, Secret);

        headers.Id.Should().Be("11111111-1111-1111-1111-111111111111");
        headers.Signature.Should().Be("v1,pBVuja6rVd7mF3FLj6fmUhnswZKkIfPlduZGNfWHTv0=");
    }

    [Fact]
    public void Timestamp_header_reflects_the_clock_in_unix_seconds()
    {
        var headers = EngineAt(DateTimeOffset.FromUnixTimeSeconds(1700000000)).Sign(Guid.NewGuid(), Payload, Secret);

        headers.Timestamp.Should().Be("1700000000");
    }

    [Fact]
    public void A_generated_secret_round_trips_through_sign_and_constant_time_compare()
    {
        var secret = WebhookSecret.Generate();
        var id = Guid.NewGuid();
        var headers = EngineAt(VectorTime).Sign(id, Payload, secret);

        var recomputed = SigningEngine.ComputeSignature(id.ToString(), headers.Timestamp, Payload, secret);

        SigningEngine.SignaturesMatch(recomputed, headers.Signature).Should().BeTrue();
    }

    [Fact]
    public void SignaturesMatch_rejects_a_different_signature()
    {
        SigningEngine.SignaturesMatch("v1,AAAA", "v1,BBBB").Should().BeFalse();
    }

    [Fact]
    public void SignaturesMatch_rejects_a_length_mismatch()
    {
        SigningEngine.SignaturesMatch("v1,AAAA", "v1,AAAAAAAA").Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Sign_rejects_a_missing_secret(string? secret)
    {
        var act = () => EngineAt(VectorTime).Sign(Guid.NewGuid(), Payload, secret!);

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("secret");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Sign_rejects_a_null_or_empty_webhook_id(string? webhookId)
    {
        var act = () => EngineAt(VectorTime).Sign(webhookId!, Payload, Secret);

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("webhookId");
    }

    [Fact]
    public void Sign_rejects_null_payload()
    {
        var act = () => EngineAt(VectorTime).Sign("any-id", null!, Secret);

        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("payload");
    }
}
