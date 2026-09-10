using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Apple;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ben.Web.Tests.Support;

/// <summary>The Apple credential service as tests need it: a real store over a test database, Apple itself mocked.</summary>
internal static class AppleTestSupport
{
    /// <summary>A token client that is configured and says yes to everything, unless a test says otherwise.</summary>
    public static Mock<IAppleTokenClient> TokenClient()
    {
        var mock = new Mock<IAppleTokenClient>();
        mock.SetupGet(c => c.IsConfigured).Returns(true);
        mock.Setup(c => c.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string code, string _, CancellationToken _) => new AppleTokenExchange($"refresh-for-{code}", "001234.abc", null));
        mock.Setup(c => c.RevokeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return mock;
    }

    /// <summary>
    /// One key ring for the test process. A fresh ephemeral provider per service instance would
    /// mean the instance that revokes cannot read what the instance that remembered wrote — which
    /// is a real failure mode in production (a lost key ring), and the service handles it by
    /// dropping the row, but it is not what these tests are about.
    /// </summary>
    private static readonly IDataProtectionProvider Protection = new EphemeralDataProtectionProvider();

    public static AppleCredentialService Credentials(IDbContextFactory<BenDataContext> db, Mock<IAppleTokenClient>? apple = null) =>
        new(db, (apple ?? TokenClient()).Object, Protection, NullLogger<AppleCredentialService>.Instance);
}
