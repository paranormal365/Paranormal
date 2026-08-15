using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Support;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for the public contact form: what it stores, what it refuses, and what it never leaks.
/// </summary>
public class PublicSupportTicketControllerTests
{
    private static SupportFormGuard BuildGuard()
        => new(DataProtectionProvider.Create(nameof(PublicSupportTicketControllerTests)),
               new ConfigurationBuilder().Build());

    private static PublicSupportTicketController BuildController(
        IDbContextFactory<BenDataContext> factory,
        SupportFormGuard guard,
        string? remoteIp = "203.0.113.7")
    {
        var http = new DefaultHttpContext();
        if (remoteIp is not null) http.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);

        return new PublicSupportTicketController(
            factory, guard, NullLogger<PublicSupportTicketController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    /// <summary>A token that passes: minted far enough in the past to clear the fill-time floor.</summary>
    private static string GoodToken(SupportFormGuard guard)
        => guard.IssueFormToken(DateTimeOffset.UtcNow - TimeSpan.FromSeconds(30));

    private static SubmitSupportTicketRequest Request(
        SupportFormGuard guard,
        string email = "visitor@example.com",
        string? honeypot = null,
        string? formToken = null)
        => new(
            FromName: "A Visitor",
            FromEmail: email,
            Topic: SupportTicketTopic.WebsiteHelp,
            Subject: "I can't sign in",
            Body: "The sign-in page says my password is wrong.",
            FormToken: formToken ?? GoodToken(guard),
            Website: honeypot);

    // ── The happy path ────────────────────────────────────────────────────────

    [Fact]
    public async Task Valid_submission_is_stored_and_returns_a_way_back_to_it()
    {
        var factory = TestDbFactory.Create();
        var guard = BuildGuard();

        var result = await BuildController(factory, guard).Submit(Request(guard), default);

        var response = Assert.IsType<SubmitSupportTicketResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.StartsWith("SUP-", response.Reference);
        Assert.NotEqual(Guid.Empty, response.AccessToken);

        await using var db = await factory.CreateDbContextAsync();
        var ticket = await db.SupportTickets.SingleAsync();
        Assert.Equal(SupportTicketStatus.New, ticket.Status);
        Assert.Equal("I can't sign in", ticket.Subject);
        Assert.Equal(response.AccessToken, ticket.AccessToken);
        Assert.Null(ticket.AppUserId);          // anonymous sender
        Assert.NotNull(ticket.SourceIpHash);
    }

    [Fact]
    public async Task Email_is_stored_lower_cased_so_capitals_cannot_dodge_the_rate_limit()
    {
        var factory = TestDbFactory.Create();
        var guard = BuildGuard();

        await BuildController(factory, guard).Submit(Request(guard, email: "Visitor@Example.COM"), default);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal("visitor@example.com", (await db.SupportTickets.SingleAsync()).FromEmail);
    }

    // ── Spam checks ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Tripped_honeypot_looks_like_success_and_stores_nothing()
    {
        var factory = TestDbFactory.Create();
        var guard = BuildGuard();

        var result = await BuildController(factory, guard)
            .Submit(Request(guard, honeypot: "http://spam.example"), default);

        // Looks accepted — telling a bot which check caught it is free tuning information.
        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.SupportTickets.ToListAsync());
    }

