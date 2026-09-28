using System.Security.Claims;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Services.Push;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The phones a person may be pushed on (item 252): registered at sign-in, taken over by whoever
/// signs in on the phone next, removed at sign-out, and forgotten when Apple says they are gone.
/// </summary>
public sealed class PushDeviceTests
{
    private const string Token = "A1B2C3D4E5F60718293A4B5C6D7E8F90A1B2C3D4E5F60718293A4B5C6D7E8F90";
    private static readonly string Stored = Token.ToLowerInvariant();

    private sealed class SimpleFactory(DbContextOptions<BenDataContext> opts) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(opts);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(opts));
    }

    private static IDbContextFactory<BenDataContext> Factory()
        => new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static MyPushDevicesController As(IDbContextFactory<BenDataContext> db, Guid userId) => new(db)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
            },
        },
    };

    private static MyPushDevicesController.RegisterPushDeviceRequest Request(string token = Token, string env = "production")
        => new(token, env, "1.1.0");

    [Fact]
    public async Task APhoneIsRegisteredForThePersonSignedIn()
    {
        var db = Factory();
        var ben = Guid.NewGuid();

        Assert.IsType<NoContentResult>(await As(db, ben).Register(Request(env: "sandbox"), default));

        await using var ctx = await db.CreateDbContextAsync();
        var device = Assert.Single(ctx.PushDevices);
        Assert.Equal(ben, device.AppUserId);
        Assert.Equal(Stored, device.Token);
        Assert.Equal(PushEnvironment.Sandbox, device.Environment);
        Assert.Equal("1.1.0", device.AppVersion);
    }

    [Fact]
    public async Task ThePhoneInNewHandsStopsReachingThePersonBefore()
    {
        var db = Factory();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await As(db, first).Register(Request(), default);

        await As(db, second).Register(Request(), default);

        await using var ctx = await db.CreateDbContextAsync();
        Assert.Equal(second, Assert.Single(ctx.PushDevices).AppUserId);
    }

    [Theory]
    [InlineData("not-a-token", "production")]
    [InlineData("abc123", "production")]
    [InlineData(Token, "staging")]
    [InlineData(Token, "")]
    public async Task ATokenOrServiceTheAppWouldNotSendIsRefused(string token, string env)
    {
        var db = Factory();
        Assert.IsType<BadRequestObjectResult>(await As(db, Guid.NewGuid()).Register(Request(token, env), default));
        await using var ctx = await db.CreateDbContextAsync();
        Assert.Empty(ctx.PushDevices);
    }

    [Fact]
    public async Task SigningOutRemovesOnlyYourOwnPhone()
    {
        var db = Factory();
        var owner = Guid.NewGuid();
        await As(db, owner).Register(Request(), default);

        // Somebody else naming the token changes nothing, and learns nothing.
        Assert.IsType<NoContentResult>(await As(db, Guid.NewGuid()).Remove(Token, default));
        await using (var ctx = await db.CreateDbContextAsync()) Assert.Single(ctx.PushDevices);

        Assert.IsType<NoContentResult>(await As(db, owner).Remove(Token, default));
        await using (var ctx = await db.CreateDbContextAsync()) Assert.Empty(ctx.PushDevices);
    }

    /// <summary>Scripted per token; remembers what it was asked to send.</summary>
    private sealed class FakeSender(bool configured = true) : IPushSender
    {
        public readonly Dictionary<string, PushOutcome> Answers = new();
        public readonly List<string> SentTo = new();
        public bool IsConfigured => configured;

        public Task<PushOutcome> SendAsync(string token, PushEnvironment environment, PushMessage message, CancellationToken ct)
        {
            lock (SentTo) SentTo.Add(token);
            return Task.FromResult(Answers.GetValueOrDefault(token, PushOutcome.Delivered));
        }
    }

    private static async Task SeedDevice(IDbContextFactory<BenDataContext> db, Guid userId, string token)
    {
        await using var ctx = await db.CreateDbContextAsync();
        ctx.PushDevices.Add(new PushDevice
        {
            Id = Guid.NewGuid(), AppUserId = userId, Token = token, Environment = PushEnvironment.Production,
            DateCreated = DateTime.UtcNow, LastSeenUtc = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }

    private static readonly PushMessage Message = new("The walk is starting", "Tap to open Field Kit.", new Dictionary<string, string>());

    [Fact]
    public async Task EveryPhoneOfThePeopleNamedIsPushedAndNobodyElse()
    {
        var db = Factory();
        var (a, b, stranger, noApp) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await SeedDevice(db, a, "aa01");
        await SeedDevice(db, a, "aa02");   // an iPhone and an iPad
        await SeedDevice(db, b, "bb01");
        await SeedDevice(db, stranger, "cc01");
        var sender = new FakeSender();

        var fanOut = await new PushNotifier(db, sender, NullLogger<PushNotifier>.Instance)
            .SendAsync([a, b, noApp], Message, default);

        Assert.Equal(["aa01", "aa02", "bb01"], sender.SentTo.Order());
        Assert.Equal(new PushFanOut(People: 3, PeopleWithTheApp: 2, Delivered: 3, Failed: 0, Forgotten: 0, Configured: true), fanOut);
    }

    [Fact]
    public async Task APhoneAppleSaysIsGoneIsForgotten()
    {
        var db = Factory();
        var a = Guid.NewGuid();
        await SeedDevice(db, a, "aa01");
        await SeedDevice(db, a, "aa02");
        var sender = new FakeSender();
        sender.Answers["aa01"] = PushOutcome.Unregistered;
        sender.Answers["aa02"] = PushOutcome.Failed;

        var fanOut = await new PushNotifier(db, sender, NullLogger<PushNotifier>.Instance).SendAsync([a], Message, default);

        Assert.Equal((0, 1, 1), (fanOut.Delivered, fanOut.Failed, fanOut.Forgotten));
        await using var ctx = await db.CreateDbContextAsync();
        Assert.Equal("aa02", Assert.Single(ctx.PushDevices).Token);   // a failure is kept for next time
    }

    [Fact]
    public async Task WithNoKeyItSaysSoAndSendsNothing()
    {
        var db = Factory();
        var a = Guid.NewGuid();
        await SeedDevice(db, a, "aa01");
        var sender = new FakeSender(configured: false);

        var fanOut = await new PushNotifier(db, sender, NullLogger<PushNotifier>.Instance).SendAsync([a], Message, default);

        Assert.False(fanOut.Configured);
        Assert.Equal(1, fanOut.PeopleWithTheApp);
        Assert.Empty(sender.SentTo);
    }
}
