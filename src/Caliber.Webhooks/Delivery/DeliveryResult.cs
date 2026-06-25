namespace Caliber.Webhooks;

/// <summary>
/// The outcome of a single delivery attempt: whether it succeeded, the HTTP status (when one was
/// received), and an error description for the failure case.
/// </summary>
internal readonly record struct DeliveryResult(bool Succeeded, int? StatusCode, string? Error);
