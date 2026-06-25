namespace Caliber.Webhooks;

/// <summary>
/// The Standard Webhooks headers for a single signed delivery: the message id, the unix-seconds
/// timestamp the signature was computed at, and the <c>v1,&lt;base64&gt;</c> signature value.
/// </summary>
internal readonly record struct WebhookSignatureHeaders(string Id, string Timestamp, string Signature);
