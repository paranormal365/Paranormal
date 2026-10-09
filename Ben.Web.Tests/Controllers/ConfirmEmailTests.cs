using Ben.Data.Common;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Text;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Confirming an email address that is already confirmed.
/// </summary>
public sealed class ConfirmEmailTests
{
    private const string RealToken = "the-real-token";

    private static Mock<UserManager<AppUser>> UserManagerMock(AppUser user)
    {
        var store = new Mock<IUserStore<AppUser>>();
        var um = new Mock<UserManager<AppUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        um.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        um.Setup(m => m.VerifyUserTokenAsync(user, It.IsAny<string>(),
                UserManager<AppUser>.ConfirmEmailTokenPurpose, It.IsAny<string>()))
          .ReturnsAsync((AppUser _, string _, string _, string token) => token == RealToken);
        return um;
    }

    private static AccountRegistrationController Build(Mock<UserManager<AppUser>> um)
    {
        var factory = new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        return new AccountRegistrationController(
            um.Object,
            new UserHandleService(factory),
            new AccountCreationService(
                um.Object,
                new UserHandleService(factory),
                new Mock<IConfirmationMailer>().Object,
                new Mock<Ben.Data.Common.Interfaces.IEmailService>().Object,
                Options.Create(new SiteIdentity { BaseUrl = "https://example.test" }),
                new ConfigurationBuilder().Build(),
                NullLogger<AccountCreationService>.Instance),
            factory);
    }

    private static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private static async Task<ConfirmEmailResponse> ConfirmAsync(AppUser user, string code)
    {
        var result = await Build(UserManagerMock(user))
            .ConfirmEmail(new ConfirmEmailRequest(user.Id, code), default);
        return Assert.IsType<ConfirmEmailResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    private static AppUser Confirmed() => new()
    {
        Id = Guid.NewGuid(), Email = "done@example.test", EmailConfirmed = true, Handle = "nightwatch",
    };

    /// <summary>
    /// Somebody without the real link learns nothing about an already-confirmed account. Asked with any
    /// code at all, this answered with the person's @name and what they were waiting on, so anybody who
    /// knew a person's id could learn which groups they had asked to investigate their home (site
    /// audit, 10/09/2026).
    /// </summary>
    [Fact]
    public async Task An_already_confirmed_account_asked_with_a_wrong_code_gives_no_name_or_waiting_request()
    {
        var answer = await ConfirmAsync(Confirmed(), Encode("a-guess"));

        Assert.True(answer.Succeeded);
        Assert.Null(answer.Handle);
        Assert.Null(answer.Waiting);
    }

    /// <summary>
    /// The person holding the real link still sees their @name (site audit, 10/09/2026).
    /// </summary>
    [Fact]
    public async Task An_already_confirmed_account_asked_with_the_real_link_gives_its_name()
    {
        var answer = await ConfirmAsync(Confirmed(), Encode(RealToken));

        Assert.True(answer.Succeeded);
        Assert.Equal("nightwatch", answer.Handle);
    }
}
