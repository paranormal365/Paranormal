using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The defects a fresh read of item 233 turned up, each pinned where it bit.
/// </summary>
/// <remarks>
/// Written after the feature was built and reviewed rather than alongside it, which is the honest
/// place for them: every one of these passed a build, a suite and a walk through the site first.
/// </remarks>
public sealed class TourAuditFixTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private static IDbContextFactory<BenDataContext> Db()
        => new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeGateway : IStripeGateway
    {
        public readonly List<StripeRenewalCharge> Charges = [];
        public bool IsConfigured => true;
        public Task<StripeCheckoutHandle> CreateCheckoutSessionAsync(StripeCheckoutSpec spec, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<StripeChargeOutcome> ChargeSavedCardAsync(StripeRenewalCharge charge, CancellationToken ct)
        {
            lock (Charges) Charges.Add(charge);
            return Task.FromResult(new StripeChargeOutcome(true, $"pi_{Guid.NewGuid():N}", null));
        }
        public StripeCompletedCheckout? ParseCompletedCheckout(string payload, string signatureHeader)
            => throw new NotSupportedException();
    }

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory, Guid OrgId, Guid OtherOrgId,
        Guid UserId, Guid AddressId, Guid TourId, Guid OtherTourId);

    private static async Task<World> SeedAsync(int liveTours = 1, int paidTours = 1, bool subscription = true)
    {
        var factory = Db();
        var now = DateTime.UtcNow;
        Guid orgId = Guid.NewGuid(), otherOrgId = Guid.NewGuid(), userId = Guid.NewGuid(),
             addressId = Guid.NewGuid(), tourId = Guid.NewGuid(), otherTourId = Guid.NewGuid(),
             tierId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser { Id = userId, UserName = "o", Email = "o@t.com", DisplayName = "Owner", DateCreated = now });
        db.Organizations.AddRange(
            new Organization { Id = orgId, Name = "Mine", UrlName = "mine", Kind = OrganizationKind.GhostWalkingTour, DateCreated = now, CreatedByAppUserId = userId },
            new Organization { Id = otherOrgId, Name = "Theirs", UrlName = "theirs", Kind = OrganizationKind.GhostWalkingTour, DateCreated = now, CreatedByAppUserId = userId });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = OrganizationMemberRole.Owner, IsActive = true, DateCreated = now, CreatedByAppUserId = userId,
        });
        db.OrganizationAddresses.Add(new OrganizationAddress
        {
            Id = addressId, OrganizationId = orgId, OrganizationAddressTypeId = Guid.NewGuid(),
            StreetAddress1 = "1 Printers Alley", City = "Nashville", State = "TN", ZipCode = "37201",
            Country = "US", DateCreated = now, CreatedByAppUserId = userId,
        });
        db.Tours.Add(new Tour
        {
            Id = tourId, OrganizationId = orgId, Name = "Mine", UrlName = "mine",
            StartOrganizationAddressId = addressId, DateCreated = now, CreatedByAppUserId = userId,
        });
        for (var i = 1; i < liveTours; i++)
            db.Tours.Add(new Tour
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, Name = $"Extra {i}", UrlName = $"extra-{i}",
                StartOrganizationAddressId = addressId, DateCreated = now, CreatedByAppUserId = userId,
            });
        // The other business meets somewhere of its own — the API refuses a tour that starts at
        // somebody else's address, and a seed that broke that rule would make the refusal below
        // name two tours where the site could only ever produce one.
        var theirAddressId = Guid.NewGuid();
        db.OrganizationAddresses.Add(new OrganizationAddress
        {
            Id = theirAddressId, OrganizationId = otherOrgId, OrganizationAddressTypeId = Guid.NewGuid(),
            StreetAddress1 = "9 Elsewhere", City = "Memphis", State = "TN", ZipCode = "38103",
            Country = "US", DateCreated = now, CreatedByAppUserId = userId,
        });
        db.Tours.Add(new Tour
        {
            Id = otherTourId, OrganizationId = otherOrgId, Name = "Theirs", UrlName = "theirs",
            StartOrganizationAddressId = theirAddressId, DateCreated = now, CreatedByAppUserId = userId,
        });

        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = tierId, Name = "Tour & Event Business", MinMembers = 1, MaxMembers = null,
            IsBandedByMembers = false, IsActive = true, SortOrder = 90, DateCreated = now, CreatedByAppUserId = userId,
        });
        db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
        {
            Id = Guid.NewGuid(), SubscriptionTierId = tierId, Interval = BillingInterval.Monthly,
            Price = 30m, IsActive = true, DateCreated = now, CreatedByAppUserId = userId,
        });
        if (subscription)
        {
            db.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, Status = SubscriptionStatus.Active,
                SubscriptionTierId = tierId, Interval = BillingInterval.Monthly,
                CurrentPeriodStart = now.AddDays(-15), CurrentPeriodEnd = now.AddDays(15),
                PriceAtPeriodStart = 30m, MemberCountAtPeriodStart = 1, TourCountAtPeriodStart = paidTours,
                ProviderName = "Stripe", ProviderCustomerRef = "cus", ProviderPaymentMethodRef = "pm",
                DateCreated = now.AddDays(-15), CreatedByAppUserId = userId,
            });
        }
        await db.SaveChangesAsync();
        return new World(factory, orgId, otherOrgId, userId, addressId, tourId, otherTourId);
    }

    private static TourGalleryController Gallery(World w)
        => new(w.Factory, new Mock<AutoMapper.IMapper>().Object,
               new Ben.Service.RepositoryService.Services.OrganizationSecurityService(w.Factory),
               new Mock<Ben.Data.Common.Interfaces.IFileStorageService>().Object,
               new Mock<IMediaIngestService>().Object,
               new Mock<IMediaSanitizationService>().Object)
        {
            ControllerContext = Caller(w.UserId),
        };

    private static ControllerContext Caller(Guid userId) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
        },
    };

    // ── the gallery belongs to its own tour's business ───────────────────────

    [Fact]
    public async Task A_gallery_picture_cannot_be_deleted_through_another_businesss_route()
    {
        // Authorisation checked the orgId in the ROUTE, which the caller picks. Without scoping
        // the picture through its own tour, somebody with settings rights on their own group
        // could destroy another group's photograph — bytes and all — by knowing an image id.
        var w = await SeedAsync();
        Guid imageId = Guid.NewGuid(), fileId = Guid.NewGuid();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = Guid.NewGuid(), FileName = "x.jpg",
                StoredFileName = "x.jpg", ContentType = "image/jpeg", FileSize = 1,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.UserId,
            });
            db.TourGalleryImages.Add(new TourGalleryImage
            {
                Id = imageId, TourId = w.OtherTourId, UploadFileId = fileId, SortOrder = 0,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.UserId,
            });
            await db.SaveChangesAsync();
        }

        // The caller owns w.OrgId and names it in the route; the tour and image are the other org's.
        var result = await Gallery(w).Delete(w.OrgId, w.OtherTourId, imageId, default);

        Assert.IsType<NotFoundResult>(result);
        await using var after = await w.Factory.CreateDbContextAsync();
        Assert.Equal(1, await after.TourGalleryImages.CountAsync());
    }

    [Fact]
    public async Task A_gallery_caption_cannot_be_rewritten_through_another_businesss_route()
    {
        var w = await SeedAsync();
        var imageId = Guid.NewGuid();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.TourGalleryImages.Add(new TourGalleryImage
            {
                Id = imageId, TourId = w.OtherTourId, UploadFileId = Guid.NewGuid(),
                SortOrder = 0, Caption = "Theirs",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.UserId,
            });
            await db.SaveChangesAsync();
        }

        var result = await Gallery(w).Update(
            w.OrgId, w.OtherTourId, imageId, new UpdateTourImageRequest("Mine now", null), default);

        Assert.IsType<NotFoundResult>(result.Result);
        await using var after = await w.Factory.CreateDbContextAsync();
        Assert.Equal("Theirs", (await after.TourGalleryImages.SingleAsync()).Caption);
    }

    // ── the meeting point cannot be deleted out from under a tour ────────────

    [Fact]
    public async Task An_address_a_tour_meets_at_is_refused_by_name_rather_than_by_a_database_error()
    {
        var w = await SeedAsync();
        var ctrl = new OrganizationAddressCrudController(
            w.Factory, new Mock<AutoMapper.IMapper>().Object,
            new Ben.Service.RepositoryService.Services.OrganizationSecurityService(w.Factory),
            new Mock<Ben.Service.RepositoryService.GenericInterfaces.IAuditLogService>().Object)
        {
            ControllerContext = Caller(w.UserId),
        };

        var result = await ctrl.Delete(w.OrgId, w.AddressId, default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Mine", refusal.Value!.ToString());
        Assert.Contains("meets at this address", refusal.Value!.ToString());
    }

    // ── a paused or retired tour takes nobody ────────────────────────────────

    [Theory]
    [InlineData(true, false, "isn't taking sign-ups")]
    [InlineData(false, true, "no longer running")]
    public void A_paused_or_retired_tour_stops_sign_ups(bool paused, bool retired, string expected)
    {
        var ev = new OrgCalendarEvent
        {
            Id = Guid.NewGuid(), Title = "Saturday walk",
            StartDateTime = DateTime.UtcNow.AddDays(2), EndDateTime = DateTime.UtcNow.AddDays(2).AddHours(1),
            Tour = new Tour
            {
                Name = "Mine", UrlName = "mine",
                IsBookable = !paused,
                RetiredAtUtc = retired ? DateTime.UtcNow : null,
            },
        };

        var why = PublicEventController.WhyTourIsNotTakingSignUps(ev);
        Assert.NotNull(why);
        Assert.Contains(expected, why);
    }

    [Fact]
    public void An_ordinary_event_and_a_running_tour_are_both_left_alone()
    {
        Assert.Null(PublicEventController.WhyTourIsNotTakingSignUps(new OrgCalendarEvent { Title = "x" }));
        Assert.Null(PublicEventController.WhyTourIsNotTakingSignUps(new OrgCalendarEvent
        {
            Title = "x",
            Tour = new Tour { Name = "Mine", UrlName = "mine", IsBookable = true },
        }));
    }

    // ── money ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Two_tours_added_at_once_are_charged_for_two_and_not_for_three()
    {
        // Both callers used to read the old paid-for count, compute different extras and send
        // different idempotency keys — so a business that added two tours in the same minute was
        // charged three units of remainder.
        var w = await SeedAsync(liveTours: 3, paidTours: 1);
        var gateway = new FakeGateway();
        var service = new TourAddOnService(gateway, NullLogger<TourAddOnService>.Instance);

        async Task ChargeAsync()
        {
            await using var db = await w.Factory.CreateDbContextAsync();
            var org = await db.Organizations.FirstAsync(o => o.Id == w.OrgId);
            await service.ChargeRemainderAsync(db, org, w.UserId, default);
        }

        await Task.WhenAll(ChargeAsync(), ChargeAsync());

        // Two tours were added; whatever order the two calls landed in, the money adds up to two.
        var unit = 30m * 15m / 30m;   // half a 30-day period at $30
        var total = gateway.Charges.Sum(c => c.Total);
        Assert.InRange(total, unit * 2 - 2m, unit * 2 + 2m);

        await using var after = await w.Factory.CreateDbContextAsync();
        Assert.Equal(3, (await after.OrganizationSubscriptions.SingleAsync()).TourCountAtPeriodStart);
    }

    [Fact]
    public async Task A_business_on_a_free_coupon_is_not_charged_for_a_second_tour()
    {
        // "Your first three months are free" has to mean the tour added on day three as well.
        var w = await SeedAsync(liveTours: 2, paidTours: 1);
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var coupon = new Coupon
            {
                Id = Guid.NewGuid(), Name = "Founding", PercentOff = 100,
                DurationPeriods = 3, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.UserId,
            };
            var code = new CouponCode
            {
                Id = Guid.NewGuid(), CouponId = coupon.Id, Code = "FOUNDING",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.UserId,
            };
            db.Coupons.Add(coupon);
            db.CouponCodes.Add(code);
            db.CouponRedemptions.Add(new CouponRedemption
            {
                Id = Guid.NewGuid(), CouponId = coupon.Id, CouponCodeId = code.Id,
                OrganizationId = w.OrgId, RedeemedAtUtc = DateTime.UtcNow.AddDays(-15),
                PeriodsRemaining = 2, ListPrice = 30m, Discount = 30m, Payable = 0m,
                DateCreated = DateTime.UtcNow.AddDays(-15), CreatedByAppUserId = w.UserId,
            });
            await db.SaveChangesAsync();
        }

        var gateway = new FakeGateway();
        await using var db2 = await w.Factory.CreateDbContextAsync();
        var org = await db2.Organizations.FirstAsync(o => o.Id == w.OrgId);
        var outcome = await new TourAddOnService(gateway, NullLogger<TourAddOnService>.Instance)
            .ChargeRemainderAsync(db2, org, w.UserId, default);

        Assert.Empty(gateway.Charges);
        Assert.Equal(0m, outcome.Charged);
        Assert.Contains("coupon", outcome.Note, StringComparison.OrdinalIgnoreCase);
        // And the tour is still counted, so the renewal prices two.
        Assert.Equal(2, (await db2.OrganizationSubscriptions.SingleAsync()).TourCountAtPeriodStart);
    }
}
