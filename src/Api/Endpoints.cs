using System.Text.Json;
using Api.Blacklist;
using Api.Domain;
using Api.Findings;
using Api.Merchants;
using Api.Runs;

namespace Api;

public static class Endpoints
{
    public const int MaxMerchantsPerRun = 25_000;

    private static IResult Error(int status, string message) => Results.Json(new { error = message }, statusCode: status);

    public static void MapApi(this WebApplication app)
    {
        app.MapPost("/runs", async (HttpRequest request, BlacklistStore blacklist, RunService runs, RunQueue queue, CancellationToken ct) =>
        {
            if (!blacklist.Ready) return Error(503, "Blacklist se ještě připravuje, zkuste to za chvíli.");

            var body = await ReadJsonAsync(request, ct);
            var (merchants, error) = ParseMerchants(body);
            if (error is not null) return Error(400, error);

            var runId = await runs.CreateRunAsync(merchants!, ct);
            await queue.EnqueueAsync(runId);
            return Results.Json(new { run_id = runId.ToString() }, statusCode: 202);
        });

        app.MapGet("/runs/{run_id}", async (string run_id, RunService runs, CancellationToken ct) =>
        {
            if (!Guid.TryParse(run_id, out var id)) return Error(404, "Kontrola neexistuje.");
            var run = await runs.GetRunAsync(id, ct);
            return run is null ? Error(404, "Kontrola neexistuje.") : Results.Ok(run);
        });

        app.MapPost("/alerts/{id}/resolve", async (string id, HttpRequest request, FindingService findings, CancellationToken ct) =>
        {
            var body = await ReadJsonAsync(request, ct);
            var note = body is { ValueKind: JsonValueKind.Object } && body.Value.TryGetProperty("note", out var noteEl) ? StringOf(noteEl) : null;
            if (note is null)
                return Error(400, "Tělo musí být JSON objekt s textovou položkou \"note\" (může být prázdná), v kódování UTF-8.");
            if (note.Length > 10_000) return Error(400, "Poznámka je příliš dlouhá (max. 10 000 znaků).");

            return await findings.ResolveAsync(id, note, ct)
                ? Results.Ok(new { ok = true })
                : Error(404, "Nález neexistuje.");
        });
    }

    /// <summary>Helper endpoints for the UI (documented in openapi.yaml).</summary>
    public static void MapUiApi(this WebApplication app)
    {
        app.MapGet("/alerts", async (HttpRequest request, MerchantQueries queries, CancellationToken ct) =>
        {
            var q = request.Query;
            if (!TryPaging(q, out var page, out var pageSize, out var err)) return Error(400, err!);
            bool? resolved = null;
            if (q.TryGetValue("resolved", out var r) && !string.IsNullOrEmpty(r))
            {
                if (!bool.TryParse(r, out var b)) return Error(400, "Parametr resolved musí být true nebo false.");
                resolved = b;
            }
            return Results.Ok(await queries.AlertsAsync(resolved, q["merchant_id"].FirstOrDefault(), page, pageSize, ct));
        });

        app.MapGet("/merchants", async (HttpRequest request, MerchantQueries queries, CancellationToken ct) =>
        {
            var q = request.Query;
            if (!TryPaging(q, out var page, out var pageSize, out var err)) return Error(400, err!);
            var status = q["status"].FirstOrDefault();
            if (!string.IsNullOrEmpty(status) && !MerchantQueries.Statuses.Contains(status))
                return Error(400, $"Parametr status musí být jeden z: {string.Join(", ", MerchantQueries.Statuses)}.");
            return Results.Ok(await queries.MerchantsAsync(q["search"].FirstOrDefault(), status, page, pageSize, ct));
        });
    }

    public const int MaxPageSize = 1000;

    private static bool TryPaging(IQueryCollection q, out int page, out int pageSize, out string? error)
    {
        page = 1;
        pageSize = 25;
        error = null;
        if (q.TryGetValue("page", out var p) && !string.IsNullOrEmpty(p) && (!int.TryParse(p, out page) || page < 1))
            error = "Parametr page musí být celé číslo ≥ 1.";
        else if (q.TryGetValue("pageSize", out var s) && !string.IsNullOrEmpty(s) && (!int.TryParse(s, out pageSize) || pageSize is < 1 or > MaxPageSize))
            error = $"Parametr pageSize musí být celé číslo 1–{MaxPageSize}.";
        return error is null;
    }

    private static async Task<JsonElement?> ReadJsonAsync(HttpRequest request, CancellationToken ct)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<JsonElement>(request.Body, cancellationToken: ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>String value, or null when it is not a string or not valid UTF-8 (=> 400 instead of 500).</summary>
    private static string? StringOf(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.String) return null;
        try { return el.GetString(); }
        catch (InvalidOperationException) { return null; }
    }

    public static (List<MerchantInput>? Merchants, string? Error) ParseMerchants(JsonElement? body)
    {
        if (body is not { ValueKind: JsonValueKind.Object } root || !root.TryGetProperty("merchants", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return (null, "Tělo musí být JSON objekt s polem \"merchants\".");
        if (arr.GetArrayLength() == 0) return (null, "Pole \"merchants\" nesmí být prázdné.");
        if (arr.GetArrayLength() > MaxMerchantsPerRun) return (null, $"Najednou lze kontrolovat nejvýše {MaxMerchantsPerRun} obchodů.");

        var list = new List<MerchantInput>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var i = 0;
        foreach (var m in arr.EnumerateArray())
        {
            if (m.ValueKind != JsonValueKind.Object) return (null, $"merchants[{i}] musí být objekt s položkami id a url.");
            var id = m.TryGetProperty("id", out var idEl) ? StringOf(idEl)?.Trim() : null;
            var url = m.TryGetProperty("url", out var urlEl) ? StringOf(urlEl)?.Trim() : null;
            if (string.IsNullOrEmpty(id) || id.Length > 200) return (null, $"merchants[{i}].id musí být neprázdný text (max. 200 znaků).");
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out _) || UrlNormalizer.Normalize(url) is null || url.Length > 2000)
                return (null, $"merchants[{i}].url není platná absolutní http(s) adresa.");
            if (!ids.Add(id)) return (null, $"merchants[{i}].id \"{id}\" je v požadavku vícekrát.");
            list.Add(new MerchantInput(id, url));
            i++;
        }
        return (list, null);
    }
}
