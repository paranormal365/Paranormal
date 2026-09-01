using Ben.Data.Common;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Asking for the confirmation email again, from the sign-in page.
/// </summary>
/// <remarks>
/// <para>The endpoint's whole security story is that <b>its answer never varies</b>. It is
/// anonymous and takes an email address, so a reply that differed for a registered address would
/// let anybody discover who has an account here, one guess at a time.</para>
///
/// <para>That makes it exactly the kind of rule that erodes quietly: the natural instinct when
/// editing this later is to be helpful — "we couldn't find that address", "you're already
/// confirmed" — and each of those is the leak. These tests exist to make that edit fail.</para>
/// </remarks>
public sealed class ResendConfirmationTests
{
    private const string Neutral = "If that address has an unconfirmed account";

    private static Mock<UserManager<AppUser>> UserManagerMock()
    {
        var store = new Mock<IUserStore<AppUser>>();
        return new Mock<UserManager<AppUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private static AccountRegistrationController Build(
        Mock<UserManager<AppUser>> um, Mock<IConfirmationMailer> mailer)
    {
        var factory = new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        return new AccountRegistrationController(
            um.Object,
            new UserHandleService(factory),
            new Mock<IEmailSender<AppUser>>().Object,
            mailer.Object,
            new Mock<Ben.Data.Common.Interfaces.IEmailService>().Object,
            Options.Create(new SiteIdentity { BaseUrl = "https://example.test" }),
            new ConfigurationBuilder().Build(),
            NullLogger<AccountRegistrationController>.Instance);
    }

    private static string MessageOf(ActionResult<ResendConfirmationResponse> result)
        => Assert.IsType<ResendConfirmationResponse>(
               Assert.IsType<OkObjectResult>(result.Result).Value).Message;

    [Fact]
    public async Task An_unknown_address_gets_the_neutral_answer_and_sends_nothing()
    {
        var um = UserManagerMock();
        um.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((AppUser?)null);
        var mailer = new Mock<IConfirmationMailer>();

        var result = await Build(um, mailer)
            .ResendConfirmation(new ResendConfirmationRequest("nobody@example.test"), default);

        Assert.Contains(Neutral, MessageOf(result));
        mailer.Verify(m => m.TrySendConfirmationAsync(
            It.IsAny<AppUser>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task An_already_confirmed_account_gets_the_same_answer_and_no_new_mail()
    {
        // The leak this prevents: telling somebody "that address is already confirmed" confirms
        // the address is registered.
        var um = UserManagerMock();
        um.Setup(m => m.FindByEmailAsync(It.IsAny<string>()))
          .ReturnsAsync(new AppUser { Email = "taken@example.test", EmailConfirmed = true });
        var mailer = new Mock<IConfirmationMailer>();

        var result = await Build(um, mailer)
            .ResendConfirmation(new ResendConfirmationRequest("taken@example.test"), default);

        Assert.Contains(Neutral, MessageOf(result));
        mailer.Verify(m => m.TrySendConfirmationAsync(
            It.IsAny<AppUser>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task A_recent_request_is_throttled_and_still_answers_the_same()
    {
        var um = UserManagerMock();
        um.Setup(m => m.FindByEmailAsync(It.IsAny<string>()))
          .ReturnsAsync(new AppUser
          {
              Email = "waiting@example.test",
              EmailConfirmed = false,
              DateConfirmationSent = DateTime.UtcNow.AddSeconds(-5),
          });
        var mailer = new Mock<IConfirmationMailer>();

        var result = await Build(um, mailer)
            .ResendConfirmation(new ResendConfirmationRequest("waiting@example.test"), default);

        Assert.Contains(Neutral, MessageOf(result));
        mailer.Verify(m => m.TrySendConfirmationAsync(
            It.IsAny<AppUser>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task A_stale_request_actually_sends()
    {
        // The test that stops the throttle from becoming a permanent block — the failure mode
        // where every answer looks right and no mail ever goes out again.
        var um = UserManagerMock();
        um.Setup(m => m.FindByEmailAsync(It.IsAny<string>()))
          .ReturnsAsync(new AppUser
          {
              Id = Guid.NewGuid(),
              Email = "stuck@example.test",
              EmailConfirmed = false,
              DateConfirmationSent = DateTime.UtcNow.AddHours(-2),
          });
        um.Setup(m => m.GenerateEmailConfirmationTokenAsync(It.IsAny<AppUser>()))
          .ReturnsAsync("token");
        um.Setup(m => m.UpdateAsync(It.IsAny<AppUser>())).ReturnsAsync(IdentityResult.Success);

        var mailer = new Mock<IConfirmationMailer>();
        mailer.Setup(m => m.TrySendConfirmationAsync(
                  It.IsAny<AppUser>(), It.IsAny<string>(), It.IsAny<string>()))
              .ReturnsAsync(true);

        var result = await Build(um, mailer)
            .ResendConfirmation(new ResendConfirmationRequest("stuck@example.test"), default);

        Assert.Contains(Neutral, MessageOf(result));
        mailer.Verify(m => m.TrySendConfirmationAsync(
            It.IsAny<AppUser>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task A_never_contacted_account_sends_because_null_is_not_recent()
    {
        // DateConfirmationSent is null exactly when nothing ever went out — the state Ben's own
        // sign-up was left in. Treating null as "recent" would deny mail to the only people who
        // truly need it.
        var um = UserManagerMock();
        um.Setup(m => m.FindByEmailAsync(It.IsAny<string>()))
          .ReturnsAsync(new AppUser
          {
              Id = Guid.NewGuid(), Email = "never@example.test",
              EmailConfirmed = false, DateConfirmationSent = null,
          });
        um.Setup(m => m.GenerateEmailConfirmationTokenAsync(It.IsAny<AppUser>()))
          .ReturnsAsync("token");
        um.Setup(m => m.UpdateAsync(It.IsAny<AppUser>())).ReturnsAsync(IdentityResult.Success);

        var mailer = new Mock<IConfirmationMailer>();
        mailer.Setup(m => m.TrySendConfirmationAsync(
                  It.IsAny<AppUser>(), It.IsAny<string>(), It.IsAny<string>()))
              .ReturnsAsync(true);

        var result = await Build(um, mailer)
            .ResendConfirmation(new ResendConfirmationRequest("never@example.test"), default);

        Assert.Contains(Neutral, MessageOf(result));
        mailer.Verify(m => m.TrySendConfirmationAsync(
            It.IsAny<AppUser>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    public async Task Rubbish_input_gets_the_neutral_answer_too(string? email)
    {
        // Not a 400: a validation error for a malformed address and a neutral 200 for a
        // well-formed unknown one is itself a distinction worth probing.
        var um = UserManagerMock();
        var mailer = new Mock<IConfirmationMailer>();

        var result = await Build(um, mailer)
            .ResendConfirmation(new ResendConfirmationRequest(email), default);

        Assert.Contains(Neutral, MessageOf(result));
    }

}
