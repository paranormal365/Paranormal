using System.Net;
using Ben.Data.WebApi.Services.LinkPreviews;
using Xunit;

namespace Ben.Web.Tests.LinkPreviews;

/// <summary>The addresses a link preview may never make this server connect to.</summary>
public sealed class OutboundUrlGuardTests
{
    [Theory]
    [InlineData("https://example.com/page")]
    [InlineData("http://example.com:80/")]
    [InlineData("https://example.com:443/")]
    public void Plain_web_addresses_are_allowed(string url) => Assert.Null(OutboundUrlGuard.WhyNot(new Uri(url)));

    [Theory]
    [InlineData("ftp://example.com/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:pass@example.com/")]
    [InlineData("http://example.com:8080/")]
    [InlineData("http://example.com:22/")]
    [InlineData("http://localhost/")]
    [InlineData("http://intranet/")]
    [InlineData("http://printer.local/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://[::1]/")]
    public void Anything_else_is_refused_with_a_reason(string url) => Assert.NotNull(OutboundUrlGuard.WhyNot(new Uri(url)));

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("93.184.216.34", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("0.0.0.0", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("192.168.1.71", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fd12:3456::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("2001:db8::1", false)]
    public void Only_public_internet_addresses_are_public(string address, bool expected) =>
        Assert.Equal(expected, OutboundUrlGuard.IsPublic(IPAddress.Parse(address)));

    [Fact]
    public async Task A_host_that_is_a_private_literal_resolves_to_nothing_dialable() =>
        Assert.Empty(await OutboundUrlGuard.PublicAddressesAsync("192.168.1.71", default));
}
