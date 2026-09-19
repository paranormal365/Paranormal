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

    // ── The mechanism is pinned ───────────────────────────────────────────────
    //
    // The relay advertises PLAIN and LOGIN and has refused what MailKit picked on its own; on
    // 2026-08-31 that was hours of "5.7.8 authentication failed" that no page could see. The
    // service now removes everything else from the advertised set before authenticating.

    [Fact]
    public void Only_PLAIN_and_LOGIN_survive_the_pin()
    {
        var advertised = new HashSet<string> { "XOAUTH2", "NTLM", "CRAM-MD5", "PLAIN", "LOGIN", "SCRAM-SHA-256" };

        SmtpEmailService.KeepOnlyPlainAndLogin(advertised);

        Assert.Equal(new[] { "LOGIN", "PLAIN" }, advertised.Order());
    }

    [Fact]
    public void A_server_offering_neither_is_left_with_nothing_rather_than_something_untested()
    {
        // MailKit then refuses to authenticate with a clear sentence, which the Outgoing Mail
        // page shows — better than a mechanism this service has never been proven against.
        var advertised = new HashSet<string> { "XOAUTH2" };

        SmtpEmailService.KeepOnlyPlainAndLogin(advertised);

        Assert.Empty(advertised);
    }

    [Fact]
    public void The_pin_is_applied_before_authenticating()
    {
        // Reading the source, as the other guards do: a call that exists but comes after
        // AuthenticateAsync pins nothing.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);

        var source = File.ReadAllText(Path.Combine(dir!.FullName, "Ben.Data.WebApi", "Services", "SmtpEmailService.cs"));
        var pin  = source.IndexOf("KeepOnlyPlainAndLogin(client.AuthenticationMechanisms)", StringComparison.Ordinal);
        var auth = source.IndexOf("client.AuthenticateAsync(", StringComparison.Ordinal);

        Assert.True(pin >= 0, "SendAsync no longer pins the SASL mechanism.");
        Assert.True(auth >= 0);
        Assert.True(pin < auth, "The pin must run before AuthenticateAsync, not after.");
    }
}
