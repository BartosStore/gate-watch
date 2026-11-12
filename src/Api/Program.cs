using Api.Blacklist;
using Api.Data;
using Api.Scanning;
using Microsoft.EntityFrameworkCore;

if (args is ["scan", ..])
{
    // Developer command (step 3): docker compose run --rm app scan https://www.example.cz/
    var dataDir = Environment.GetEnvironmentVariable("DataDir") ?? Path.Combine(AppContext.BaseDirectory, "data");
    return await ScanCli.RunAsync(args[1..], dataDir);
}

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Db")
    ?? throw new InvalidOperationException("Missing connection string 'ConnectionStrings:Db'.");
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

builder.Services.AddSingleton<BlacklistStore>();
builder.Services.AddHostedService<BlacklistLoader>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", async (AppDbContext db, BlacklistStore blacklist, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct)
        ? Results.Ok(new { status = "ok", blacklist_ready = blacklist.Ready, blacklist_entries = blacklist.Count })
        : Results.Json(new { error = "database unavailable" }, statusCode: 503));

// Temporary until step 4: only the "blacklist not ready" guard is in place.
app.MapPost("/runs", (BlacklistStore blacklist) =>
    !blacklist.Ready
        ? Results.Json(new { error = "Blacklist se ještě připravuje, zkuste to za chvíli." }, statusCode: 503)
        : Results.Json(new { error = "Spouštění kontrol bude implementováno v kroku 4." }, statusCode: 501));

app.MapFallbackToFile("index.html");

await app.RunAsync();
return 0;

public partial class Program;
