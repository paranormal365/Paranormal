using Ben.Data.Common;
using Ben.Data.Common.Interfaces;
using Ben.Data.WebApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Ben.Web.Tests;

/// <summary>A <see cref="ClientStatusMailer"/> over unconfigured mail: it sends nothing and never throws.</summary>
public static class TestMailer
{
    public static ClientStatusMailer Quiet()
    {
        var email = new Mock<IEmailService>();
        email.SetupGet(e => e.IsConfigured).Returns(false);
        return new ClientStatusMailer(email.Object, Options.Create(new SiteIdentity { Name = "Test", BaseUrl = "https://test.local" }),
                                      NullLogger<ClientStatusMailer>.Instance);
    }
}
