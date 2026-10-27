using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Access;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Who may scan a field session for EVPs, and whose copy they get (phase 9 of the audio plan,
/// 10/27/2026): every kind of person against every kind of session.
/// </summary>
/// <remarks>
/// The row that matters most is the stranger on an open investigation: they may READ it (the public
/// door), and must not scan it. A shortcut through <c>MayReadAsync</c> would pass every other row.
/// </remarks>
public sealed class FieldSessionAnalysisAccessTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    // People.
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();
    private static readonly Guid Attendee = Guid.NewGuid();
    private static readonly Guid Member = Guid.NewGuid();
    private static readonly Guid FormerMember = Guid.NewGuid();
    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly Guid UnconfirmedStaff = Guid.NewGuid();
    private static readonly Guid VenueMember = Guid.NewGuid();
    private static readonly Guid MemberOwner = Guid.NewGuid();

    // Groups and places.
    private static readonly Guid Group = Guid.NewGuid();
    private static readonly Guid Venue = Guid.NewGuid();
    private static readonly Guid PublicPlace = Guid.NewGuid();
    private static readonly Guid PrivateInvestigation = Guid.NewGuid();
    private static readonly Guid OpenInvestigation = Guid.NewGuid();
    private static readonly Guid Hosted = Guid.NewGuid();

    // Sessions.
    private static readonly Guid Personal = Guid.NewGuid();
    private static readonly Guid OnPrivate = Guid.NewGuid();
    private static readonly Guid OnOpen = Guid.NewGuid();
    private static readonly Guid AtHosted = Guid.NewGuid();
    private static readonly Guid Published = Guid.NewGuid();
    private static readonly Guid PublishedUnreviewed = Guid.NewGuid();
    private static readonly Guid OnPrivateByMember = Guid.NewGuid();

    private const string Mine = "mine";
    private const string Groups = "group";
    private const string Venues = "venue";

    public static TheoryData<string, string, string> Matrix => new()
    {
        // Person              Session                 Expected: scopes, or the refusal
        { nameof(Owner),       nameof(Personal),            Mine },
        { nameof(Stranger),    nameof(Personal),            nameof(ScanRefusal.NotYours) },
        { nameof(Member),      nameof(Personal),            nameof(ScanRefusal.NotYours) },

        { nameof(Owner),       nameof(OnPrivate),           Mine },   // not a member: their own copy only
        { nameof(Attendee),    nameof(OnPrivate),           Groups },
        { nameof(Member),      nameof(OnPrivate),           Groups },
        { nameof(MemberOwner), nameof(OnPrivateByMember),   $"{Mine},{Groups}" },
        { nameof(FormerMember),nameof(OnPrivate),           nameof(ScanRefusal.NotYours) },
        { nameof(Stranger),    nameof(OnPrivate),           nameof(ScanRefusal.NotYours) },

        // An open investigation lets anybody watch, and only its people scan.
        { nameof(Stranger),    nameof(OnOpen),              nameof(ScanRefusal.NotYours) },
        { nameof(Member),      nameof(OnOpen),              Groups },

        { nameof(Member),      nameof(AtHosted),            Groups },
        { nameof(Staff),       nameof(AtHosted),            Groups },
        { nameof(UnconfirmedStaff), nameof(AtHosted),       nameof(ScanRefusal.NotYours) },
        { nameof(Attendee),    nameof(AtHosted),            nameof(ScanRefusal.NotYours) },
        { nameof(Owner),       nameof(AtHosted),            Mine },   // the guest who sent it

        { nameof(VenueMember), nameof(Published),           Venues },
        { nameof(Stranger),    nameof(Published),           nameof(ScanRefusal.PublicViewOnly) },
        { nameof(Owner),       nameof(Published),           Mine },

        // Published but its media not yet approved for show: still somebody's private night.
        { nameof(VenueMember), nameof(PublishedUnreviewed), nameof(ScanRefusal.PublicViewOnly) },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Who_may_scan_which_session(string person, string session, string expected)
    {
        var factory = TestDbFactory.Create();
        await SeedAsync(factory);
        await using var db = await factory.CreateDbContextAsync();
        var row = await db.FieldSessionUploads.FirstAsync(s => s.Id == SessionId(session));

        var decision = await FieldSessionAnalysisAccess.MayScanAsync(db, row, PersonId(person), null, null, default);
        var scopes = await FieldSessionAnalysisAccess.ScopesAsync(db, row, PersonId(person), default);

        if (Enum.TryParse<ScanRefusal>(expected, out var refusal))
        {
            Assert.Equal(refusal, decision.Refusal);
            Assert.Empty(scopes);
            Assert.Equal(FieldSessionAnalysisAccess.Describe(refusal), decision.Message);
        }
        else
        {
            Assert.True(decision.Allowed, decision.Message);
            Assert.Equal(expected, string.Join(",", scopes.Select(Name)));
            Assert.Equal(scopes[0], decision.Scope);
        }
    }

    [Fact]
    public async Task Nobody_signed_in_is_told_to_sign_in()
    {
        var factory = TestDbFactory.Create();
        await SeedAsync(factory);
        await using var db = await factory.CreateDbContextAsync();
        var row = await db.FieldSessionUploads.FirstAsync(s => s.Id == Published);

        var decision = await FieldSessionAnalysisAccess.MayScanAsync(db, row, Guid.Empty, null, null, default);
        Assert.Equal(ScanRefusal.NotSignedIn, decision.Refusal);
    }

    [Fact]
    public async Task Asking_for_a_copy_you_may_not_have_is_refused()
    {
        var factory = TestDbFactory.Create();
        await SeedAsync(factory);
        await using var db = await factory.CreateDbContextAsync();
        var hosted = await db.FieldSessionUploads.FirstAsync(s => s.Id == AtHosted);

        // Staff may scan for the group, never into a copy of their own: the recording is not theirs.
        var own = await FieldSessionAnalysisAccess.MayScanAsync(db, hosted, Staff, AnalysisScope.Person, null, default);
        Assert.Equal(ScanRefusal.NotYours, own.Refusal);

        var otherGroup = await FieldSessionAnalysisAccess.MayScanAsync(db, hosted, Staff, AnalysisScope.Organization, Venue, default);
        Assert.Equal(ScanRefusal.NotYours, otherGroup.Refusal);

        var theGroup = await FieldSessionAnalysisAccess.MayScanAsync(db, hosted, Staff, AnalysisScope.Organization, Group, default);
        Assert.True(theGroup.Allowed);
        Assert.Equal(Group, theGroup.Scope!.OrganizationId);
        Assert.Equal("Hollow Creek Paranormal's copy", theGroup.Scope.Label);
    }

    [Fact]
    public async Task A_groups_copy_is_seen_by_its_confirmed_staff_and_nobody_else_outside_it()
    {
        var factory = TestDbFactory.Create();
        await SeedAsync(factory);
        var copyId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = copyId, UploadFileTypeId = Guid.NewGuid(), OwnerOrganizationId = Group,
                FileName = "Session audio-001.m4a", StoredFileName = "x.m4a", ContentType = "audio/mp4",
                SourceFieldSessionUploadId = AtHosted, SourceClipPath = "media/audio-001.m4a",
                DateCreated = Now, CreatedByAppUserId = Member,
            });
            await db.SaveChangesAsync();
        }

        await using var check = await factory.CreateDbContextAsync();
        Assert.True(await FileAudienceAccess.CanViewFileAsync(check, copyId, Member, default));
        Assert.True(await FileAudienceAccess.CanViewFileAsync(check, copyId, Staff, default));
        Assert.False(await FileAudienceAccess.CanViewFileAsync(check, copyId, UnconfirmedStaff, default));
        Assert.False(await FileAudienceAccess.CanViewFileAsync(check, copyId, Stranger, default));
        // The guest who recorded it sees their session, not the group's working copy.
        Assert.False(await FileAudienceAccess.CanViewFileAsync(check, copyId, Owner, default));
    }

    private static string Name(ScanScope scope) => scope switch
    {
        { Scope: AnalysisScope.Person } => Mine,
        { OrganizationId: var org } when org == Group => Groups,
        { OrganizationId: var org } when org == Venue => Venues,
        _ => "?",
    };

    private static Guid PersonId(string name) => name switch
    {
        nameof(Owner) => Owner, nameof(Stranger) => Stranger, nameof(Attendee) => Attendee,
        nameof(Member) => Member, nameof(FormerMember) => FormerMember, nameof(Staff) => Staff,
        nameof(UnconfirmedStaff) => UnconfirmedStaff, nameof(VenueMember) => VenueMember,
        nameof(MemberOwner) => MemberOwner,
        _ => throw new ArgumentOutOfRangeException(name),
    };

    private static Guid SessionId(string name) => name switch
    {
        nameof(Personal) => Personal, nameof(OnPrivate) => OnPrivate, nameof(OnOpen) => OnOpen,
        nameof(AtHosted) => AtHosted, nameof(Published) => Published,
        nameof(PublishedUnreviewed) => PublishedUnreviewed, nameof(OnPrivateByMember) => OnPrivateByMember,
        _ => throw new ArgumentOutOfRangeException(name),
    };

    private static async Task SeedAsync(IDbContextFactory<BenDataContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();

        db.Organizations.Add(new Organization { Id = Group, Name = "Hollow Creek Paranormal", DateCreated = Now, CreatedByAppUserId = Member });
        db.Organizations.Add(new Organization { Id = Venue, Name = "The Thomas House", DateCreated = Now, CreatedByAppUserId = VenueMember });

        Membership(db, Group, Member, active: true);
        Membership(db, Group, FormerMember, active: false);
        Membership(db, Venue, VenueMember, active: true);
        Membership(db, Group, MemberOwner, active: true);

        db.Investigations.Add(new Investigation
        {
            Id = PrivateInvestigation, OrganizationId = Group, Title = "Hollow Creek Road",
            ScheduledDateTime = Now, DateCreated = Now, CreatedByAppUserId = Member,
        });
        db.Investigations.Add(new Investigation
        {
            Id = OpenInvestigation, OrganizationId = Group, Title = "Open night", Visibility = InvestigationVisibility.Public,
            ScheduledDateTime = Now, DateCreated = Now, CreatedByAppUserId = Member,
        });
        db.InvestigationAttendees.Add(new InvestigationAttendee
        {
            Id = Guid.NewGuid(), InvestigationId = PrivateInvestigation, AppUserId = Attendee,
            DateCreated = Now, CreatedByAppUserId = Member,
        });

        db.Places.Add(new Place { Id = PublicPlace, Name = "The Thomas House Hotel", Kind = PlaceKind.PublicLocation, DateCreated = Now, CreatedByAppUserId = VenueMember });
        db.OrganizationVenueProfiles.Add(new OrganizationVenueProfile
        {
            Id = Guid.NewGuid(), OrganizationId = Venue, PlaceId = PublicPlace, VerifiedUtc = Now,
            DateCreated = Now, CreatedByAppUserId = VenueMember,
        });

        db.HostedEvents.Add(new HostedEvent
        {
            Id = Hosted, OrganizationId = Group, PlaceId = PublicPlace, Name = "Seance Weekend", UrlName = "seance-weekend",
            StartsOn = Now.Date, EndsOn = Now.Date.AddDays(1), LayoutKind = HostedEventLayoutKind.Seats,
            DateCreated = Now, CreatedByAppUserId = Member,
        });
        db.HostedEventStaff.Add(new HostedEventStaff
        {
            Id = Guid.NewGuid(), HostedEventId = Hosted, AppUserId = Staff, DateConfirmed = Now,
            DateCreated = Now, CreatedByAppUserId = Member,
        });
        db.HostedEventStaff.Add(new HostedEventStaff
        {
            Id = Guid.NewGuid(), HostedEventId = Hosted, AppUserId = UnconfirmedStaff,
            DateCreated = Now, CreatedByAppUserId = Member,
        });

        Session(db, Personal);
        Session(db, OnPrivate, s => s.InvestigationId = PrivateInvestigation);
        Session(db, OnOpen, s => s.InvestigationId = OpenInvestigation);
        Session(db, OnPrivateByMember, s => { s.InvestigationId = PrivateInvestigation; s.SubmittedByAppUserId = MemberOwner; });
        Session(db, AtHosted, s => s.HostedEventId = Hosted);
        Session(db, Published, s =>
        {
            s.PlaceId = PublicPlace; s.PublishedAtUtc = Now; s.MediaReviewState = FeedMediaReviewState.Approved;
        });
        Session(db, PublishedUnreviewed, s =>
        {
            s.PlaceId = PublicPlace; s.PublishedAtUtc = Now; s.MediaReviewState = FeedMediaReviewState.Pending;
        });

        await db.SaveChangesAsync();
    }

    private static void Membership(BenDataContext db, Guid org, Guid person, bool active)
        => db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = org, AppUserId = person, Role = OrganizationMemberRole.Member,
            IsActive = active, DateCreated = Now, CreatedByAppUserId = person,
        });

    private static void Session(BenDataContext db, Guid id, Action<FieldSessionUpload>? shape = null)
    {
        var session = new FieldSessionUpload
        {
            Id = id, SubmittedByAppUserId = Owner, DeviceSessionId = Guid.NewGuid(), DeviceModel = "iPhone",
            DocumentUploadFileId = Guid.NewGuid(), StartedAt = Now.AddHours(-2),
            DateCreated = Now, CreatedByAppUserId = Owner,
        };
        shape?.Invoke(session);
        db.FieldSessionUploads.Add(session);
    }
}
