using Api.Domain;

namespace Api.Tests;

public class UrlNormalizerTests
{
    private const string Page = "https://www.shop.cz/kategorie/boty/index.html?x=1";

    [Theory]
    [InlineData("/static/app.js", "https://www.shop.cz/static/app.js")]
    [InlineData("app.js", "https://www.shop.cz/kategorie/boty/app.js")]
    [InlineData("../img/logo.png", "https://www.shop.cz/kategorie/img/logo.png")]
    [InlineData("//cdn.Example.com/lib.js", "https://cdn.example.com/lib.js")]
    [InlineData("?page=2", "https://www.shop.cz/kategorie/boty/index.html?page=2")]
    [InlineData("http://other.cz/a", "http://other.cz/a")]
    public void Relative_urls_are_resolved_against_page(string raw, string expected) =>
        Assert.Equal(expected, UrlNormalizer.Normalize(raw, Page));

    [Theory]
    [InlineData("http://shop.cz:80/a", "http://shop.cz/a")]
    [InlineData("https://shop.cz:443/a", "https://shop.cz/a")]
    [InlineData("https://shop.cz:8443/a", "https://shop.cz:8443/a")]
    [InlineData("http://shop.cz:443/a", "http://shop.cz:443/a")]
    public void Default_port_is_dropped(string raw, string expected) =>
        Assert.Equal(expected, UrlNormalizer.Normalize(raw));

    [Theory]
    [InlineData("https://shop.cz/a#top", "https://shop.cz/a")]
    [InlineData("https://shop.cz/a?q=1#top", "https://shop.cz/a?q=1")]
    [InlineData("https://shop.cz/#", "https://shop.cz/")]
    public void Fragment_is_dropped(string raw, string expected) =>
        Assert.Equal(expected, UrlNormalizer.Normalize(raw));

    [Theory]
    [InlineData("HTTPS://WWW.Shop.CZ/Path/File.JS", "https://www.shop.cz/Path/File.JS")]
    [InlineData("HtTp://Shop.cz", "http://shop.cz/")]
    public void Scheme_and_host_are_lowercased_path_is_not(string raw, string expected) =>
        Assert.Equal(expected, UrlNormalizer.Normalize(raw));

    [Theory]
    [InlineData("https://shop.cz", "https://shop.cz/")]
    [InlineData("https://shop.cz?a=1", "https://shop.cz/?a=1")]
    public void Empty_path_becomes_slash(string raw, string expected) =>
        Assert.Equal(expected, UrlNormalizer.Normalize(raw));

    [Fact]
    public void Query_string_is_kept_as_is() =>
        Assert.Equal("https://shop.cz/a?b=2&a=1&c", UrlNormalizer.Normalize("https://shop.cz/a?b=2&a=1&c"));

    [Fact]
    public void User_info_is_dropped() =>
        Assert.Equal("https://shop.cz/a", UrlNormalizer.Normalize("https://user:pass@shop.cz/a"));

    [Fact]
    public void Surrounding_whitespace_is_ignored() =>
        Assert.Equal("https://shop.cz/a", UrlNormalizer.Normalize("  https://shop.cz/a \n"));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:info@shop.cz")]
    [InlineData("tel:+420123456789")]
    [InlineData("data:text/javascript,alert(1)")]
    [InlineData("ftp://shop.cz/file.zip")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ws://shop.cz/socket")]
    [InlineData("blob:https://shop.cz/123")]
    public void Unsupported_schemes_are_rejected(string raw)
    {
        Assert.Null(UrlNormalizer.Normalize(raw));
        Assert.Null(UrlNormalizer.Normalize(raw, Page));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://")]
    [InlineData("/relative/without/base")]
    [InlineData("not a url")]
    public void Invalid_input_returns_null(string? raw) =>
        Assert.Null(UrlNormalizer.Normalize(raw));

    [Fact]
    public void Same_url_written_differently_normalizes_to_same_value()
    {
        var a = UrlNormalizer.Normalize("HTTPS://Evil.example:443/x.js?v=1#frag");
        var b = UrlNormalizer.Normalize("/x.js?v=1", "https://evil.example/");
        Assert.Equal("https://evil.example/x.js?v=1", a);
        Assert.Equal(a, b);
    }
}
