using Api.Blacklist;

namespace Api.Tests;

public class BlacklistTests
{
    [Fact]
    public void Text_list_skips_comments_and_blank_lines_and_normalizes()
    {
        var urls = BlacklistParser.ParseText("# comment\n\nHTTPS://Evil.Example:443/x.js#a\r\nhttps://evil.example/x.js\nmailto:a@b.cz\nhttp://ok.invalid/\n");
        Assert.Equal(["https://evil.example/x.js", "http://ok.invalid/"], urls);
    }

    [Fact]
    public void Urlhaus_csv_takes_url_column()
    {
        const string csv = """
            ################################################################
            # abuse.ch URLhaus Database Dump (CSV - recent URLs)            #
            # id,dateadded,url,url_status,last_online,threat,tags,urlhaus_link,reporter
            "3645123","2026-11-12 10:00:00","http://198.51.100.23:8080/i.sh","online","2026-11-12 10:00:00","malware_download","elf,mirai","https://urlhaus.abuse.ch/url/3645123/","anonymous"
            "3645122","2026-11-12 09:59:00","https://Bad.Example/Payload.exe","offline","","malware_download","exe","https://urlhaus.abuse.ch/url/3645122/","anonymous"
            """;
        Assert.Equal(["http://198.51.100.23:8080/i.sh", "https://bad.example/Payload.exe"], BlacklistParser.ParseUrlhausCsv(csv));
    }

    [Fact]
    public void Store_is_not_ready_until_loaded_and_matches_exactly()
    {
        var store = new BlacklistStore();
        Assert.False(store.Ready);
        Assert.False(store.TryMatch("https://evil.example/x.js", out _));

        store.Load([new("https://evil.example/x.js", "custom")]);

        Assert.True(store.Ready);
        Assert.Equal(1, store.Count);
        Assert.True(store.TryMatch("https://evil.example/x.js", out var source));
        Assert.Equal("custom", source);
        Assert.False(store.TryMatch("https://evil.example/x.js?v=2", out _));
        Assert.False(store.TryMatch("https://evil.example/", out _));
    }

    [Fact]
    public void Stored_files_in_repository_parse()
    {
        var dataDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data");
        Assert.NotEmpty(BlacklistParser.ParseText(File.ReadAllText(Path.Combine(dataDir, "urlhaus-snapshot.txt"))));
        Assert.NotEmpty(BlacklistParser.ParseText(File.ReadAllText(Path.Combine(dataDir, "blacklist-custom.txt"))));
    }
}
