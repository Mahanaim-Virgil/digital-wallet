var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry, health check, service discovery ve HttpClient dayanıklılık ayarları (ServiceDefaults).
builder.AddServiceDefaults();

var app = builder.Build();

// /health ve /alive (yalnızca Development ortamında açılır).
app.MapDefaultEndpoints();

app.Run();
