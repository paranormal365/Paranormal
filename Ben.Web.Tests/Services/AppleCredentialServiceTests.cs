using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Apple;
using Ben.Web.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>Keeping a refresh token at sign-in and spending it at deletion (item 229).</summary>
public class AppleCredentialServiceTests
{
    private static IDbContextFactory<BenDataContext> Db() =>
        new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<Guid> PersonAsync(IDbContextFactory<BenDataContext> factory)
    {
        var id = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser { Id = id, Email = "a@b.test", UserName = "a@b.test", DisplayName = "A", Handle = "a" });
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task ACodeBuysAProtectedTokenOnePerClientReplacedOnRepeat()
    {
        var factory = Db(); var userId = await PersonAsync(factory);
        var apple = AppleTestSupport.TokenClient();
        var service = AppleTestSupport.Credentials(factory, apple);

        Assert.True(await service.RememberAsync(userId, "com.ishaunted.ios", "code-1", default));
        Assert.True(await service.RememberAsync(userId, "com.ishaunted.ios", "code-2", default));   // same client: replaced
        Assert.True(await service.RememberAsync(userId, "com.ishaunted.web", "code-3", default));   // another client: added

        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.AppleCredentials.Where(c => c.AppUserId == userId).OrderBy(c => c.ClientId).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(["com.ishaunted.ios", "com.ishaunted.web"], rows.Select(r => r.ClientId));
        Assert.All(rows, r => Assert.DoesNotContain("refresh-for-", r.ProtectedRefreshToken));   // protected, not plain
        Assert.All(rows, r => Assert.Equal("001234.abc", r.Subject));
        apple.Verify(c => c.ExchangeCodeAsync("code-2", "com.ishaunted.ios", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoCodeOrNoKeyMeansNoCallAndNothingKept()
    {
        var factory = Db(); var userId = await PersonAsync(factory);
        var apple = AppleTestSupport.TokenClient();

        Assert.False(await AppleTestSupport.Credentials(factory, apple).RememberAsync(userId, "com.ishaunted.ios", null, default));
        Assert.False(await AppleTestSupport.Credentials(factory, apple).RememberAsync(userId, "com.ishaunted.ios", "  ", default));
        apple.SetupGet(c => c.IsConfigured).Returns(false);
        Assert.False(await AppleTestSupport.Credentials(factory, apple).RememberAsync(userId, "com.ishaunted.ios", "code", default));

        apple.Verify(c => c.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(db.AppleCredentials);
    }

    [Fact]
    public async Task AFailedExchangeKeepsNothingAndIsNotAnError()
    {
        var factory = Db(); var userId = await PersonAsync(factory);
        var apple = AppleTestSupport.TokenClient();
        apple.Setup(c => c.ExchangeCodeAsync("bad", "com.ishaunted.ios", It.IsAny<CancellationToken>()))
             .ReturnsAsync(new AppleTokenExchange(null, null, "invalid_grant"));

        Assert.False(await AppleTestSupport.Credentials(factory, apple).RememberAsync(userId, "com.ishaunted.ios", "bad", default));
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(db.AppleCredentials);
    }

    /// <summary>Each token goes to Apple under the client it was minted for, unprotected only in flight.</summary>
    [Fact]
    public async Task RevokingPresentsEachTokenWithItsClientAndDeletesWhatAppleAccepted()
    {
        var factory = Db(); var userId = await PersonAsync(factory);
        var apple = AppleTestSupport.TokenClient();
        var service = AppleTestSupport.Credentials(factory, apple);
        await service.RememberAsync(userId, "com.ishaunted.ios", "c-ios", default);
        await service.RememberAsync(userId, "com.ishaunted.web", "c-web", default);

        Assert.Equal(2, await service.RevokeAllAsync(userId, default));

        apple.Verify(c => c.RevokeAsync("refresh-for-c-ios", "com.ishaunted.ios", It.IsAny<CancellationToken>()), Times.Once);
        apple.Verify(c => c.RevokeAsync("refresh-for-c-web", "com.ishaunted.web", It.IsAny<CancellationToken>()), Times.Once);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(db.AppleCredentials);
    }

    [Fact]
    public async Task ARefusedRevocationKeepsTheRowStampedForAnotherDay()
    {
        var factory = Db(); var userId = await PersonAsync(factory);
        var apple = AppleTestSupport.TokenClient();
        apple.Setup(c => c.RevokeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var service = AppleTestSupport.Credentials(factory, apple);
        await service.RememberAsync(userId, "com.ishaunted.ios", "c-ios", default);

        Assert.Equal(0, await service.RevokeAllAsync(userId, default));

        await using var db = await factory.CreateDbContextAsync();
        var row = Assert.Single(db.AppleCredentials);
        Assert.NotNull(row.DateRevocationFailed);
    }

    [Fact]
    public async Task NothingKeptMeansNothingAsked()
    {
        var factory = Db(); var userId = await PersonAsync(factory);
        var apple = AppleTestSupport.TokenClient();

        Assert.Equal(0, await AppleTestSupport.Credentials(factory, apple).RevokeAllAsync(userId, default));
        apple.Verify(c => c.RevokeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
