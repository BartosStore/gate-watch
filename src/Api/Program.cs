using Api;
using Api.Blacklist;
using Api.Data;
using Api.Runs;
using Api.Scanning;
using Microsoft.EntityFrameworkCore;

if (args is ["scan", ..])
{
    // Developer command: docker compose run --rm --no-deps app scan https://www.example.cz/
    var dataDir = Environment.GetEnvironmentVariable("DataDir") ?? Path.Combine(AppContext.BaseDirectory, "data");
    return await ScanCli.RunAsync(args[1..], dataDir);
}

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Db")
    ?? throw new InvalidOperationException("Missing connection string 'ConnectionStrings:Db'.");
var concurrency = ServiceSetup.ScanConcurrency(builder.Configuration);
builder.Services.AddGateWatchCore(connectionString, concurrency);
builder.Services.AddHostedService<BlacklistLoader>();
builder.Services.AddHostedService<RunWorker>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = ServiceSetup.JsonNaming);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    // Before any new run can be created: close runs left "running" by the previous process.
    var interrupted = await scope.ServiceProvider.GetRequiredService<RunService>().FailInterruptedRunsAsync(CancellationToken.None);
    if (interrupted > 0) app.Logger.LogWarning("Marked {Count} interrupted run(s) as failed", interrupted);
    app.Logger.LogInformation("Scan concurrency: {Concurrency}", concurrency);
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", async (AppDbContext db, BlacklistStore blacklist, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct)
        ? Results.Ok(new { status = "ok", blacklist_ready = blacklist.Ready, blacklist_entries = blacklist.Count })
        : Results.Json(new { error = "database unavailable" }, statusCode: 503));

app.MapApi();

app.MapFallbackToFile("index.html");

await app.RunAsync();
return 0;

public partial class Program;
