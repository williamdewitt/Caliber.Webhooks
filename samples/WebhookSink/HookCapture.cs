namespace WebhookSink;

internal sealed record HookCapture(
    Guid Id,
    string Bucket,
    string Method,
    IReadOnlyDictionary<string, string> Headers,
    string Body,
    DateTimeOffset ReceivedAt);
