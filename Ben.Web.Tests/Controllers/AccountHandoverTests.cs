using System.Text;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Taking over an account somebody else made for you.
/// </summary>
/// <remarks>
/// <para><b>Why this endpoint had to exist.</b> Identity's own <c>/resetPassword</c> refuses an
/// address nobody has confirmed and answers "invalid token" when it does — a deliberate refusal to
/// say who has an account here. That is right for a forgotten password and fatal for this letter,
/// because an account made FOR somebody who has never been here is unconfirmed by definition. The
/// one letter that has to work was the one that never could, and it blamed the code.</para>
///
/// <para>Found by sending a real one and clicking it, not by any test.</para>
/// </remarks>
public sealed class AccountHandoverTests
{
    private const string Code = "the-reset-token";

    /// <summary>A password per run — see the note in <c>AccountMadeForYouTests</c>.</summary>
    private static string NewPassword()
    {
        const string alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return "T!" + System.Security.Cryptography.RandomNumberGenerator.GetString(alphabet, 20) + "9";
    }

    private static string Encoded => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(Code));

    private static (PublicAccountHandoverController Ctrl, Mock<UserManager<AppUser>> Users, AppUser User)
        Build(bool confirmed = false, IdentityResult? resetResult = null)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(), Email = "newcomer@example.com", EmailConfirmed = confirmed,
        };

        var store = new Mock<IUserStore<AppUser>>();
        var users = new Mock<UserManager<AppUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        users.Setup(u => u.FindByEmailAsync("newcomer@example.com")).ReturnsAsync(user);
        users.Setup(u => u.FindByEmailAsync(It.Is<string>(e => e != "newcomer@example.com")))
             .ReturnsAsync((AppUser?)null);
        users.Setup(u => u.ResetPasswordAsync(It.IsAny<AppUser>(), Code, It.IsAny<string>()))
             .ReturnsAsync(resetResult ?? IdentityResult.Success);
        users.Setup(u => u.ResetPasswordAsync(It.IsAny<AppUser>(), It.Is<string>(t => t != Code), It.IsAny<string>()))
             .ReturnsAsync(IdentityResult.Failed(new IdentityError
             { Code = "InvalidToken", Description = "Invalid token." }));
        users.Setup(u => u.UpdateAsync(It.IsAny<AppUser>())).ReturnsAsync(IdentityResult.Success);

        var ctrl = new PublicAccountHandoverController(
            users.Object, NullLogger<PublicAccountHandoverController>.Instance);

        return (ctrl, users, user);
    }

    [Fact]
    public async Task An_unconfirmed_address_can_still_take_its_account_over()
    {
        var (ctrl, _, user) = Build(confirmed: false);

        var result = await ctrl.TakeOver(
            new AccountHandoverRequest("newcomer@example.com", Encoded, NewPassword()), default);

        // The whole reason this endpoint exists. Identity's reset would have refused here.
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task Taking_it_over_confirms_the_address_because_they_proved_it()
    {
        var (ctrl, users, user) = Build(confirmed: false);

        await ctrl.TakeOver(
            new AccountHandoverRequest("newcomer@example.com", Encoded, NewPassword()), default);

        // The code went to that address and nowhere else, so holding it IS the proof. Recording
        // something just demonstrated, not something an administrator asserted.
        Assert.True(user.EmailConfirmed);
        users.Verify(u => u.UpdateAsync(user), Times.Once);
    }

    [Fact]
    public async Task A_failed_reset_never_confirms_the_address()
    {
        var (ctrl, users, user) = Build(confirmed: false);

        var result = await ctrl.TakeOver(
            new AccountHandoverRequest("newcomer@example.com",
                WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("a-stale-token")),
                NewPassword()), default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.False(user.EmailConfirmed);
        users.Verify(u => u.UpdateAsync(It.IsAny<AppUser>()), Times.Never);
    }

    [Fact]
    public async Task An_unknown_address_and_a_bad_code_are_refused_in_the_same_words()
    {
        var (ctrl, _, _) = Build();

        var unknown = await ctrl.TakeOver(
            new AccountHandoverRequest("nobody@example.com", Encoded, NewPassword()), default);
        var badCode = await ctrl.TakeOver(
            new AccountHandoverRequest("newcomer@example.com",
                WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("wrong")), NewPassword()),
            default);

        // No oracle. Whether an address is registered here is not a thing this may leak, which is
        // the same rule the endpoint it sits beside keeps.
        Assert.Equal(
            Assert.IsType<BadRequestObjectResult>(unknown).Value,
            Assert.IsType<BadRequestObjectResult>(badCode).Value);
    }

    [Fact]
    public async Task A_mangled_code_is_refused_rather_than_thrown()
    {
        var (ctrl, _, _) = Build();

        // A mail client that wrapped the link mangles the code rather than losing it, and a
        // FormatException escaping here would be a 500 on a page anybody can reach.
        var result = await ctrl.TakeOver(
            new AccountHandoverRequest("newcomer@example.com", "not!base64url!", NewPassword()),
            default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task The_password_policy_is_quoted_because_the_reader_can_act_on_it()
    {
        var (ctrl, _, _) = Build(resetResult: IdentityResult.Failed(
            new IdentityError { Code = "PasswordTooShort", Description = "Passwords must be at least 8 characters." }));

        var result = await ctrl.TakeOver(
            new AccountHandoverRequest("newcomer@example.com", Encoded, "short"), default);

        // Saying this reveals nothing: the code had to be right to get this far.
        Assert.Contains("at least 8 characters",
            Assert.IsType<string>(Assert.IsType<BadRequestObjectResult>(result).Value));
    }
}
