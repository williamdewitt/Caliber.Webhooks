using AwesomeAssertions;

namespace Caliber.Webhooks.Tests;

public sealed class WebhookSecretTests
{
    [Fact]
    public void Generate_produces_a_whsec_prefixed_secret()
    {
        WebhookSecret.Generate().Should().StartWith("whsec_");
    }

    [Fact]
    public void Generate_decodes_to_a_256_bit_key()
    {
        WebhookSecret.DecodeKey(WebhookSecret.Generate()).Should().HaveCount(32);
    }

    [Fact]
    public void Generate_returns_a_distinct_secret_each_call()
    {
        WebhookSecret.Generate().Should().NotBe(WebhookSecret.Generate());
    }

    [Fact]
    public void DecodeKey_accepts_a_bare_base64_secret_without_the_prefix()
    {
        WebhookSecret.DecodeKey("MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw").Should().HaveCount(24);
    }

    [Fact]
    public void DecodeKey_rejects_invalid_base64_without_leaking_the_secret()
    {
        const string badSecret = "whsec_@@@SECRETMARKER@@@";

        var act = () => WebhookSecret.DecodeKey(badSecret);

        act.Should().Throw<ArgumentException>()
            .Which.Message.Should().NotContain("SECRETMARKER");
    }

    [Fact]
    public void DecodeKey_rejects_invalid_base64_with_the_expected_message()
    {
        var act = () => WebhookSecret.DecodeKey("whsec_!!!notbase64!!!");

        var exception = act.Should().Throw<ArgumentException>().Which;
        exception.ParamName.Should().Be("secret");
        exception.Message.Should().StartWith("The endpoint secret is not valid base64.");
    }
}
