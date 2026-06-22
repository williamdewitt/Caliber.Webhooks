using AwesomeAssertions;

namespace Caliber.Webhooks.Tests;

/// <summary>
/// M0 smoke tests — prove the build, multi-target, test runner, and assertion stack are wired
/// end to end. Feature tests arrive with the components they cover from M1 onward.
/// </summary>
public sealed class SmokeTests
{
    [Fact]
    public void DiagnosticsName_is_the_library_source_name()
    {
        CaliberWebhooks.DiagnosticsName.Should().Be("Caliber.Webhooks");
    }
}
