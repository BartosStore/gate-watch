using System.Text.Json;
using Api.Blacklist;
using Api.Data;
using Api.Findings;
using Api.Merchants;
using Api.Runs;
using Api.Scanning;
using Microsoft.EntityFrameworkCore;

namespace Api;

public static class ServiceSetup
{
    /// <summary>Everything except hosted services; shared by the app and the integration tests.</summary>
    public static IServiceCollection AddGateWatchCore(this IServiceCollection services, string connectionString, int scanConcurrency)
    {
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
        services.AddSingleton<BlacklistStore>();
        services.AddSingleton(new ScannerOptions());
        services.AddSingleton<BrowserHost>();
        services.AddSingleton<IPageScanner, PageScanner>();
        services.AddSingleton(new ScanLimiter(scanConcurrency));
        services.AddSingleton<RunQueue>();
        services.AddScoped<FindingService>();
        services.AddScoped<RunService>();
        services.AddScoped<MerchantQueries>();
        return services;
    }

    public static int ScanConcurrency(IConfiguration config) =>
        int.TryParse(config["SCAN_CONCURRENCY"], out var n) && n is > 0 and <= 64 ? n : 8;

    public static string DataDir(IConfiguration config, IHostEnvironment env) =>
        Path.GetFullPath(config["DataDir"] ?? Path.Combine(env.ContentRootPath, "data"));

    public static readonly JsonNamingPolicy JsonNaming = JsonNamingPolicy.SnakeCaseLower;
}
