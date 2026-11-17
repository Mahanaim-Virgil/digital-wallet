var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry, health check, service discovery (API ile aynı ortak ayarlar).
builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();

app.Run();
