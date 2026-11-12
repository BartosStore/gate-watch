using System.Text.Json;
using Api;
using Api.Blacklist;
using Api.Data;
using Api.Findings;
using Api.Runs;
using Api.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests;

/// <summary>Runs only when TEST_DB points to a Postgres (docker compose run --rm tests).</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TEST_DB")))
            Skip = "TEST_DB is not set (run: docker compose run --rm --build tests).";
    }
}

/// <summary>All DB test classes share one fixture (and run sequentially), so the database is created once.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

/// <summary>Fresh, migrated test database shared by the tests; each test uses its own merchant ids.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public string ConnectionString { get; } = Environment.GetEnvironmentVariable("TEST_DB") ?? "";

    public async Task InitializeAsync()
    {
        if (ConnectionString == "") return;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>Scanner returning prepared results per URL (no browser).</summary>
public sealed class FakeScanner : IPageScanner
{
    public Func<string, ScanResult> Handler { get; set; } = _ => Ok();

    public Task<ScanResult> ScanAsync(string url, CancellationToken ct) => Task.FromResult(Handler(url));

    public static ScanResult Ok(params string[] scriptUrls) =>
        new(ScanStatus.Completed, null, scriptUrls.Select(u => new ScannedUrl(u, FindingKind.Script, true)).ToList(), 100, null);
}

[Collection(PostgresCollection.Name)]
public sealed class RunFlowTests : IAsyncLifetime
{
    private const string Evil1 = "https://evil.example/one.js";
    private const string Evil2 = "https://evil.example/two.js";
    private const string Clean = "https://cdn.example/clean.js";

    private readonly ServiceProvider _sp;
    private readonly FakeScanner _scanner = new();
    private readonly string _p = Guid.NewGuid().ToString("N")[..8] + "-"; // per-test merchant id prefix

    public RunFlowTests(PostgresFixture db)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddGateWatchCore(db.ConnectionString, 4);
        services.AddSingleton<IPageScanner>(_scanner);
        _sp = services.BuildServiceProvider();
        _sp.GetRequiredService<BlacklistStore>().Load([new(Evil1, "custom"), new(Evil2, "urlhaus-snapshot")]);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _sp.DisposeAsync();

    private async Task<RunDto> RunAsync(params MerchantInput[] merchants)
    {
        using var scope = _sp.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<RunService>();
        var id = await runs.CreateRunAsync(merchants, CancellationToken.None);
        await runs.ProcessRunAsync(id, CancellationToken.None);
        return (await runs.GetRunAsync(id, CancellationToken.None))!;
    }

    private async Task<bool> ResolveAsync(string id, string note)
    {
        using var scope = _sp.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FindingService>().ResolveAsync(id, note, CancellationToken.None);
    }

    private async Task<List<Finding>> FindingsAsync(string merchantId)
    {
        using var scope = _sp.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Findings.AsNoTracking()
            .Where(f => f.MerchantId == merchantId).ToListAsync();
    }

    private MerchantInput Shop(string name) => new(_p + name, $"https://{name}.example/");

    [PostgresFact]
    public async Task Run_returns_one_result_per_merchant_with_matches_and_alerts()
    {
        _scanner.Handler = url => url.Contains("bad") ? FakeScanner.Ok(Evil1, Clean) : FakeScanner.Ok(Clean);

        var run = await RunAsync(Shop("bad"), Shop("good"));

        Assert.Equal("done", run.Status);
        Assert.Null(run.Error);
        Assert.Equal(2, run.Results.Count);
        var bad = run.Results.Single(r => r.MerchantId == _p + "bad");
        Assert.Equal("ok", bad.Status);
        Assert.Equal([Evil1], bad.Matches);
        var alert = Assert.Single(bad.Alerts);
        Assert.Equal(Evil1, alert.Url);
        Assert.False(alert.Resolved);
        Assert.Equal("", alert.Note);
        Assert.Equal(Domain.FindingId.Compute(_p + "bad", Evil1), alert.Id);

        var good = run.Results.Single(r => r.MerchantId == _p + "good");
        Assert.Equal("ok", good.Status);
        Assert.Empty(good.Matches);
        Assert.Empty(good.Alerts);
        Assert.Null(good.Error);
    }

    [PostgresFact]
    public async Task Same_finding_on_next_check_is_not_duplicated_and_stays_resolved()
    {
        _scanner.Handler = _ => FakeScanner.Ok(Evil1);
        var first = await RunAsync(Shop("a"));
        var id = first.Results[0].Alerts[0].Id;
        Assert.True(await ResolveAsync(id, "vyřešeno s obchodem"));
        var before = Assert.Single(await FindingsAsync(_p + "a"));

        var second = await RunAsync(Shop("a"));

        var alert = Assert.Single(second.Results[0].Alerts);
        Assert.Equal(id, alert.Id);
        Assert.True(alert.Resolved);
        Assert.Equal("vyřešeno s obchodem", alert.Note);
        Assert.Equal([Evil1], second.Results[0].Matches); // matches stay even when resolved

        var after = Assert.Single(await FindingsAsync(_p + "a"));
        Assert.True(after.LastSeenAt > before.LastSeenAt);
        Assert.NotEqual(before.LastCheckId, after.LastCheckId);
        Assert.Equal(before.FirstSeenAt, after.FirstSeenAt);
        Assert.Equal(before.ResolvedAt, after.ResolvedAt);
    }

    [PostgresFact]
    public async Task New_url_creates_new_unresolved_finding_even_if_older_is_resolved()
    {
        _scanner.Handler = _ => FakeScanner.Ok(Evil1);
        var first = await RunAsync(Shop("a"));
        await ResolveAsync(first.Results[0].Alerts[0].Id, "ok");

        _scanner.Handler = _ => FakeScanner.Ok(Evil1, Evil2);
        var second = await RunAsync(Shop("a"));

        Assert.Equal(2, second.Results[0].Alerts.Count);
        Assert.True(second.Results[0].Alerts.Single(a => a.Url == Evil1).Resolved);
        Assert.False(second.Results[0].Alerts.Single(a => a.Url == Evil2).Resolved);
        Assert.Equal(2, (await FindingsAsync(_p + "a")).Count);
    }

    [PostgresFact]
    public async Task Same_url_at_another_merchant_is_separate_finding()
    {
        _scanner.Handler = _ => FakeScanner.Ok(Evil1);
        var first = await RunAsync(Shop("a"));
        await ResolveAsync(first.Results[0].Alerts[0].Id, "ok");

        var run = await RunAsync(Shop("a"), Shop("b"));

        var a = run.Results.Single(r => r.MerchantId == _p + "a").Alerts.Single();
        var b = run.Results.Single(r => r.MerchantId == _p + "b").Alerts.Single();
        Assert.NotEqual(a.Id, b.Id);
        Assert.True(a.Resolved);
        Assert.False(b.Resolved);
    }

    [PostgresFact]
    public async Task Timeout_is_never_clean_ok_but_keeps_found_matches()
    {
        _scanner.Handler = url => url.Contains("slow")
            ? new ScanResult(ScanStatus.Timeout, "Kontrola nedokončena do 10 s", [new(Evil1, FindingKind.Script, true)], 10000, null)
            : new ScanResult(ScanStatus.Unreachable, "net::ERR_NAME_NOT_RESOLVED", [], 50, null);

        var run = await RunAsync(Shop("slow"), Shop("down"));

        var slow = run.Results.Single(r => r.MerchantId == _p + "slow");
        Assert.Equal("error", slow.Status);
        Assert.Equal("Kontrola nedokončena do 10 s", slow.Error);
        Assert.Equal([Evil1], slow.Matches);
        Assert.Single(slow.Alerts);

        var down = run.Results.Single(r => r.MerchantId == _p + "down");
        Assert.Equal("error", down.Status);
        Assert.Contains("ERR_NAME_NOT_RESOLVED", down.Error);

        using var scope = _sp.CreateScope();
        var outcomes = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Checks
            .Where(c => c.MerchantId.StartsWith(_p)).Select(c => c.Outcome).ToListAsync();
        Assert.Equal(["timeout", "unreachable"], outcomes.Order());
    }

    [PostgresFact]
    public async Task Failed_check_does_not_remove_or_change_findings()
    {
        _scanner.Handler = _ => FakeScanner.Ok(Evil1);
        var first = await RunAsync(Shop("a"));
        await ResolveAsync(first.Results[0].Alerts[0].Id, "poznámka");

        _scanner.Handler = _ => new ScanResult(ScanStatus.Unreachable, "HTTP 503", [], 10, null);
        var second = await RunAsync(Shop("a"));

        Assert.Equal("error", second.Results[0].Status);
        Assert.Empty(second.Results[0].Matches);
        var f = Assert.Single(await FindingsAsync(_p + "a"));
        Assert.True(f.Resolved);
        Assert.Equal("poznámka", f.Note);
    }

    [PostgresFact]
    public async Task Resolve_is_idempotent_and_only_overwrites_note()
    {
        _scanner.Handler = _ => FakeScanner.Ok(Evil1);
        var run = await RunAsync(Shop("a"));
        var id = run.Results[0].Alerts[0].Id;

        Assert.True(await ResolveAsync(id, "první"));
        var once = Assert.Single(await FindingsAsync(_p + "a"));
        Assert.True(await ResolveAsync(id, "druhá"));
        var twice = Assert.Single(await FindingsAsync(_p + "a"));

        Assert.True(twice.Resolved);
        Assert.Equal("druhá", twice.Note);
        Assert.Equal(once.ResolvedAt, twice.ResolvedAt);
        Assert.False(await ResolveAsync("0123456789abcdef0123456789abcdef", "x"));
    }

    [PostgresFact]
    public async Task Exception_in_one_merchant_is_error_of_that_merchant_only()
    {
        _scanner.Handler = url => url.Contains("crash") ? throw new InvalidOperationException("boom") : FakeScanner.Ok();

        var run = await RunAsync(Shop("crash"), Shop("fine"));

        Assert.Equal("done", run.Status);
        var crash = run.Results.Single(r => r.MerchantId == _p + "crash");
        Assert.Equal("error", crash.Status);
        Assert.Contains("boom", crash.Error);
        Assert.Equal("ok", run.Results.Single(r => r.MerchantId == _p + "fine").Status);
    }

    [PostgresFact]
    public async Task Running_runs_from_previous_process_are_failed_on_start()
    {
        Guid id;
        using (var scope = _sp.CreateScope())
            id = await scope.ServiceProvider.GetRequiredService<RunService>().CreateRunAsync([Shop("x")], CancellationToken.None);

        using (var scope = _sp.CreateScope())
        {
            var runs = scope.ServiceProvider.GetRequiredService<RunService>();
            Assert.True(await runs.FailInterruptedRunsAsync(CancellationToken.None) >= 1);
            var run = (await runs.GetRunAsync(id, CancellationToken.None))!;
            Assert.Equal("failed", run.Status);
            Assert.Equal(RunService.InterruptedError, run.Error);
            Assert.Empty(run.Results);
        }
    }

    [PostgresFact]
    public async Task Unknown_run_returns_null()
    {
        using var scope = _sp.CreateScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<RunService>().GetRunAsync(Guid.NewGuid(), CancellationToken.None));
    }
}

