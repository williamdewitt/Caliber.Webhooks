namespace Caliber.Webhooks;

/// <summary>
/// Library-level constants for Caliber.Webhooks.
/// </summary>
/// <remarks>
/// This is the M0 production-ready shell: the multi-target build, packaging, analyzers,
/// and CI are in place ahead of the feature work that begins in M1. See the roadmap in the
/// documentation bundle for what each milestone delivers.
/// </remarks>
public static class CaliberWebhooks
{
    /// <summary>
    /// The name used for the library's <see cref="System.Diagnostics.ActivitySource"/> and
    /// <see cref="System.Diagnostics.Metrics.Meter"/>. Register it in your OpenTelemetry
    /// pipeline with <c>.AddSource(CaliberWebhooks.DiagnosticsName)</c> and
    /// <c>.AddMeter(CaliberWebhooks.DiagnosticsName)</c>.
    /// </summary>
    public const string DiagnosticsName = "Caliber.Webhooks";
}
