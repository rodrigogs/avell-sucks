using AvellSucks.Core;
using AvellSucks.Core.Hardware;
using AvellSucks.Core.Platforms;
using AvellSucks.Server;

var port = ResolvePort(args);
var requireHttps = RequireHttps();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogging(cfg => cfg.AddConsole());
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// --- Telemetry ---
builder.Services.AddSingleton<IEventPublisher, ConsoleEventPublisher>();

// --- EC hardware backend (Windows WMI) ---
// Single instance serves both read and write paths.
builder.Services.AddSingleton<WmiEcBackend>();
builder.Services.AddSingleton<IEcBackend>(sp => sp.GetRequiredService<WmiEcBackend>());
builder.Services.AddSingleton<IEcWriter>(sp => sp.GetRequiredService<WmiEcBackend>());

// --- EC write safety pipeline ---
builder.Services.AddSingleton<WriteGate>(WriteGate.FromEnvironment());
builder.Services.AddSingleton<EcWriteAllowlist>();
builder.Services.AddSingleton<IWriteAuditLog>(_ =>
{
    var dir = Environment.GetEnvironmentVariable("GAMINGCENTER_AUDIT_DIR")
              ?? Path.Combine(
                  Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                  "AvellSucks");
    return new JsonlWriteAuditLog(Path.Combine(dir, "ec-write-audit.jsonl"));
});
builder.Services.AddSingleton<SafeEcWriter>();

var app = builder.Build();

app.Use((context, next) =>
{
    context.Response.Headers["XLocalApi"] = bool.TrueString;
    return next(context);
});
app.UseLoopbackOnly(requireHttps);
app.MapOpenApi();

app.MapControllers();

var prefix = requireHttps ? "https" : "http";
await app.RunAsync($"{prefix}://127.0.0.1:{port}/");
return;

// --- Helpers ---

static bool RequireHttps()
{
    var flag = Environment.GetEnvironmentVariable("GAMING_CENTER_REQUIRE_HTTPS");
    return string.Equals(flag, "1", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
}

static int ResolvePort(string[] args)
{
    if (args.Length > 0 && int.TryParse(args[0], out var p) && p is > 0 and < 65536) return p;
    return 5055;
}
