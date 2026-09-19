using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Keeping a file, which is the exception the whole retention rule rests on (item 233).
/// </summary>
/// <remarks>
/// Ben's rule is that a tour business's photographs last a month and its recordings a week
/// "unless they mark them to be saved". The marking had no endpoint and no screen when the sweep
/// shipped: the only keep that existed was putting a picture on a tour's page, so a recording
/// could not be kept at all. These pin the endpoint that fixed it, and the ownership rule that
/// stops one business keeping another's files.
/// </remarks>
public sealed class KeptMediaTests
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
        IDbContextFactory<BenDataContext> Factory, Guid OrgId, Guid OtherOrgId,
        Guid OwnerId, Guid FileId, Guid OtherFileId);

    /// <summary>
    /// Two businesses, each with one guest submission on a public date of its own.
    /// </summary>
    private static async Task<World> SeedAsync()
    {
        var factory = Db();
        var now = DateTime.UtcNow;
        Guid ownerId = Guid.NewGuid();
        var orgs = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var files = new Guid[2];

        await using var db = await factory.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser { Id = ownerId, UserName = "o", Email = "o@t.com", DisplayName = "Owner", DateCreated = now });

        for (var i = 0; i < 2; i++)
        {
            var eventId = Guid.NewGuid();
            files[i] = Guid.NewGuid();

            db.Organizations.Add(new Organization
            {
                Id = orgs[i], Name = $"Walks {i}", UrlName = $"walks-{i}",
                Kind = OrganizationKind.GhostWalkingTour, DateCreated = now, CreatedByAppUserId = ownerId,
            });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgs[i], AppUserId = ownerId,
                Role = OrganizationMemberRole.Owner, IsActive = true, DateCreated = now, CreatedByAppUserId = ownerId,
            });
            db.OrgCalendarEvents.Add(new OrgCalendarEvent
            {
                Id = eventId, OrganizationId = orgs[i], Title = "A walk",
                StartDateTime = now.AddDays(-1), EndDateTime = now.AddDays(-1).AddHours(2),
                IsPublic = true, DateCreated = now, CreatedByAppUserId = ownerId,
            });
            db.UploadFiles.Add(new UploadFile
            {
                Id = files[i], UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
                FileName = "evp.m4a", StoredFileName = "evp.m4a", ContentType = "audio/mp4",
                FileSize = 1, ExpiresAtUtc = now.AddDays(7),
                DateCreated = now, CreatedByAppUserId = ownerId,
            });
            db.EventEvidenceSubmissions.Add(new EventEvidenceSubmission
            {
                Id = Guid.NewGuid(), OrgCalendarEventId = eventId, SubmittedByAppUserId = ownerId,
                UploadFileId = files[i], Status = EvidenceSubmissionStatus.Accepted,
                DateCreated = now, CreatedByAppUserId = ownerId,
            });
        }
        await db.SaveChangesAsync();

        return new World(factory, orgs[0], orgs[1], ownerId, files[0], files[1]);
    }

    private static KeptMediaController Build(World w)
        => new(w.Factory, new Mock<AutoMapper.IMapper>().Object,
               new Ben.Service.RepositoryService.Services.OrganizationSecurityService(w.Factory))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, w.OwnerId.ToString())], "Bearer")),
                    RequestServices = new ServiceProviderStub(w.Factory),
                },
            },
        };

    /// <summary>Enough of a container for the one service the release path resolves.</summary>
    private sealed class ServiceProviderStub(IDbContextFactory<BenDataContext> factory) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(Ben.Data.WebApi.Services.Billing.SubscriptionLimitGuard)
                ? new Ben.Data.WebApi.Services.Billing.SubscriptionLimitGuard(factory)
                : null;
    }

    [Fact]
    public async Task Keeping_a_recording_stops_its_clock_and_says_who_did_it()
    {
        var w = await SeedAsync();

        Assert.IsType<NoContentResult>(await Build(w).Keep(w.OrgId, w.FileId, default));

        await using var db = await w.Factory.CreateDbContextAsync();
        var file = await db.UploadFiles.FirstAsync(f => f.Id == w.FileId);
        Assert.NotNull(file.KeptAtUtc);
        Assert.Equal(w.OwnerId, file.KeptByAppUserId);
        Assert.Null(file.ExpiresAtUtc);
    }

    [Fact]
    public async Task Letting_it_go_again_restarts_the_clock_from_today_rather_than_from_upload()
    {
        // Releasing something kept six months ago must not delete it in the same second, and the
        // uploader is warned afresh either way.
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            // A tour plan with a 7-day recording rule, so releasing has something to count.
            var tierId = Guid.NewGuid();
            db.SubscriptionTiers.Add(new SubscriptionTier
            {
                Id = tierId, Name = "Tour & Event Business", MinMembers = 1, MaxMembers = null,
                IsBandedByMembers = false, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
            });
            db.SubscriptionTierLimits.Add(new SubscriptionTierLimit
            {
                Id = Guid.NewGuid(), SubscriptionTierId = tierId,
                Limit = SubscriptionLimit.RecordingRetentionDays, MaxValue = 7,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
            });
            db.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = w.OrgId, Status = SubscriptionStatus.Active,
                SubscriptionTierId = tierId, Interval = BillingInterval.Monthly,
                CurrentPeriodStart = DateTime.UtcNow.AddDays(-1), CurrentPeriodEnd = DateTime.UtcNow.AddDays(29),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
            });
            var kept = await db.UploadFiles.FirstAsync(f => f.Id == w.FileId);
            kept.KeptAtUtc = DateTime.UtcNow.AddMonths(-6);
            kept.KeptByAppUserId = w.OwnerId;
            kept.ExpiresAtUtc = null;
            kept.ExpiryNoticeSentAtUtc = DateTime.UtcNow.AddMonths(-6);
            await db.SaveChangesAsync();
        }

        Assert.IsType<NoContentResult>(await Build(w).Release(w.OrgId, w.FileId, default));

        await using var after = await w.Factory.CreateDbContextAsync();
        var file = await after.UploadFiles.FirstAsync(f => f.Id == w.FileId);
        Assert.Null(file.KeptAtUtc);
        Assert.Null(file.KeptByAppUserId);
        Assert.NotNull(file.ExpiresAtUtc);
        Assert.InRange(file.ExpiresAtUtc!.Value, DateTime.UtcNow.AddDays(6), DateTime.UtcNow.AddDays(8));
        // Warned again before it goes, rather than treated as already told.
        Assert.Null(file.ExpiryNoticeSentAtUtc);
    }

    [Fact]
    public async Task One_business_cannot_keep_another_businesss_file()
    {
        var w = await SeedAsync();

        // The caller owns both orgs here, so this is purely about the file/route pairing: naming
        // one org in the route and another's file in the path answers 404.
        Assert.IsType<NotFoundResult>(await Build(w).Keep(w.OrgId, w.OtherFileId, default));

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Null((await db.UploadFiles.FirstAsync(f => f.Id == w.OtherFileId)).KeptAtUtc);
    }

    [Fact]
    public async Task A_file_that_belongs_to_nobody_is_not_keepable()
    {
        var w = await SeedAsync();
        var loose = Guid.NewGuid();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = loose, UploadFileTypeId = Guid.NewGuid(), AppUserId = w.OwnerId,
                FileName = "personal.jpg", StoredFileName = "personal.jpg", ContentType = "image/jpeg",
                FileSize = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
            });
            await db.SaveChangesAsync();
        }

        Assert.IsType<NotFoundResult>(await Build(w).Keep(w.OrgId, loose, default));
    }
}
