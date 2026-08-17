using Ben.Data.WebApi.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>Tests for SmtpEmailService.IsConfigured — the flag every caller checks before
/// promising a recipient an email is on its way.</summary>
public class SmtpEmailServiceTests
{
    [Fact]
    public void IsConfigured_NoHost_ReturnsFalse()
    {
        var service = new SmtpEmailService(Options.Create(new SmtpOptions()), Options.Create(new Ben.Data.Common.SiteIdentity()));
        Assert.False(service.IsConfigured);
    }

    [Fact]
    public void IsConfigured_WhitespaceHost_ReturnsFalse()
    {
        var service = new SmtpEmailService(Options.Create(new SmtpOptions { Host = "   " }), Options.Create(new Ben.Data.Common.SiteIdentity()));
        Assert.False(service.IsConfigured);
    }

    [Fact]
    public void IsConfigured_HostSet_ReturnsTrue()
    {
        var service = new SmtpEmailService(Options.Create(new SmtpOptions { Host = "smtp.example.com" }), Options.Create(new Ben.Data.Common.SiteIdentity()));
        Assert.True(service.IsConfigured);
    }

    [Fact]
    public async Task SendAsync_Unconfigured_Throws()
    {
        var service = new SmtpEmailService(Options.Create(new SmtpOptions()), Options.Create(new Ben.Data.Common.SiteIdentity()));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendAsync("to@test.com", "subject", "<p>body</p>"));
    }
}
