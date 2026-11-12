using Api.Scanning;

namespace Api.Tests;

public class UrlGuardTests
{
    [Theory]
    [InlineData("http://localhost/")]
    [InlineData("http://LOCALHOST:8080/admin")]
    [InlineData("http://app.localhost/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://127.10.0.5:9000/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://172.16.0.1/")]
    [InlineData("http://172.31.255.255/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://0.0.0.0/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://[fd00::1]/")]
    [InlineData("http://[::ffff:192.168.0.1]/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/")]
    [InlineData("chrome://settings")]
    [InlineData("not a url")]
    public void Blocks_private_local_and_non_http(string url) => Assert.True(UrlGuard.IsForbidden(url));

    [Theory]
    [InlineData("https://www.shop.cz/")]
    [InlineData("http://172.15.0.1/")]
    [InlineData("http://172.32.0.1/")]
    [InlineData("http://8.8.8.8/")]
    [InlineData("https://[2001:4860:4860::8888]/")]
    [InlineData("https://localhost.example.com/")]
    public void Allows_public_http(string url) => Assert.False(UrlGuard.IsForbidden(url));
}
