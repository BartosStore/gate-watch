using System.Net;
using System.Text;
using System.Text.Json;
using Api.Scanning;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests;

/// <summary>
/// End-to-end over HTTP against the real app (Program) with Postgres and a fake scanner:
/// documentation endpoints and the exact JSON shape of the three mandatory operations.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class HttpApiTests : IAsyncLifetime
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private readonly FakeScanner _scanner = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _http;

    public HttpApiTests(PostgresFixture db)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            // UseSetting applies before Program reads its configuration (ConfigureAppConfiguration would be too late).
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Db", db.ConnectionString);
            b.UseSetting("DataDir", Path.Combine(RepoRoot, "data"));
            b.UseSetting("OpenApiPath", Path.Combine(RepoRoot, "openapi.yaml"));
            b.ConfigureServices(s => s.AddSingleton<IPageScanner>(_scanner));
        });
        _http = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public async Task InitializeAsync()
    {
        // POST /runs answers 503 until the blacklist is loaded.
        for (var i = 0; i < 100; i++)
        {
            var health = await _http.GetStringAsync("/health");
            if (health.Contains("\"blacklist_ready\":true")) return;
            await Task.Delay(100);
        }
        throw new TimeoutException("Blacklist was not loaded");
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static JsonElement Parse(string body) => JsonDocument.Parse(body).RootElement;

    private static string[] Keys(JsonElement e) => e.EnumerateObject().Select(p => p.Name).Order().ToArray();

    [PostgresFact]
    public async Task Openapi_yaml_is_served_unchanged_from_repository()
    {
        var res = await _http.GetAsync("/openapi.yaml");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/yaml", res.Content.Headers.ContentType?.MediaType);
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(RepoRoot, "openapi.yaml")), await res.Content.ReadAsByteArrayAsync());
    }

    [PostgresFact]
    public async Task Swagger_ui_is_served_at_docs_and_points_to_openapi_yaml()
    {
        var redirect = await _http.GetAsync("/docs");
        Assert.True(redirect.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.OK);

        var index = await _http.GetStringAsync("/docs/index.html");
        Assert.Contains("swagger-ui", index);

        var init = await _http.GetStringAsync("/docs/index.js");
        Assert.Contains("/openapi.yaml", init);

        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/docs/swagger-ui-bundle.js")).StatusCode);
    }

    [PostgresFact]
    public async Task Mandatory_operations_match_openapi_shapes_over_http()
    {
        var id = "http-" + Guid.NewGuid().ToString("N")[..8];
        _scanner.Handler = _ => FakeScanner.Ok("https://evil.example/skimmer.js"); // entry from data/blacklist-custom.txt

        // 400 / 404 with {error}
        var bad = await _http.PostAsync("/runs", Json("{\"merchants\":[]}"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(["error"], Keys(Parse(await bad.Content.ReadAsStringAsync())));
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/runs/not-a-run")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.PostAsync("/alerts/unknown/resolve", Json("{\"note\":\"\"}"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.PostAsync("/alerts/unknown/resolve", Json("{}"))).StatusCode);

        // 202 {run_id}
        var created = await _http.PostAsync("/runs", Json($"{{\"merchants\":[{{\"id\":\"{id}\",\"url\":\"https://shop.example/\"}}]}}"));
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        var createdBody = Parse(await created.Content.ReadAsStringAsync());
        Assert.Equal(["run_id"], Keys(createdBody));
        var runId = createdBody.GetProperty("run_id").GetString();

        JsonElement run = default;
        for (var i = 0; i < 100; i++)
        {
            run = Parse(await _http.GetStringAsync($"/runs/{runId}"));
            if (run.GetProperty("status").GetString() != "running") break;
            await Task.Delay(100);
        }
        Assert.Equal(["error", "results", "run_id", "status"], Keys(run));
        Assert.Equal("done", run.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, run.GetProperty("error").ValueKind);

        var result = run.GetProperty("results").EnumerateArray().Single();
        Assert.Equal(["alerts", "error", "matches", "merchant_id", "status"], Keys(result));
        Assert.Equal("ok", result.GetProperty("status").GetString());
        Assert.Equal("https://evil.example/skimmer.js", result.GetProperty("matches")[0].GetString());
        var alert = result.GetProperty("alerts").EnumerateArray().Single();
        Assert.Equal(["id", "note", "resolved", "url"], Keys(alert));

        // 200 {ok: true}, visible in GET /runs afterwards
        var resolved = await _http.PostAsync($"/alerts/{alert.GetProperty("id").GetString()}/resolve", Json("{\"note\":\"ověřeno\"}"));
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        Assert.Equal("{\"ok\":true}", await resolved.Content.ReadAsStringAsync());
        var after = Parse(await _http.GetStringAsync($"/runs/{runId}")).GetProperty("results")[0].GetProperty("alerts")[0];
        Assert.True(after.GetProperty("resolved").GetBoolean());
        Assert.Equal("ověřeno", after.GetProperty("note").GetString());
    }
}
