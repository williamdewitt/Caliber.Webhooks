using WebhookSink;

var builder = WebApplication.CreateBuilder(args);

var capacity = builder.Configuration.GetValue("WebhookSink:StoreCapacity", 200);
builder.Services.AddSingleton<IHookStore>(_ => new HookStore(capacity));

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok());

app.MapGet("/", () => Results.Content(
    """
    <!DOCTYPE html>
    <html lang="en">
    <head><meta charset="utf-8" /><title>WebhookSink</title></head>
    <body>
    <h1>WebhookSink</h1>
    <p>Caliber.Webhooks sample — receive and inspect webhook deliveries.</p>
    </body>
    </html>
    """,
    "text/html"));

string[] allVerbs = ["DELETE", "GET", "HEAD", "OPTIONS", "PATCH", "POST", "PUT", "TRACE"];

app.MapMethods("/in/{bucket}", allVerbs, async (
    string bucket,
    HttpRequest request,
    IHookStore store,
    CancellationToken ct) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

    var headers = request.Headers.ToDictionary(
        h => h.Key,
        h => h.Value.ToString() ?? string.Empty);

    var capture = new HookCapture(
        Id: Guid.NewGuid(),
        Bucket: bucket,
        Method: request.Method,
        Headers: headers,
        Body: body,
        ReceivedAt: DateTimeOffset.UtcNow);

    store.Add(capture);
    return Results.Accepted();
});

app.MapGet("/api/hooks", (IHookStore store) => Results.Ok(store.GetAll()));

app.MapGet("/api/hooks/{id:guid}", (Guid id, IHookStore store) =>
{
    var hook = store.GetById(id);
    return hook is not null ? Results.Ok(hook) : Results.NotFound();
});

app.Run();

public partial class Program { }