    [Fact]
    public async Task Form_submitted_faster_than_a_person_could_type_is_refused()
    {
        var factory = TestDbFactory.Create();
        var guard = BuildGuard();

        // Minted now, submitted now.
        var token = guard.IssueFormToken(DateTimeOffset.UtcNow);
        var result = await BuildController(factory, guard)
            .Submit(Request(guard, formToken: token), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.SupportTickets.ToListAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-real-token")]
    public async Task Missing_or_tampered_form_token_is_refused(string? token)
    {
        var factory = TestDbFactory.Create();
        var guard = BuildGuard();

        // Built directly rather than through Request(), whose `?? GoodToken(...)` fallback would
        // quietly hand the null case a perfectly good token and make this test prove nothing.
        var result = await BuildController(factory, guard).Submit(
            new SubmitSupportTicketRequest("A Visitor", "visitor@example.com",
                SupportTicketTopic.WebsiteHelp, "Subject", "Body", token, null), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void Expired_token_is_reported_separately_from_a_bad_one()
    {
        var guard = BuildGuard();
        var now = DateTimeOffset.UtcNow;

        // A real person can hit this by leaving the tab open, so it earns an honest message.
        var old = guard.IssueFormToken(now - SupportFormGuard.FormTokenLifetime - TimeSpan.FromMinutes(1));

        Assert.Equal(FormTokenResult.Expired, guard.ValidateFormToken(old, now));
        Assert.Equal(FormTokenResult.Invalid, guard.ValidateFormToken("rubbish", now));
    }

    [Fact]
    public void A_token_from_another_purpose_does_not_validate()
    {
        var provider = DataProtectionProvider.Create(nameof(PublicSupportTicketControllerTests));
        var foreign = provider.CreateProtector("SomeOtherPurpose")
            .Protect(DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds().ToString());

        Assert.Equal(FormTokenResult.Invalid, BuildGuard().ValidateFormToken(foreign, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Hashing_the_same_address_twice_gives_the_same_value()
    {
        // The rate limit compares stored hashes, so a non-deterministic hash would silently make
        // every limit unenforceable while still looking like it worked.
        var guard = BuildGuard();

        Assert.Equal(guard.HashIp("203.0.113.7"), guard.HashIp("203.0.113.7"));
        Assert.NotEqual(guard.HashIp("203.0.113.7"), guard.HashIp("203.0.113.8"));
        Assert.Null(guard.HashIp(null));
    }

    [Fact]
    public void Hashing_does_not_keep_the_address_itself()
    {
        Assert.DoesNotContain("203.0.113.7", BuildGuard().HashIp("203.0.113.7"));
    }

    // ── Rate limits ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Too_many_from_one_address_in_a_day_is_refused()
    {
        var factory = TestDbFactory.Create();
        var guard = BuildGuard();

        // Vary the IP so this test exercises the email limit and not the IP one.
        for (var i = 0; i < SupportFormGuard.MaxPerEmailPerDay; i++)
        {
            var ctrl = BuildController(factory, guard, remoteIp: $"198.51.100.{i + 1}");
            Assert.IsType<OkObjectResult>((await ctrl.Submit(Request(guard), default)).Result);
        }

        var blocked = await BuildController(factory, guard, remoteIp: "198.51.100.200")
            .Submit(Request(guard), default);

        Assert.Equal(429, Assert.IsType<ObjectResult>(blocked.Result).StatusCode);
    }

    [Fact]
    public async Task Too_many_from_one_address_in_an_hour_is_refused()
    {
        var factory = TestDbFactory.Create();
        var guard = BuildGuard();

        // Vary the email so this exercises the IP limit and not the email one.
        for (var i = 0; i < SupportFormGuard.MaxPerIpPerHour; i++)
        {
            var ctrl = BuildController(factory, guard);
            Assert.IsType<OkObjectResult>(
                (await ctrl.Submit(Request(guard, email: $"person{i}@example.com"), default)).Result);
        }

        var blocked = await BuildController(factory, guard)
            .Submit(Request(guard, email: "someone-else@example.com"), default);

        Assert.Equal(429, Assert.IsType<ObjectResult>(blocked.Result).StatusCode);
    }

    // ── Validation ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "s", "b")]
    [InlineData("n", "", "b")]
    [InlineData("n", "s", "")]
    public async Task Blank_required_fields_are_refused(string name, string subject, string body)
    {
        var factory = TestDbFactory.Create();
        var guard = BuildGuard();

        var result = await BuildController(factory, guard).Submit(
            new SubmitSupportTicketRequest(name, "a@b.com", SupportTicketTopic.Other,
                subject, body, GoodToken(guard), null), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Theory]
    [InlineData("no-at-sign")]
    [InlineData("two@at@signs.com")]
    [InlineData("has space@example.com")]
    [InlineData("nodot@localhost")]
    [InlineData("@example.com")]
    public async Task Implausible_email_is_refused(string email)
    {
        var factory = TestDbFactory.Create();
        var guard = BuildGuard();

        var result = await BuildController(factory, guard).Submit(Request(guard, email: email), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Undefined_topic_is_refused()
    {
        var factory = TestDbFactory.Create();
        var guard = BuildGuard();

        var result = await BuildController(factory, guard).Submit(
            new SubmitSupportTicketRequest("n", "a@b.com", (SupportTicketTopic)99,
                "s", "b", GoodToken(guard), null), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ── Notification ──────────────────────────────────────────────────────────

    [Fact]
    public async Task App_administrators_are_told_about_a_new_ticket()
    {
        var factory = TestDbFactory.Create();
        var guard = BuildGuard();

        Guid adminId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var role = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = RoleNames.SuperAdmin };
            db.Roles.Add(role);
            adminId = Guid.NewGuid();
            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = adminId, RoleId = role.Id });
            await db.SaveChangesAsync();
        }

        await BuildController(factory, guard).Submit(Request(guard), default);

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Contains(adminId, await db.UserMessageTos.Select(t => t.ToAppUserId).ToListAsync());
            var message = await db.UserMessages.SingleAsync();
            Assert.Contains("I can't sign in", message.MessageSubject);
        }
    }

    [Fact]
    public async Task A_submission_still_succeeds_when_there_are_no_administrators_to_tell()
    {
        var factory = TestDbFactory.Create();
        var guard = BuildGuard();

        var result = await BuildController(factory, guard).Submit(Request(guard), default);

        Assert.IsType<OkObjectResult>(result.Result);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Single(await db.SupportTickets.ToListAsync());
    }

    // ── The sender's view ─────────────────────────────────────────────────────

    [Fact]
    public async Task Sender_sees_staff_replies_but_never_internal_notes()
    {
        var factory = TestDbFactory.Create();
        var guard = BuildGuard();
        var submitted = await BuildController(factory, guard).Submit(Request(guard), default);
        var token = ((SubmitSupportTicketResponse)((OkObjectResult)submitted.Result!).Value!).AccessToken;

        await using (var db = await factory.CreateDbContextAsync())
        {
            var ticketId = (await db.SupportTickets.SingleAsync()).Id;
            db.SupportTicketReplies.AddRange(
                new SupportTicketReply
                {
                    Id = Guid.NewGuid(), SupportTicketId = ticketId,
                    Body = "Try resetting your password.", IsFromStaff = true,
                    IsInternalNote = false, DateCreated = DateTime.UtcNow,
                },
                new SupportTicketReply
                {
                    Id = Guid.NewGuid(), SupportTicketId = ticketId,
                    Body = "Third time this week from this person.", IsFromStaff = true,
                    IsInternalNote = true, DateCreated = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
        }

        var result = await BuildController(factory, guard).GetByToken(token, default);

        var record = Assert.IsType<SupportTicketPublicRecord>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        var only = Assert.Single(record.Replies);
        Assert.Equal("Try resetting your password.", only.Body);
        Assert.DoesNotContain(record.Replies, r => r.Body.Contains("Third time"));
    }

    [Fact]
    public async Task An_unknown_token_reveals_nothing()
    {
        var factory = TestDbFactory.Create();
        var guard = BuildGuard();
        await BuildController(factory, guard).Submit(Request(guard), default);

        var result = await BuildController(factory, guard).GetByToken(Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Sender_reply_reopens_the_ticket_and_can_never_be_an_internal_note()
    {
        var factory = TestDbFactory.Create();
        var guard = BuildGuard();
        var submitted = await BuildController(factory, guard).Submit(Request(guard), default);
        var token = ((SubmitSupportTicketResponse)((OkObjectResult)submitted.Result!).Value!).AccessToken;

        await using (var db = await factory.CreateDbContextAsync())
        {
            var t = await db.SupportTickets.SingleAsync();
            t.Status = SupportTicketStatus.Answered;
            await db.SaveChangesAsync();
        }

        // Asks for an internal note; must not get one.
        var result = await BuildController(factory, guard)
            .ReplyByToken(token, new AddSupportTicketReplyRequest("That didn't work.", IsInternalNote: true), default);

        Assert.IsType<NoContentResult>(result);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var reply = await db.SupportTicketReplies.SingleAsync();
            Assert.False(reply.IsInternalNote);
            Assert.False(reply.IsFromStaff);
            Assert.Equal(SupportTicketStatus.Open, (await db.SupportTickets.SingleAsync()).Status);
        }
    }

    [Fact]
    public async Task Cannot_reply_to_a_closed_ticket()
    {
        var factory = TestDbFactory.Create();
        var guard = BuildGuard();
        var submitted = await BuildController(factory, guard).Submit(Request(guard), default);
        var token = ((SubmitSupportTicketResponse)((OkObjectResult)submitted.Result!).Value!).AccessToken;

        await using (var db = await factory.CreateDbContextAsync())
        {
            (await db.SupportTickets.SingleAsync()).Status = SupportTicketStatus.Closed;
            await db.SaveChangesAsync();
        }

        var result = await BuildController(factory, guard)
            .ReplyByToken(token, new AddSupportTicketReplyRequest("Hello?", false), default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void The_sender_facing_shape_carries_nothing_staff_only()
    {
        // A separate record from the admin one, so a field added for staff cannot leak here by
        // being forgotten. Asserted structurally rather than by reading the mapping code.
        var names = typeof(SupportTicketPublicRecord).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain("SourceIpHash", names);
        Assert.DoesNotContain("AssignedToAppUserId", names);
        Assert.DoesNotContain("FromEmail", names);
        Assert.DoesNotContain("AppUserId", names);
    }
}
