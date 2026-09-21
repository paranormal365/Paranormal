using Ben.Data.Common;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Admin;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Somebody who had an account made for them is told, and handed the way to take it over.
/// </summary>
/// <remarks>
/// <para><b>What this closes.</b> An account could be created under somebody's address with a
/// password a stranger typed, and nothing told them. They held an account on a live site they had
/// never heard of, with a password they did not know and could not change — because changing it
/// starts with knowing it exists. The letter had been declared since the letter list was written
/// and was sent by nothing.</para>
///
/// <para>The second test is the one that matters: what goes out must be a way to CHOOSE a password,
/// never the password itself. Mailing it would make two people who know it rather than one,
/// permanently, in a message that sits in an inbox.</para>
/// </remarks>
public sealed class AccountMadeForYouTests
{
    /// <summary>
    /// The password an administrator typed, generated per run.
    /// </summary>
    /// <remarks>
    /// Not a literal. This repository is public and development shares production's database, so
    /// anything password-shaped in a tracked file reads as a live credential — a rule
    /// <c>NoCredentialsInTheRepoTests</c> enforces rather than trusts. Generating it also makes
    /// the assertion below stronger: the test proves the LETTER never carries whatever the
    /// administrator typed, whatever that happened to be.
    /// </remarks>
    private static readonly string TheAdminsChosenPassword = NewPassword();

    private static string NewPassword()
    {
        const string alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return "T!" + System.Security.Cryptography.RandomNumberGenerator.GetString(alphabet, 20) + "9";
    }

    private sealed record Sent(AppUser User, string Email, string Link, string MadeBy);

    private static IDbContextFactory<BenDataContext> Factory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

    private static AutoMapper.IMapper Mapper()
    {
        var m = new Mock<AutoMapper.IMapper>();
        m.Setup(x => x.Map<AppUserAdminRecord>(It.IsAny<object>()))
         .Returns<object>(o => o is AppUser u
             ? new AppUserAdminRecord { Id = u.Id, DisplayName = u.DisplayName ?? "", Email = u.Email }
             : new AppUserAdminRecord { Id = Guid.Empty, DisplayName = "" });
        return m.Object;
    }

    private static (AdminAppUserController Ctrl, List<Sent> Letters, IDbContextFactory<BenDataContext> Db)
        Build(Guid actorId, bool mailerSucceeds = true)
    {
        var factory = Factory();
        var store = new Mock<IUserStore<AppUser>>();
        var users = new Mock<UserManager<AppUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        users.Setup(x => x.CreateAsync(It.IsAny<AppUser>(), It.IsAny<string>()))
             .ReturnsAsync(IdentityResult.Success)
             .Callback<AppUser, string>((u, _) => u.Id = Guid.NewGuid());
        users.Setup(x => x.GeneratePasswordResetTokenAsync(It.IsAny<AppUser>()))
             .ReturnsAsync("a-reset-token");

        var audit = new Mock<IAuditLogService>();
        audit.Setup(x => x.LogCreateAsync(It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        var letters = new List<Sent>();
        var mailer = new Mock<IConfirmationMailer>();
        mailer.Setup(m => m.TrySendAccountMadeForYouAsync(
                It.IsAny<AppUser>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
              .ReturnsAsync(mailerSucceeds)
              .Callback<AppUser, string, string, string>(
                  (u, to, link, by) => letters.Add(new Sent(u, to, link, by)));

        var site = Options.Create(new SiteIdentity { BaseUrl = "https://ishaunted.com" });

        var ctrl = new AdminAppUserController(
            factory, Mapper(), audit.Object, users.Object,
            new UserHandleService(factory), mailer.Object, site)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, actorId.ToString())], "Bearer")),
                },
            },
        };

        return (ctrl, letters, factory);
    }

    private static async Task SeedAdminAsync(IDbContextFactory<BenDataContext> db, Guid id, string name)
    {
        await using var ctx = await db.CreateDbContextAsync();
        ctx.AppUsers.Add(new AppUser { Id = id, DisplayName = name, DateCreated = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Making_somebody_an_account_tells_them_it_exists()
    {
        var actor = Guid.NewGuid();
        var (ctrl, letters, db) = Build(actor);
        await SeedAdminAsync(db, actor, "Sarah Mitchell");

        var result = await ctrl.CreateUser(
            new AdminCreateUserRequest("newcomer@example.com", TheAdminsChosenPassword,
                                       "A Newcomer", null, false, false), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);

        var letter = Assert.Single(letters);
        Assert.Equal("newcomer@example.com", letter.Email);

        // Named, not anonymous. "Somebody made you an account" is alarming in a way a name is not,
        // and the reader has to decide whether they were expecting this.
        Assert.Equal("Sarah Mitchell", letter.MadeBy);
    }

    [Fact]
    public async Task What_goes_out_is_a_way_to_choose_a_password_never_the_password()
    {
        var actor = Guid.NewGuid();
        var (ctrl, letters, db) = Build(actor);
        await SeedAdminAsync(db, actor, "Sarah Mitchell");

        await ctrl.CreateUser(
            new AdminCreateUserRequest("newcomer@example.com", TheAdminsChosenPassword,
                                       "A Newcomer", null, false, false), default);

        var letter = Assert.Single(letters);

        // The whole point: a link that lets the owner REPLACE the password, which takes the
        // account out of the hands of whoever set it up, rather than a copy of the key.
        Assert.Contains("/reset-password", letter.Link);
        Assert.DoesNotContain(TheAdminsChosenPassword, letter.Link);
    }

    [Fact]
    public async Task The_code_in_the_link_is_encoded_the_way_the_reset_endpoint_reads_it()
    {
        var actor = Guid.NewGuid();
        var (ctrl, letters, db) = Build(actor);
        await SeedAdminAsync(db, actor, "Sarah Mitchell");

        await ctrl.CreateUser(
            new AdminCreateUserRequest("newcomer@example.com", TheAdminsChosenPassword,
                                       "A Newcomer", null, false, false), default);

        var code = System.Web.HttpUtility.ParseQueryString(
            new Uri(Assert.Single(letters).Link).Query)["code"];
        Assert.NotNull(code);

        // Identity's /resetPassword base64url-DECODES the code before checking it, so a raw token
        // makes it throw FormatException and answer "invalid token". The button in this letter
        // would have failed every single time, for a reason nobody could have diagnosed from the
        // message. Decoding it here is the same step the endpoint takes.
        var decoded = System.Text.Encoding.UTF8.GetString(
            Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(code!));

        Assert.Equal("a-reset-token", decoded);
    }

    [Fact]
    public async Task A_letter_that_could_not_be_sent_does_not_undo_the_account()
    {
        var actor = Guid.NewGuid();
        var (ctrl, _, db) = Build(actor, mailerSucceeds: false);
        await SeedAdminAsync(db, actor, "Sarah Mitchell");

        // The account is made either way. A site with no mail configured must still be able to
        // add somebody; the failure is logged with the link in it so they can be reached by hand.
        var result = await ctrl.CreateUser(
            new AdminCreateUserRequest("newcomer@example.com", TheAdminsChosenPassword,
                                       "A Newcomer", null, false, false), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task An_account_with_no_address_is_not_written_to()
    {
        var actor = Guid.NewGuid();
        var (ctrl, letters, db) = Build(actor);
        await SeedAdminAsync(db, actor, "Sarah Mitchell");

        await ctrl.CreateUser(
            new AdminCreateUserRequest("", TheAdminsChosenPassword, "No Address", "no-address", false, false),
            default);

        Assert.Empty(letters);
    }
}