public class ParseMerchantsTests
{
    private static string? Error(string json) => Endpoints.ParseMerchants(JsonSerializer.Deserialize<JsonElement>(json)).Error;

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"merchants\": []}")]
    [InlineData("{\"merchants\": [1]}")]
    [InlineData("{\"merchants\": [{\"id\": \"\", \"url\": \"https://a.cz/\"}]}")]
    [InlineData("{\"merchants\": [{\"id\": \"a\"}]}")]
    [InlineData("{\"merchants\": [{\"id\": \"a\", \"url\": \"not a url\"}]}")]
    [InlineData("{\"merchants\": [{\"id\": \"a\", \"url\": \"ftp://a.cz/\"}]}")]
    [InlineData("{\"merchants\": [{\"id\": \"a\", \"url\": \"/relative\"}]}")]
    [InlineData("{\"merchants\": [{\"id\": 5, \"url\": \"https://a.cz/\"}]}")]
    [InlineData("{\"merchants\": [{\"id\": \"a\", \"url\": \"https://a.cz/\"}, {\"id\": \"a\", \"url\": \"https://b.cz/\"}]}")]
    public void Invalid_input_is_rejected(string json) => Assert.NotNull(Error(json));

    [Fact]
    public void Null_body_is_rejected() => Assert.NotNull(Endpoints.ParseMerchants(null).Error);

    [Fact]
    public void Valid_input_is_accepted()
    {
        var (merchants, error) = Endpoints.ParseMerchants(JsonSerializer.Deserialize<JsonElement>(
            "{\"merchants\": [{\"id\": \"shop-1\", \"url\": \"https://www.example-shop.cz/\"}]}"));
        Assert.Null(error);
        Assert.Equal([new MerchantInput("shop-1", "https://www.example-shop.cz/")], merchants);
    }
}

public class InvalidUtf8Tests
{
    [Fact]
    public void Invalid_utf8_in_merchant_id_is_rejected_not_thrown()
    {
        // 0xEC = "ě" in Windows-1250, as sent by a non-UTF-8 terminal; not valid UTF-8 on its own.
        byte[] bytes = [.. "{\"merchants\":[{\"id\":\"x"u8, 0xEC, .. "\",\"url\":\"https://a.cz/\"}]}"u8];
        var el = JsonSerializer.Deserialize<JsonElement>(bytes);
        Assert.NotNull(Endpoints.ParseMerchants(el).Error);
    }
}
