var builder = WebApplication.CreateBuilder(args);
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

app.Run();

public partial class Program { }
