using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Reflection;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Redeeming an email-validation link.
/// </summary>
/// <remarks>
/// The whole point of this endpoint is that it works for someone who is not signed in — the link
/// arrives in a mailbox, which is often read on another device entirely. That makes the token the
/// only credential, so the rules around single use and expiry are the security, and they are what
/// these tests hold down.
/// </remarks>
public class PublicEmailValidationControllerTests
{
    private static PublicEmailValidationController Build(IDbContextFactory<BenDataContext> factory)
        => new(factory, new Mock<IAuditLogService>().Object)
        {
            // No user at all — the caller is anonymous by design.
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private static async Task<(IDbContextFactory<BenDataContext> Factory, Guid RowId)> SeedAsync(
        string token, DateTime? sentAt, string address = "person@example.test")
    {
        var factory = TestDbFactory.Create();
        var rowId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.UserEmails.Add(new UserEmail
        {
            Id = rowId,
            AppUserId = Guid.NewGuid(),
            UserEmailTypeId = Guid.NewGuid(),
            EmailAddress = address,
            ValidationToken = token,
            DateValidationSent = sentAt,
            IsValidated = false,
            DateCreated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        return (factory, rowId);
    }

    [Fact]
    public void The_endpoint_is_anonymous()
    {
        // A stray [Authorize] here would break the flow for exactly the people it exists for, and
        // would do so silently — the link would simply bounce to the login page.
        var type = typeof(PublicEmailValidationController);
        Assert.NotNull(type.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(type.GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public async Task Info_masks_the_address()
    {
        var (factory, _) = await SeedAsync("tok-info", DateTime.UtcNow, "benjamin@example.test");

        var info = Assert.IsType<EmailValidationInfoRecord>(
            Assert.IsType<OkObjectResult>((await Build(factory).GetInfo("tok-info", default)).Result).Value);

        // Enough to recognise your own address, not enough to harvest somebody else's from a
        // guessed link.
        Assert.StartsWith("be", info.MaskedEmail);
        Assert.EndsWith("@example.test", info.MaskedEmail);
        Assert.DoesNotContain("benjamin", info.MaskedEmail);
        Assert.False(info.IsExpired);
    }

    [Fact]
    public async Task An_unknown_token_is_not_found()
    {
        var (factory, _) = await SeedAsync("tok-real", DateTime.UtcNow);

        Assert.IsType<NotFoundResult>((await Build(factory).GetInfo("tok-wrong", default)).Result);
        Assert.IsType<NotFoundResult>(await Build(factory).Confirm("tok-wrong", default));
    }

    [Fact]
    public async Task Confirming_validates_the_address_and_clears_the_token()
    {
        var (factory, rowId) = await SeedAsync("tok-good", DateTime.UtcNow);

        var result = await Build(factory).Confirm("tok-good", default);

        Assert.IsType<NoContentResult>(result);

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.UserEmails.FirstAsync(e => e.Id == rowId);
        Assert.True(row.IsValidated);
        Assert.NotNull(row.DateValidated);
        Assert.Null(row.ValidationToken);
    }

    [Fact]
    public async Task A_token_cannot_be_redeemed_twice()
    {
        var (factory, _) = await SeedAsync("tok-once", DateTime.UtcNow);

        Assert.IsType<NoContentResult>(await Build(factory).Confirm("tok-once", default));

        // Cleared rather than flagged used, so the replay is indistinguishable from a bad link.
        Assert.IsType<NotFoundResult>(await Build(factory).Confirm("tok-once", default));
    }

    [Fact]
    public async Task An_expired_link_is_gone_not_confirmed()
    {
        var (factory, rowId) = await SeedAsync("tok-old", DateTime.UtcNow.AddDays(-8));

        var info = Assert.IsType<EmailValidationInfoRecord>(
            Assert.IsType<OkObjectResult>((await Build(factory).GetInfo("tok-old", default)).Result).Value);
        Assert.True(info.IsExpired);

        var result = Assert.IsType<ObjectResult>(await Build(factory).Confirm("tok-old", default));
        Assert.Equal(StatusCodes.Status410Gone, result.StatusCode);

        await using var db = await factory.CreateDbContextAsync();
        Assert.False((await db.UserEmails.FirstAsync(e => e.Id == rowId)).IsValidated);
    }

    [Fact]
    public async Task A_link_just_inside_the_window_still_works()
    {
        // Guards the boundary from the other side: an off-by-one in the expiry comparison that
        // rejected everything would still pass the expired-link test above.
        var (factory, _) = await SeedAsync("tok-fresh", DateTime.UtcNow.AddDays(-6));

        Assert.IsType<NoContentResult>(await Build(factory).Confirm("tok-fresh", default));
    }

    [Fact]
    public async Task A_row_that_never_had_a_link_issued_cannot_be_confirmed()
    {
        var (factory, _) = await SeedAsync("tok-nosend", sentAt: null);

        var result = Assert.IsType<ObjectResult>(await Build(factory).Confirm("tok-nosend", default));
        Assert.Equal(StatusCodes.Status410Gone, result.StatusCode);
    }
}
