using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Reviews of a tour, from the people who walked it (item 233).
/// </summary>
/// <remarks>
/// <para>Ben chose reviews as optional per tour and on by default. The rule that carries the
/// weight is who may write one: somebody accepted on a date of that tour <b>which has finished</b>,
/// because a rating from somebody who never turned up is a review of nothing.</para>
///
/// <para>And the rule about what a business may do: hide, never edit. Changing somebody's words
/// while their name stays on them is the one thing a review system must not allow.</para>
/// </remarks>
public sealed class TourReviewTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private static IDbContextFactory<BenDataContext> Db()
        => new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory, Guid OrgId, Guid TourId,
        Guid PastDateId, Guid FutureDateId, Guid GuestId, Guid StrangerId, Guid OwnerId);

    private static async Task<World> SeedAsync(bool allowReviews = true, bool guestCame = true)
    {
        var factory = Db();
        var now = DateTime.UtcNow;
        Guid orgId = Guid.NewGuid(), tourId = Guid.NewGuid(), pastId = Guid.NewGuid(),
             futureId = Guid.NewGuid(), guestId = Guid.NewGuid(), strangerId = Guid.NewGuid(),
             ownerId = Guid.NewGuid(), addressId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.AppUsers.AddRange(
            new AppUser { Id = ownerId, UserName = "o", Email = "o@t.com", DisplayName = "Olive Owner", DateCreated = now },
            new AppUser { Id = guestId, UserName = "g", Email = "g@t.com", DisplayName = "Ada Guest", Handle = "ada", DateCreated = now },
            new AppUser { Id = strangerId, UserName = "s", Email = "s@t.com", DisplayName = "Sam Stranger", DateCreated = now });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Printers Alley Walks", UrlName = "paw",
            Kind = OrganizationKind.GhostWalkingTour, DateCreated = now, CreatedByAppUserId = ownerId,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = ownerId,
            Role = OrganizationMemberRole.Owner, IsActive = true, DateCreated = now, CreatedByAppUserId = ownerId,
        });
        db.OrganizationAddresses.Add(new OrganizationAddress
        {
            Id = addressId, OrganizationId = orgId, OrganizationAddressTypeId = Guid.NewGuid(),
            StreetAddress1 = "1 Printers Alley", City = "Nashville", State = "TN", ZipCode = "37201",
            Country = "US", DateCreated = now, CreatedByAppUserId = ownerId,
        });
        db.Tours.Add(new Tour
        {
            Id = tourId, OrganizationId = orgId, Name = "Printers Alley Ghost Walk",
            UrlName = "printers-alley-ghost-walk", StartOrganizationAddressId = addressId,
            AllowReviews = allowReviews, DateCreated = now, CreatedByAppUserId = ownerId,
        });
        db.OrgCalendarEvents.AddRange(
            new OrgCalendarEvent
            {
                Id = pastId, OrganizationId = orgId, TourId = tourId, Title = "Last Saturday",
                StartDateTime = now.AddDays(-7), EndDateTime = now.AddDays(-7).AddHours(2),
                IsPublic = true, DateCreated = now, CreatedByAppUserId = ownerId,
            },
            new OrgCalendarEvent
            {
                Id = futureId, OrganizationId = orgId, TourId = tourId, Title = "This Saturday",
                StartDateTime = now.AddDays(3), EndDateTime = now.AddDays(3).AddHours(2),
                IsPublic = true, DateCreated = now, CreatedByAppUserId = ownerId,
            });

        if (guestCame)
        {
            db.OrgCalendarEventAttendees.Add(new OrgCalendarEventAttendee
            {
                Id = Guid.NewGuid(), OrgCalendarEventId = pastId, AppUserId = guestId,
                RsvpStatus = RsvpStatus.Accepted, DateCreated = now, CreatedByAppUserId = guestId,
            });
        }
        // Somebody who is coming to the NEXT one but has not walked it yet.
        db.OrgCalendarEventAttendees.Add(new OrgCalendarEventAttendee
        {
            Id = Guid.NewGuid(), OrgCalendarEventId = futureId, AppUserId = strangerId,
            RsvpStatus = RsvpStatus.Accepted, DateCreated = now, CreatedByAppUserId = strangerId,
        });

        await db.SaveChangesAsync();
        return new World(factory, orgId, tourId, pastId, futureId, guestId, strangerId, ownerId);
    }

    private static PublicTourController Public(World w, Guid? userId)
        => new(w.Factory, new Ben.Data.WebApi.Services.CmsMarkupSanitizer())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = userId is { } id
                        ? new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim(ClaimTypes.NameIdentifier, id.ToString())], "Bearer"))
                        : new ClaimsPrincipal(new ClaimsIdentity()),
                },
            },
        };

    private static async Task<TourReviewsRecord> ReviewsAsync(World w, Guid? asUser)
        => Assert.IsType<TourReviewsRecord>(
            Assert.IsType<OkObjectResult>((await Public(w, asUser).GetReviews(w.TourId, default)).Result).Value);

    // ── who may write one ────────────────────────────────────────────────────

    [Fact]
    public async Task Somebody_who_walked_it_may_review_it()
    {
        var w = await SeedAsync();
        var result = await Public(w, w.GuestId)
            .UpsertReview(w.TourId, new UpsertTourReviewRequest(5, "Genuinely unsettling."), default);

        var reviews = Assert.IsType<TourReviewsRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var review = Assert.Single(reviews.Reviews);
        Assert.Equal(5, review.Stars);
        Assert.Equal("Ada Guest", review.By);
        Assert.True(review.IsMine);
        Assert.Equal(5.0m, reviews.Average);
        Assert.Equal(1, reviews.Count);
    }

    [Fact]
    public async Task Somebody_who_has_not_walked_it_yet_is_told_when_they_may()
    {
        // Coming on Saturday is not the same as having been.
        var w = await SeedAsync();
        var reviews = await ReviewsAsync(w, w.StrangerId);

        Assert.False(reviews.MayReview);
        Assert.Contains("after the walk", reviews.WhyNot);

        var refused = await Public(w, w.StrangerId)
            .UpsertReview(w.TourId, new UpsertTourReviewRequest(1, "Rubbish."), default);
        Assert.IsType<BadRequestObjectResult>(refused.Result);
    }

    [Fact]
    public async Task A_visitor_who_is_not_signed_in_can_read_them_and_is_asked_to_sign_in()
    {
        var w = await SeedAsync();
        await Public(w, w.GuestId).UpsertReview(w.TourId, new UpsertTourReviewRequest(4, "Good fun."), default);

        var reviews = await ReviewsAsync(w, null);

        Assert.Single(reviews.Reviews);
        Assert.False(reviews.MayReview);
        Assert.Contains("Sign in", reviews.WhyNot);
    }

    [Fact]
    public async Task A_tour_with_reviews_switched_off_takes_none()
    {
        var w = await SeedAsync(allowReviews: false);
        var reviews = await ReviewsAsync(w, w.GuestId);

        Assert.False(reviews.MayReview);
        Assert.Contains("isn't taking reviews", reviews.WhyNot);
        Assert.IsType<BadRequestObjectResult>(
            (await Public(w, w.GuestId).UpsertReview(w.TourId, new UpsertTourReviewRequest(5, null), default)).Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task A_rating_outside_one_to_five_is_refused(int stars)
    {
        var w = await SeedAsync();
        var result = await Public(w, w.GuestId).UpsertReview(w.TourId, new UpsertTourReviewRequest(stars, null), default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ── one per guest, editable ──────────────────────────────────────────────

    [Fact]
    public async Task Walking_it_twice_still_leaves_one_opinion()
    {
        var w = await SeedAsync();
        await Public(w, w.GuestId).UpsertReview(w.TourId, new UpsertTourReviewRequest(3, "Fine."), default);
        var second = await Public(w, w.GuestId).UpsertReview(w.TourId, new UpsertTourReviewRequest(5, "Better the second time."), default);

        var reviews = Assert.IsType<TourReviewsRecord>(Assert.IsType<OkObjectResult>(second.Result).Value);
        var review = Assert.Single(reviews.Reviews);
        Assert.Equal(5, review.Stars);
        Assert.Equal("Better the second time.", review.Comment);
    }

    [Fact]
    public async Task A_guest_can_take_their_own_review_down()
    {
        var w = await SeedAsync();
        await Public(w, w.GuestId).UpsertReview(w.TourId, new UpsertTourReviewRequest(2, "Not for me."), default);

        Assert.IsType<NoContentResult>(await Public(w, w.GuestId).DeleteMyReview(w.TourId, default));
        Assert.Empty((await ReviewsAsync(w, w.GuestId)).Reviews);
    }

    // ── what the business may do ─────────────────────────────────────────────

    [Fact]
    public async Task A_hidden_review_is_gone_for_readers_but_not_for_the_person_who_wrote_it()
    {
        var w = await SeedAsync();
        await Public(w, w.GuestId).UpsertReview(w.TourId, new UpsertTourReviewRequest(1, "Unfair."), default);

        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var review = await db.TourReviews.SingleAsync();
            review.HiddenAtUtc = DateTime.UtcNow;
            review.HiddenByAppUserId = w.OwnerId;
            await db.SaveChangesAsync();
        }

        // A stranger sees nothing, and the average does not count it.
        var toEverybody = await ReviewsAsync(w, w.StrangerId);
        Assert.Empty(toEverybody.Reviews);
        Assert.Null(toEverybody.Average);

        // Its author still sees it, marked, rather than wondering where it went.
        var toAuthor = await ReviewsAsync(w, w.GuestId);
        var mine = Assert.Single(toAuthor.Reviews);
        Assert.True(mine.IsHidden);
        Assert.True(mine.IsMine);
    }

    [Fact]
    public async Task Rewriting_a_hidden_review_puts_it_back_for_a_fresh_look()
    {
        // New words are not the words that were judged.
        var w = await SeedAsync();
        await Public(w, w.GuestId).UpsertReview(w.TourId, new UpsertTourReviewRequest(1, "Unfair."), default);
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var review = await db.TourReviews.SingleAsync();
            review.HiddenAtUtc = DateTime.UtcNow;
            review.HiddenByAppUserId = w.OwnerId;
            await db.SaveChangesAsync();
        }

        await Public(w, w.GuestId).UpsertReview(w.TourId, new UpsertTourReviewRequest(4, "On reflection."), default);

        var reviews = await ReviewsAsync(w, w.StrangerId);
        var review2 = Assert.Single(reviews.Reviews);
        Assert.False(review2.IsHidden);
        Assert.Equal(4, review2.Stars);
    }

    [Fact]
    public async Task The_average_is_rounded_once_on_the_server()
    {
        // So the page, the card and the phone cannot each round the same numbers differently.
        var w = await SeedAsync();
        await Public(w, w.GuestId).UpsertReview(w.TourId, new UpsertTourReviewRequest(4, null), default);
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.OrgCalendarEventAttendees.Add(new OrgCalendarEventAttendee
            {
                Id = Guid.NewGuid(), OrgCalendarEventId = w.PastDateId, AppUserId = w.StrangerId,
                RsvpStatus = RsvpStatus.Accepted, DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.StrangerId,
            });
            await db.SaveChangesAsync();
        }
        await Public(w, w.StrangerId).UpsertReview(w.TourId, new UpsertTourReviewRequest(5, null), default);

        var reviews = await ReviewsAsync(w, null);
        Assert.Equal(4.5m, reviews.Average);
        Assert.Equal(2, reviews.Count);
    }
}
