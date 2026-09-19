using System.Net;
using Ben.Data.WebApi.Services.LinkUnfurl;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Which addresses the link-unfurl fetcher may be pointed at, and which network addresses it may
/// connect to (canvas plan M6-08 and review R21).
/// </summary>
/// <remarks>
/// <para>The fetcher exists because Ben reversed <c>PublicLinkPreviewController</c>'s no-fetch rule
/// for the canvas on 2026-09-14. Everything that made that rule right — a request from our server
/// to an address a stranger chose — is still true, so this table is the part that has to be right:
/// a cloud metadata endpoint, the SQL Server on this box's own LAN address, a router's admin page,
/// or an IPv6 spelling of any of those.</para>
///
/// <para>The IPv6 forms that carry an IPv4 inside them are the classic bypass: a filter that knows
/// 127.0.0.1 is loopback and does not know <c>::ffff:127.0.0.1</c>, <c>64:ff9b::7f00:1</c> or
/// <c>2002:7f00:1::</c> is the same address is a filter with a door in it.</para>
/// </remarks>
public sealed class SafeUrlPolicyTests
{
    [Theory]
    [InlineData("http://example.com/", "https")]
    [InlineData("ftp://example.com/", "https")]
    [InlineData("https://user@example.com/", "user")]
    [InlineData("https://user:pass@example.com/", "user")]
    [InlineData("https://10.0.0.1/", "address")]
    [InlineData("https://93.184.216.34/", "address")]
    [InlineData("https://[::1]/", "address")]
    [InlineData("https://[2606:2800:220:1:248:1893:25c8:1946]/", "address")]
    [InlineData("https://localhost/", "name")]
    [InlineData("https://localhost./", "name")]
    [InlineData("https://example.com:8443/", "port")]
    [InlineData("https://printer.local/", "name")]
    [InlineData("https://db.internal/", "name")]
    [InlineData("https://app.localhost/", "name")]
    [InlineData("https://router.home.arpa/", "name")]
    [InlineData("https://intranet/", "name")]
    [InlineData("https://0x7f000001/", "")]          // .NET may read this as 127.0.0.1; refused either way
    [InlineData("https://2130706433/", "")]
    public void Refused(string url, string reasonMentions)
    {
        var refusal = SafeUrlPolicy.Refuse(new Uri(url));

        Assert.NotNull(refusal);
        Assert.Contains(reasonMentions, refusal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_url_longer_than_2048_characters_is_refused()
    {
        var url = new Uri("https://example.com/" + new string('a', 2049 - "https://example.com/".Length));
        Assert.Equal(2049, url.OriginalString.Length);

        Assert.NotNull(SafeUrlPolicy.Refuse(url));
        Assert.Null(SafeUrlPolicy.Refuse(new Uri("https://example.com/" + new string('a', 2048 - "https://example.com/".Length))));
    }

    [Fact]
    public void A_relative_url_is_refused()
        => Assert.NotNull(SafeUrlPolicy.Refuse(new Uri("/only/a/path", UriKind.Relative)));

    [Theory]
    [InlineData("https://example.com/a")]
    [InlineData("https://example.com:443/a?b=c#d")]
    [InlineData("https://www.bbc.co.uk/news")]
    [InlineData("https://EXAMPLE.com/")]
    [InlineData("https://xn--bcher-kva.example/")]
    public void Allowed(string url) => Assert.Null(SafeUrlPolicy.Refuse(new Uri(url)));

    [Theory]
    // IPv4 private, loopback, link-local (cloud metadata), CGNAT, benchmarking, documentation, multicast, reserved
    [InlineData("0.0.0.0")]
    [InlineData("0.1.2.3")]
    [InlineData("10.1.2.3")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.254")]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.255")]
    [InlineData("169.254.169.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.0.0.8")]
    [InlineData("192.0.2.1")]
    [InlineData("192.88.99.1")]
    [InlineData("192.168.1.71")]     // this box's own LAN address, where SQL Server listens
    [InlineData("198.18.0.1")]
    [InlineData("198.51.100.7")]
    [InlineData("203.0.113.9")]
    [InlineData("224.0.0.1")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    // IPv6 plain
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fd00:ec2::254")]    // AWS metadata over IPv6
    [InlineData("fe80::1")]
    [InlineData("fec0::1")]
    [InlineData("2001:db8::1")]
    [InlineData("ff02::1")]
    // IPv6 carrying a forbidden IPv4 (R21)
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::7f00:1")]                   // IPv4-compatible ::/96
    [InlineData("64:ff9b::a00:1")]             // NAT64 of 10.0.0.1
    [InlineData("64:ff9b::a9fe:a9fe")]         // NAT64 of 169.254.169.254
    [InlineData("64:ff9b:1::a00:1")]           // local-use NAT64, 64:ff9b:1::/48
    [InlineData("2002:7f00:1::")]              // 6to4 of 127.0.0.1
    [InlineData("2002:c0a8:147::1")]           // 6to4 of 192.168.1.71
    [InlineData("2001:0:4136:e378:8000:63bf:80ff:fffe")]  // Teredo, client 127.0.0.1 (obfuscated)
    [InlineData("2001:0:a00:1::1")]            // Teredo, server 10.0.0.1
    [InlineData("100::1")]                     // discard-only 100::/64
    public void Forbidden_addresses(string address)
        => Assert.True(SafeUrlPolicy.IsForbidden(IPAddress.Parse(address)), $"{address} must be refused");

    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("1.1.1.1")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.0")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.0")]
    [InlineData("2606:2800:220:1:248:1893:25c8:1946")]
    [InlineData("::ffff:93.184.216.34")]
    [InlineData("64:ff9b::5db8:d822")]         // NAT64 of 93.184.216.34
    [InlineData("2002:5db8:d822::1")]          // 6to4 of 93.184.216.34
    public void Public_addresses(string address)
        => Assert.False(SafeUrlPolicy.IsForbidden(IPAddress.Parse(address)), $"{address} is a public address");
}
