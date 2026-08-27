using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.SeedData;

/// <summary>
/// Widens the seeded world: more people, a third group with a real owner, more cases, clients,
/// investigations, and gear.
/// </summary>
/// <remarks>
/// <para><b>Why this exists apart from <see cref="DevelopmentDataSeeder"/>:</b> that seeder builds
/// the minimum every screen needs to not be empty, and it is already a thousand lines. This one is
/// about density — a dev site with five accounts and one investigation looks like a demo, and
/// several bugs this project has caught (empty-list-versus-refused above all) only show against
/// data that has some variety in it. It runs after the core seeder and depends on it.</para>
///
/// <para><b>Music City Spirit Seekers exists to close item 121.</b> Both older groups are owned by
/// the SuperAdmin account, so the Owner membership tier could never be exercised separately from
/// the app role that bypasses every check. Emma owns this one and is nobody's administrator
/// app-wide, which makes her the first seeded person for whom <c>OrganizationMemberRole.Owner</c>
/// is the thing actually being tested.</para>
///
/// <para><b>Idempotent</b>, same as its parent: every block finds before it creates, so restarting
/// the API a hundred times yields one of everything. Fixed GUIDs are used where a row has no
/// natural key worth querying by.</para>
///
/// <para>Passwords follow the existing seed pattern and are dev-only, like every credential in
/// this folder. New accounts get their @handles from <c>UserHandleBackfillService</c> on startup,
/// the same way real pre-handle accounts did.</para>
/// </remarks>
internal static class DevelopmentRosterSeeder
{
    internal static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        if (!config.GetValue<bool>("SeedData:DevData:Enabled")) return;

        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var dbFactory   = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BenDataContext>>();

        var now = DateTime.UtcNow;

        // ── People ────────────────────────────────────────────────────────────
        // Investigators sign up with the group's domain; clients arrive from anywhere, which is
        // what their addresses should look like too.
        var marcus = await UserAsync(userManager, "marcus.webb@benco.dev",     "Marcus Webb",    "M@rcus!Webb2026");
        var olivia = await UserAsync(userManager, "olivia.chen@benco.dev",     "Olivia Chen",    "0l!via!Chen2026");
        var tyler  = await UserAsync(userManager, "tyler.brooks@benco.dev",    "Tyler Brooks",   "Tyl3r!Brooks26");
        var rachel = await UserAsync(userManager, "rachel.kim@benco.dev",      "Rachel Kim",     "R@chel!Kim2026");
        var david  = await UserAsync(userManager, "david.okafor@benco.dev",    "David Okafor",   "D@vid!Okafor26");
        var priya  = await UserAsync(userManager, "priya.sharma@benco.dev",    "Priya Sharma",   "Pr!ya!Sharma26");
        var nathan = await UserAsync(userManager, "nathan.cole@benco.dev",     "Nathan Cole",    "N@than!Cole2026");
        var grace  = await UserAsync(userManager, "grace.delgado@benco.dev",   "Grace Delgado",  "Gr@ce!Delgado26");
        // Victor exists so the Viewer membership tier has a seat that is ALWAYS there — before
        // him, every four-seat verification pass had to mutate a real member into a Viewer and
        // remember to put them back. The seat nobody can sit in is the seat nobody tests.
        var victor = await UserAsync(userManager, "victor.reyes@benco.dev",    "Victor Reyes",   "V!ctor!Reyes26");

        // IH-08, Ben's 2026-08-26 sweep: Site Roles reported Admin 0 users and Moderator 0, so
        // neither role's behaviour had ever run — including whatever gates the 26 /admin/* routes
        // and the /moderation/media screen. Same reasoning as Victor's Viewer seat above: the
        // seat nobody can sit in is the seat nobody tests. These two exist to be signed in as.
        //
        // Deliberately NOT given to an existing person: promoting Rachel or Marcus would change
        // what an existing seat means and quietly invalidate every check that treats them as an
        // ordinary org administrator or member.
        var alice  = await UserAsync(userManager, "alice.nguyen@benco.dev",    "Alice Nguyen",   "@lice!Nguyen26");
        var miguel = await UserAsync(userManager, "miguel.santos@benco.dev",   "Miguel Santos",  "M!guel!Santos26");
        await EnsureSiteRoleAsync(userManager, alice,  Ben.Data.Common.Constants.RoleNames.Admin);
        await EnsureSiteRoleAsync(userManager, miguel, Ben.Data.Common.Constants.RoleNames.Moderator);

        var linda  = await UserAsync(userManager, "linda.maxwell@example.com", "Linda Maxwell",  "L!nda!Maxwell26");
        var robert = await UserAsync(userManager, "robert.hayes@example.com",  "Robert Hayes",   "R0bert!Hayes26");
        var karen  = await UserAsync(userManager, "karen.foster@example.com",  "Karen Foster",   "K@ren!Foster26");

        var emma = await userManager.FindByEmailAsync("emma.rodriguez@benco.dev");
        if (emma is null)
        {
            Console.WriteLine("[RosterSeeder] Core seed users missing — skipping (does DevData run first?).");
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync();

        var tgh = await db.Organizations.FirstOrDefaultAsync(o => o.UrlName == "paranormal365" || o.UrlName == "tgh");
        var nps = await db.Organizations.FirstOrDefaultAsync(o => o.UrlName == "nps");
        if (tgh is null || nps is null)
        {
            Console.WriteLine("[RosterSeeder] Core orgs missing — skipping.");
            return;
        }

        // ── Filling out the two existing groups ───────────────────────────────
        // TGH grows to eight: a second administrator (so "the admin" stops being one person and
        // admin-only screens can be looked at by somebody who is not also in every test), and four
        // more plain members for rosters, pickers and attendee lists to have real names in.
        await MemberAsync(db, tgh, rachel, OrganizationMemberRole.Administrator, now);
        await MemberAsync(db, tgh, marcus, OrganizationMemberRole.Member, now);
        await MemberAsync(db, tgh, olivia, OrganizationMemberRole.Member, now);
        await MemberAsync(db, tgh, tyler,  OrganizationMemberRole.Member, now);
        await MemberAsync(db, tgh, david,  OrganizationMemberRole.Member, now);
        await MemberAsync(db, tgh, victor, OrganizationMemberRole.Viewer, now);

        await MemberAsync(db, nps, priya,  OrganizationMemberRole.Member, now);
        await MemberAsync(db, nps, nathan, OrganizationMemberRole.Member, now);

        // ── The third group: Music City Spirit Seekers ────────────────────────
        var mcss = await db.Organizations.FirstOrDefaultAsync(o => o.UrlName == "mcss");
        if (mcss is null)
        {
            mcss = new Organization
            {
                Id = new Guid("50000001-0000-0000-0000-000000000001"),
                Name = "Music City Spirit Seekers", UrlName = "mcss",
                IsAcceptingClients = true, IsAcceptingApplications = true,
                DateCreated = now.AddMonths(-7), CreatedByAppUserId = emma.Id,
            };
            db.Organizations.Add(mcss);
            await db.SaveChangesAsync();

            // Everything a real creation gives a group, given to a seeded one too. Every genuine
            // path — OrganizationController, AdminOrganizationController and
            // OrganizationSecurityService — adds these three at creation; a seeder that skips
            // them produces a group no real group resembles. The standalone backfill seeders do
            // NOT cover this: OrgRoleSeeder deliberately leaves alone any group that already has
            // a role, so the one role created below would mask the eight missing defaults
            // forever (found 2026-08-27 — six e2e tests failed on a fresh database with
            // "Role 'Case Manager Role' not found").
            Ben.Data.Source.Services.OrgMemberLevelDefaults.AddDefaultLevels(db, mcss.Id, emma.Id);
            Ben.Data.Source.Services.OrgInvestigationDutyDefaults.AddDefaultDuties(db, mcss.Id, emma.Id);
            Ben.Data.Source.Services.OrgRoleDefaults.AddDefaultRoles(db, mcss.Id, emma.Id);
            await db.SaveChangesAsync();
            Console.WriteLine("[RosterSeeder] Created organization: Music City Spirit Seekers (owner: Emma).");
        }

        await MemberAsync(db, mcss, emma,   OrganizationMemberRole.Owner, now);
        await MemberAsync(db, mcss, grace,  OrganizationMemberRole.Administrator, now);
        await MemberAsync(db, mcss, olivia, OrganizationMemberRole.Member, now);   // in two groups
        await MemberAsync(db, mcss, nathan, OrganizationMemberRole.Member, now);   // likewise

        await AddressAsync(db, mcss, "1015 17th Ave S", "Nashville", "TN", "37212",
            36.1495m, -86.7947m, emma.Id, now);

        // ── Clients: one request in each state that matters ───────────────────
        //
        // Linda's has been accepted by TGH and is a working case. Robert's went to MCSS and the
        // team is already in the summarizing stage. Karen's is still sitting in TGH's queue —
        // which keeps the Requests tab from being permanently empty, and gives the decline and
        // resubmit paths something real to act on by hand.
        var lindaCase = await AcceptedClientCaseAsync(db, tgh, linda,
            requestId:  new Guid("51000001-0000-0000-0000-000000000001"),
            street: "218 Fatherland St", city: "Nashville", state: "TN", zip: "37206",
            lat: 36.1755m, lon: -86.7513m, gender: ClientGender.Female, birthYear: 1971,
            requestBody: "<p>My late husband's workshop light turns itself on most nights around 3 AM. "
                       + "The breaker for that circuit is off. Our dog will no longer go past the workshop door.</p>",
            caseTitle: "Fatherland Street Workshop, Nashville TN",   // place, not the client's surname (item 178)
            caseBody: "<p>Widowed client reports electrical anomalies and animal avoidance centred on a "
                    + "detached workshop. Circuit verified dead at the panel on intake call.</p>",
            manager: rachel, pseudonym: "The Workshop Case",
            status: CaseStatus.Active, openedDaysAgo: 45, now: now);

        var robertCase = await AcceptedClientCaseAsync(db, mcss, robert,
            requestId:  new Guid("51000002-0000-0000-0000-000000000001"),
            street: "710 Gallatin Ave", city: "Nashville", state: "TN", zip: "37206",
            lat: 36.1841m, lon: -86.7419m, gender: ClientGender.Male, birthYear: 1965,
            requestBody: "<p>Bought a former funeral parlour to convert into a music venue. Contractors "
                       + "refuse to work alone on the lower floor. Tools go missing and reappear stacked.</p>",
            caseTitle: "Gallatin Avenue Venue, Nashville TN",
            caseBody: "<p>Commercial conversion of a former funeral home. Multiple independent contractor "
                    + "reports. Sweep completed; team is compiling the report.</p>",
            manager: emma, pseudonym: "The Venue",
            status: CaseStatus.Summarized, openedDaysAgo: 80, now: now);

        await PendingRequestAsync(db, tgh, karen,
            requestId: new Guid("51000003-0000-0000-0000-000000000001"),
            street: "3810 Charlotte Ave", city: "Nashville", state: "TN", zip: "37209",
            gender: ClientGender.Female, birthYear: 1990,
            body: "<p>Renting a 1920s duplex. Cold spots on the stairs, and twice I have heard my name "
                + "said clearly from an empty room. My landlord says the previous tenant reported the same.</p>",
            now: now);

        // A fourth story for the request-review flow (2026-08-26): offered to TWO groups at once
        // — BenCo (alphabetically first, so the route crawler resolves this pair) and MCSS — with
        // a photo attached, so the review page's file previews and the both-groups-see-the-
        // materials rule have something real to show. Mark it Under Review in either group's
        // pending queue to watch the vote messages go out; first group to accept wins.
        var benco = await db.Organizations.FirstOrDefaultAsync(o => o.UrlName == "benco");
        if (benco is not null && mcss is not null)
            await ContestedRequestAsync(db, [benco, mcss], alice,
                requestId: new Guid("51000004-0000-0000-0000-000000000001"),
                street: "121 Rosebank Ave", city: "Nashville", state: "TN", zip: "37206",
                gender: ClientGender.Female, birthYear: 1987,
                body: "<p>The attic hatch opens on its own — I have found it hanging open three "
                    + "mornings in a row with the cord still looped on its hook. My daughter says "
                    + "someone hums up there. Photo of the hatch attached.</p>",
                now: now);

        // ── Investigations with full rosters ──────────────────────────────────
        //
        // Spread over past months so the dashboard's time charts have a shape, with one still
        // ahead so upcoming-investigation surfaces (reminders included) have a subject. Attendee
        // lists mix RSVP states on purpose — a roster where everyone accepted exercises none of
        // the RSVP rendering.
        if (lindaCase is not null)
        {
            await InvestigationAsync(db, lindaCase,
                id: new Guid("52000001-0000-0000-0000-000000000001"),
                title: "Workshop Baseline Sweep",
                description: "Full EMF and audio baseline of the workshop and yard. Verify the panel.",
                location: "Fatherland St residence — Workshop",
                start: now.AddDays(-32).Date.AddHours(21), hours: 5,
                status: InvestigationStatus.Completed, creator: rachel, now: now,
                notes: "<p>Circuit confirmed dead. Light did not activate during the session, but audio "
                     + "captured an unexplained double-knock at 2:47 AM, repeated at 3:12 AM.</p>",
                (rachel, "Lead Investigator", RsvpStatus.Accepted, (bool?)true),
                (marcus, "Audio Technician",  RsvpStatus.Accepted, (bool?)true),
                (olivia, "EMF Specialist",    RsvpStatus.Accepted, (bool?)true),
                (tyler,  "Photographer",      RsvpStatus.Declined, (bool?)null));

            await InvestigationAsync(db, lindaCase,
                id: new Guid("52000001-0000-0000-0000-000000000002"),
                title: "Workshop Follow-up — Overnight",
                description: "Extended overnight session focused on the 2–4 AM window the baseline flagged.",
                location: "Fatherland St residence — Workshop + main house",
                start: now.AddDays(9).Date.AddHours(22), hours: 6,
                status: InvestigationStatus.Scheduled, creator: rachel, now: now,
                notes: null,
                (rachel, "Lead Investigator", RsvpStatus.Accepted, (bool?)null),
                (marcus, "Audio Technician",  RsvpStatus.Accepted, (bool?)null),
                (david,  "Camera Operator",   RsvpStatus.Invited,  (bool?)null));
        }

        if (robertCase is not null)
        {
            await InvestigationAsync(db, robertCase,
                id: new Guid("52000002-0000-0000-0000-000000000001"),
                title: "Lower Floor Survey",
                description: "Contractor-reported area. Object placement grid and time-lapse coverage.",
                location: "Gallatin Avenue — Lower floor",
                start: now.AddDays(-60).Date.AddHours(20), hours: 6,
                status: InvestigationStatus.Completed, creator: emma, now: now,
                notes: "<p>Time-lapse caught nothing conclusive. Two team members independently logged "
                     + "a tobacco smell in the embalming room; no source found. Grid undisturbed.</p>",
                (emma,   "Lead Investigator", RsvpStatus.Accepted, (bool?)true),
                (grace,  "Evidence Manager",  RsvpStatus.Accepted, (bool?)true),
                (nathan, "Camera Operator",   RsvpStatus.Accepted, (bool?)false));
        }

        // ── More gear, from more owners ───────────────────────────────────────
        //
        // Three real brands the community actually buys, so the catalog's browse-by-make page has
        // more than a single column, and items owned by people who are not Sarah — the catalog
        // never names owners, but loan flows do, and until now every loan led back to two people.
        var panasonic = await BrandAsync(db, "Panasonic",        emma.Id, now);
        var kii       = await BrandAsync(db, "K-II Enterprises", emma.Id, now);
        var tascam    = await BrandAsync(db, "Tascam",           emma.Id, now);

        var dr60  = await ModelAsync(db, panasonic, "Audio Recorder", "RR-DR60",       emma.Id, now);
        var meter = await ModelAsync(db, kii,       "EMF Meter",      "K-II EMF Meter", emma.Id, now);
        var dr40  = await ModelAsync(db, tascam,    "Audio Recorder", "DR-40X",        emma.Id, now);

        if (dr60 is not null && meter is not null && dr40 is not null)
        {
            var marcusRecorder = await ItemAsync(db, "Tascam DR-40X", marcus, dr40,
                EquipmentLoanAudience.SharedGroups, inCatalog: true,
                "Fresh windscreens in the side pocket.", now);
            var oliviaMeter = await ItemAsync(db, "K-II Meter", olivia, meter,
                EquipmentLoanAudience.SharedGroups, inCatalog: true,
                "Sticker on the back marks the sticky button.", now);
            // The classic DR60 is irreplaceable and stays home — a not-loanable, not-listed piece
            // owned by somebody outside TGH, for contrast in every direction.
            await ItemAsync(db, "Panasonic RR-DR60", priya, dr60,
                EquipmentLoanAudience.NotLoanable, inCatalog: false,
                "Do not lend. Original 1990s unit.", now);

            // tgh/mcss come from FirstOrDefaultAsync and the compiler cannot see the guard that
            // established them further up; asserted rather than re-checked, so a genuinely missing
            // org fails loudly in the seeder instead of silently skipping the shares.
            await ShareAsync(db, marcusRecorder, tgh!,  marcus.Id, now);
            await ShareAsync(db, oliviaMeter,    tgh!,  olivia.Id, now);
            await ShareAsync(db, oliviaMeter,    mcss!, olivia.Id, now);   // shared into both her groups
        }

        await db.SaveChangesAsync();
        await AssignInvestigatorRolesAsync(dbFactory, now);

        Console.WriteLine("[RosterSeeder] Roster seed complete — 11 people, MCSS, 3 client stories, 3 investigations, 3 brands.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Puts somebody in a site-wide role, idempotently.</summary>
    /// <remarks>
    /// The roles themselves are created by <see cref="SuperAdminSeeder"/>; this only fills them.
    /// A failure is reported and swallowed: a missing role holder is a gap in test coverage, not
    /// a reason for the whole seed to fall over.
    /// </remarks>
    private static async Task EnsureSiteRoleAsync(
        UserManager<AppUser> userManager, AppUser user, string roleName)
    {
        if (await userManager.IsInRoleAsync(user, roleName)) return;

        var result = await userManager.AddToRoleAsync(user, roleName);
        if (!result.Succeeded)
        {
            Console.WriteLine($"[RosterSeeder] Could not put {user.Email} in '{roleName}': "
                            + string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }

    private static async Task<AppUser> UserAsync(
        UserManager<AppUser> userManager, string email, string displayName, string password)
    {
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null) return existing;

        var user = new AppUser
        {
            UserName = email, Email = email, DisplayName = displayName,
            EmailConfirmed = true, DateCreated = DateTime.UtcNow,
            // Seeded people are established users: the first-run wizard has nothing to ask them,
            // and an unstamped account is redirected to /onboarding on every navigation — which
            // silently hijacked whole e2e fixtures after the database rebuild.
            DateOnboarded = DateTime.UtcNow,
        };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to create seed user '{email}': {string.Join(", ", result.Errors.Select(e => e.Description))}");

        Console.WriteLine($"[RosterSeeder] Created user: {email}");
        return user;
    }

    private static async Task MemberAsync(
        BenDataContext db, Organization org, AppUser user, OrganizationMemberRole role, DateTime now)
    {
        if (await db.OrganizationUserMemberships.AnyAsync(
                m => m.OrganizationId == org.Id && m.AppUserId == user.Id))
            return;

        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id, AppUserId = user.Id,
            Role = role, IsActive = true,
            DateCreated = now, CreatedByAppUserId = user.Id,
        });
        await db.SaveChangesAsync();
    }

    private static async Task AddressAsync(
        BenDataContext db, Organization org, string street, string city, string state, string zip,
        decimal lat, decimal lon, Guid createdBy, DateTime now)
    {
        if (await db.OrganizationAddresses.AnyAsync(a => a.OrganizationId == org.Id)) return;

        var type = await db.OrganizationAddressTypes.FirstOrDefaultAsync(t => t.Name == "Headquarters");
        if (type is null) return;   // core seeder has not run

        db.OrganizationAddresses.Add(new OrganizationAddress
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id, OrganizationAddressTypeId = type.Id,
            StreetAddress1 = street, City = city, State = state, ZipCode = zip, Country = "US",
            Latitude = lat, Longitude = lon,
            DateCreated = now, CreatedByAppUserId = createdBy,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>An assigned request plus the case it became, with a first timeline entry.</summary>
    /// <returns>The case, or null when it already existed (nothing further needs adding).</returns>
    private static async Task<Case?> AcceptedClientCaseAsync(
        BenDataContext db, Organization org, AppUser client, Guid requestId,
        string street, string city, string state, string zip, decimal lat, decimal lon,
        ClientGender gender, int birthYear, string requestBody,
        string caseTitle, string caseBody, AppUser manager, string pseudonym,
        CaseStatus status, int openedDaysAgo, DateTime now)
    {
        if (await db.ClientRequests.AnyAsync(r => r.Id == requestId))
            return await db.Cases.FirstOrDefaultAsync(c => c.ClientRequestId == requestId);

        var request = new ClientRequest
        {
            Id = requestId, AppUserId = client.Id,
            Status = ClientRequestStatus.Assigned,
            StreetAddress1 = street, City = city, State = state, ZipCode = zip, Country = "US",
            Latitude = lat, Longitude = lon,
            Gender = gender, BirthYear = birthYear,
            Description = requestBody,
            DateCreated = now.AddDays(-openedDaysAgo - 7), CreatedByAppUserId = client.Id,
        };
        db.ClientRequests.Add(request);

        db.ClientRequestOrganizations.Add(new ClientRequestOrganization
        {
            ClientRequestId = requestId, OrganizationId = org.Id,
            Status = ClientOrgRequestStatus.Accepted,
            DateApplied = now.AddDays(-openedDaysAgo - 7),
            DateResponded = now.AddDays(-openedDaysAgo),
            RespondedByAppUserId = manager.Id,
            DateCreated = now.AddDays(-openedDaysAgo - 7), CreatedByAppUserId = client.Id,
        });

        var nextNum = (await db.Cases
            .Where(c => c.OrganizationId == org.Id && c.CaseYear == now.Year)
            .MaxAsync(c => (int?)c.OrgCaseNumber) ?? 0) + 1;

        var theCase = new Case
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id,
            ClientRequestId = requestId, CaseManagerAppUserId = manager.Id,
            CaseYear = now.Year, OrgCaseNumber = nextNum,
            Title = caseTitle, Description = caseBody,
            StreetAddress1 = street, City = city, State = state, ZipCode = zip, Country = "US",
            Latitude = lat, Longitude = lon,
            Status = status, IsPublic = false, PublicPseudonym = pseudonym,
            DateCaseOpened = now.AddDays(-openedDaysAgo),
            DateCreated = now.AddDays(-openedDaysAgo), CreatedByAppUserId = manager.Id,
        };
        db.Cases.Add(theCase);

        db.CaseTimelineEntries.Add(new CaseTimelineEntry
        {
            Id = Guid.NewGuid(), CaseId = theCase.Id, AuthorAppUserId = manager.Id,
            EntryType = CaseTimelineEntryType.InvestigatorNote,
            EventDateTime = now.AddDays(-openedDaysAgo),
            Title = "Case accepted",
            Body = "<p>Accepted after intake review. Site visit to be scheduled with the client.</p>",
            Visibility = CaseTimelineVisibility.OrgOnly,
            DateCreated = now.AddDays(-openedDaysAgo), CreatedByAppUserId = manager.Id,
        });

        await db.SaveChangesAsync();
        Console.WriteLine($"[RosterSeeder] Created case '{caseTitle}' for {client.DisplayName}.");
        return theCase;
    }

    /// <summary>
    /// A submitted request offered to several groups at once, with a photo attached — the
    /// request-review flow's demo data: every candidate group can open the materials, and the
    /// first to accept wins.
    /// </summary>
    private static async Task ContestedRequestAsync(
        BenDataContext db, IReadOnlyList<Organization> orgs, AppUser client, Guid requestId,
        string street, string city, string state, string zip,
        ClientGender gender, int birthYear, string body, DateTime now)
    {
        if (await db.ClientRequests.AnyAsync(r => r.Id == requestId)) return;

        db.ClientRequests.Add(new ClientRequest
        {
            Id = requestId, AppUserId = client.Id,
            Status = ClientRequestStatus.Submitted,
            StreetAddress1 = street, City = city, State = state, ZipCode = zip, Country = "US",
            Gender = gender, BirthYear = birthYear,
            Description = body,
            DateCreated = now.AddDays(-2), CreatedByAppUserId = client.Id,
        });
        foreach (var org in orgs)
            db.ClientRequestOrganizations.Add(new ClientRequestOrganization
            {
                ClientRequestId = requestId, OrganizationId = org.Id,
                Status = ClientOrgRequestStatus.Pending,
                DateApplied = now.AddDays(-2),
                DateCreated = now.AddDays(-2), CreatedByAppUserId = client.Id,
            });

        // A real (tiny) JPEG in the legacy FileData column, which the download path still
        // honours — no file on disk to lose between environments.
        var photoId = new Guid("51000004-0000-0000-0000-00000000f001");
        db.UploadFiles.Add(new UploadFile
        {
            Id = photoId, AppUserId = client.Id,
            // The fixed case-evidence type CaseFileController uses — a client's request photo is
            // exactly prospective case evidence.
            UploadFileTypeId = new Guid("20000000-0000-0000-0000-000000000001"),
            FileName = "attic-hatch.jpg", ContentType = "image/jpeg",
            FileData = TinyJpeg, FileSize = TinyJpeg.Length,
            DateCreated = now.AddDays(-2), CreatedByAppUserId = client.Id,
        });
        db.ClientRequestFiles.Add(new ClientRequestFile
        {
            ClientRequestId = requestId, UploadFileId = photoId,
            DateCreated = now.AddDays(-2), CreatedByAppUserId = client.Id,
        });

        await db.SaveChangesAsync();
        Console.WriteLine($"[RosterSeeder] Created contested request from {client.DisplayName} to {orgs.Count} groups.");
    }

    /// <summary>A 1×1 grey JPEG — the smallest honest image the preview pipeline will render.</summary>
    private static readonly byte[] TinyJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0a"
      + "HBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAABAAAAAAAA"
      + "AAAAAAAAAAAAC//EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AVN//2Q==");

    /// <summary>A submitted request sitting unanswered in one group's queue.</summary>
    private static async Task PendingRequestAsync(
        BenDataContext db, Organization org, AppUser client, Guid requestId,
        string street, string city, string state, string zip,
        ClientGender gender, int birthYear, string body, DateTime now)
    {
        if (await db.ClientRequests.AnyAsync(r => r.Id == requestId)) return;

        db.ClientRequests.Add(new ClientRequest
        {
            Id = requestId, AppUserId = client.Id,
            Status = ClientRequestStatus.Submitted,
            StreetAddress1 = street, City = city, State = state, ZipCode = zip, Country = "US",
            Gender = gender, BirthYear = birthYear,
            Description = body,
            DateCreated = now.AddDays(-4), CreatedByAppUserId = client.Id,
        });
        db.ClientRequestOrganizations.Add(new ClientRequestOrganization
        {
            ClientRequestId = requestId, OrganizationId = org.Id,
            Status = ClientOrgRequestStatus.Pending,
            DateApplied = now.AddDays(-4),
            DateCreated = now.AddDays(-4), CreatedByAppUserId = client.Id,
        });

        await db.SaveChangesAsync();
        Console.WriteLine($"[RosterSeeder] Created pending request from {client.DisplayName} to {org.Name}.");
    }

    private static async Task InvestigationAsync(
        BenDataContext db, Case theCase, Guid id, string title, string description, string location,
        DateTime start, int hours, InvestigationStatus status, AppUser creator, DateTime now,
        string? notes,
        params (AppUser Person, string Role, RsvpStatus Rsvp, bool? DidAttend)[] attendees)
    {
        if (await db.Investigations.AnyAsync(i => i.Id == id)) return;

        db.Investigations.Add(new Investigation
        {
            // OrganizationId is a direct FK, not derived through the case — an investigation can
            // exist with no case at all (a group visiting a public place), so the org is its own
            // required column and forgetting it is an FK error at save, not a compile error.
            Id = id, OrganizationId = theCase.OrganizationId, CaseId = theCase.Id,
            Title = title, Description = description, Location = location,
            ScheduledDateTime = start, EndDateTime = start.AddHours(hours),
            Status = status, Notes = notes,
            DateCreated = now, CreatedByAppUserId = creator.Id,
        });

        foreach (var (person, role, rsvp, didAttend) in attendees)
        {
            db.InvestigationAttendees.Add(new InvestigationAttendee
            {
                Id = Guid.NewGuid(), InvestigationId = id, AppUserId = person.Id,
                AssignedRole = role, IsLead = person.Id == creator.Id,
                Rsvp = rsvp, DidAttend = didAttend,
                DateCreated = now, CreatedByAppUserId = creator.Id,
            });
        }

        await db.SaveChangesAsync();
        Console.WriteLine($"[RosterSeeder] Created investigation '{title}' with {attendees.Length} attendees.");
    }

    // ── Equipment helpers ─────────────────────────────────────────────────────
    // Same find-or-create shape as DevelopmentDataSeeder's; duplicated rather than made public
    // there because the two seeders should stay independently deletable.

    private static async Task<EquipmentBrand> BrandAsync(
        BenDataContext db, string name, Guid ownerId, DateTime now)
    {
        var existing = await db.EquipmentBrands.FirstOrDefaultAsync(b => b.Name == name);
        if (existing is not null) return existing;

        var brand = new EquipmentBrand
        {
            Id = Guid.NewGuid(), Name = name, IsApproved = true,
            ApprovedByAppUserId = ownerId, DateApproved = now,
            DateCreated = now, CreatedByAppUserId = ownerId,
        };
        db.EquipmentBrands.Add(brand);
        await db.SaveChangesAsync();
        await Services.EquipmentCatalogSlugs.AssignAsync(db, brand, default);
        await db.SaveChangesAsync();
        return brand;
    }

    private static async Task<EquipmentModel?> ModelAsync(
        BenDataContext db, EquipmentBrand brand, string categoryName, string name, Guid ownerId, DateTime now)
    {
        var category = await db.EquipmentCategories.FirstOrDefaultAsync(c => c.Name == categoryName);
        if (category is null) return null;

        var existing = await db.EquipmentModels
            .FirstOrDefaultAsync(m => m.EquipmentBrandId == brand.Id && m.Name == name);
        if (existing is not null) return existing;

        var model = new EquipmentModel
        {
            Id = Guid.NewGuid(), EquipmentBrandId = brand.Id, EquipmentCategoryId = category.Id,
            Name = name, IsApproved = true,
            ApprovedByAppUserId = ownerId, DateApproved = now,
            DateCreated = now, CreatedByAppUserId = ownerId,
        };
        db.EquipmentModels.Add(model);
        await db.SaveChangesAsync();
        await Services.EquipmentCatalogSlugs.AssignAsync(db, model, default);
        await db.SaveChangesAsync();
        return model;
    }

    private static async Task<EquipmentItem> ItemAsync(
        BenDataContext db, string displayName, AppUser owner, EquipmentModel model,
        EquipmentLoanAudience loanable, bool inCatalog, string notes, DateTime now)
    {
        var existing = await db.EquipmentItems.FirstOrDefaultAsync(
            i => i.OwnerAppUserId == owner.Id && i.DisplayName == displayName);
        if (existing is not null) return existing;

        var item = new EquipmentItem
        {
            Id = Guid.NewGuid(), OwnerAppUserId = owner.Id, EquipmentModelId = model.Id,
            DisplayName = displayName, Notes = notes,
            LoanAudience = loanable, IncludeInGlobalCatalog = inCatalog,
            AcquisitionDate = now.AddMonths(-8),
            DateCreated = now, CreatedByAppUserId = owner.Id,
        };
        db.EquipmentItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    private static async Task ShareAsync(
        BenDataContext db, EquipmentItem item, Organization org, Guid byUserId, DateTime now)
    {
        if (await db.EquipmentItemShares.AnyAsync(
                s => s.EquipmentItemId == item.Id && s.OrganizationId == org.Id))
            return;

        db.EquipmentItemShares.Add(new EquipmentItemShare
        {
            Id = Guid.NewGuid(), EquipmentItemId = item.Id, OrganizationId = org.Id,
            DateCreated = now, CreatedByAppUserId = byUserId,
        });
        await db.SaveChangesAsync();
    }


    /// <summary>
    /// Gives every seeded plain member their group's Investigator Role — the assignment a real
    /// owner would make.
    /// </summary>
    /// <remarks>
    /// <para>Needed since IH-03 step 4 ended the grandfathering (Ben, 2026-08-26). Before that,
    /// every member could read cases without holding anything, so the seeded world worked by
    /// accident. Now a member with no role sees no case surfaces — which is CORRECT enforcement
    /// aimed at an UNCONFIGURED world: the seeders built a group whose owner never assigned
    /// anyone a role, and the whole e2e suite failed honestly against it. The fix belongs here,
    /// in the seeded data, not in the enforcement and not in the tests.</para>
    ///
    /// <para>Dev-only by construction: this file already runs behind <c>SeedData:DevData:Enabled</c>.
    /// Owners and Administrators are skipped — they bypass grants. Idempotent: an existing role
    /// membership is left alone. The role is matched by name within each org, and an org whose
    /// owner deleted its Investigator Role is skipped rather than "repaired" — deletions are
    /// decisions, even seeded ones.</para>
    /// </remarks>
    private static async Task AssignInvestigatorRolesAsync(
        IDbContextFactory<BenDataContext> dbFactory, DateTime now)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        // Only the fake people — a real member in a shared dev database keeps whatever their
        // group's owner actually decided.
        var seededUserIds = await db.Users
            .Where(u => u.Email != null && u.Email.EndsWith("@benco.dev"))
            .Select(u => u.Id)
            .ToListAsync();

        // Backfill DateOnboarded for fake people created before the stamp existed at creation:
        // an unstamped account is redirected to /onboarding on every navigation, which is how a
        // database rebuild silently hijacked whole e2e fixtures. Idempotent by the null check.
        var unonboarded = await db.Users
            .Where(u => seededUserIds.Contains(u.Id) && u.DateOnboarded == null)
            .ToListAsync();
        foreach (var u in unonboarded) u.DateOnboarded = now;
        if (unonboarded.Count > 0)
        {
            await db.SaveChangesAsync();
            Console.WriteLine($"[RosterSeeder] Stamped DateOnboarded for {unonboarded.Count} seeded account(s).");
        }

        var memberships = await db.OrganizationUserMemberships
            .Where(m => m.IsActive
                     && seededUserIds.Contains(m.AppUserId)
                     // Owners and admins bypass grants; Viewers are DEFINED by holding none —
                     // Victor Reyes exists precisely so a role-less Viewer seat is always there,
                     // and the first version of this filter handed him the Investigator Role,
                     // which put case banners in front of the one seat that must never see them
                     // (the action-needed banner e2e caught it).
                     && m.Role != OrganizationMemberRole.Owner
                     && m.Role != OrganizationMemberRole.Administrator
                     && m.Role != OrganizationMemberRole.Viewer)
            .Select(m => new { m.Id, m.OrganizationId })
            .ToListAsync();

        var orgIds = memberships.Select(m => m.OrganizationId).Distinct().ToList();

        // The assignment is recorded as the org owner's act, which is who it models.
        var ownerByOrg = await db.Organizations
            .Where(o => orgIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.CreatedByAppUserId);

        var roles = await db.OrganizationRoles
            .Where(r => orgIds.Contains(r.OrganizationId) && r.Name == "Investigator Role" && r.IsActive)
            .Select(r => new { r.Id, r.OrganizationId })
            .ToListAsync();
        var roleByOrg = roles.ToDictionary(r => r.OrganizationId, r => r.Id);

        // Dev orgs seeded before the Investigator Role joined the defaults hold the original
        // seven, and the org-level backfill deliberately skips any org that already has roles.
        // Here — dev data only — the role is created where it is missing, modelling the owner
        // adding it. The name is the marker: an org where the owner RENAMED or deleted theirs is
        // an org this cannot distinguish, and in seeded data that trade is fine.
        foreach (var orgId in orgIds.Where(id => !roleByOrg.ContainsKey(id)))
        {
            var role = new OrganizationRole
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, Name = "Investigator Role",
                Description = "Reads the group's cases and investigations. Assign it to the members who should see them.",
                IsActive = true, SortOrder = 0,
                DateCreated = now, CreatedByAppUserId = ownerByOrg[orgId],
            };
            db.OrganizationRoles.Add(role);
            foreach (var table in new[] { OrganizationSecurityTable.Case, OrganizationSecurityTable.Investigation })
                db.OrganizationRolePermissions.Add(new OrganizationRolePermission
                {
                    Id = Guid.NewGuid(), OrganizationRoleId = role.Id,
                    TableName = table, Actions = OrganizationSecurityAction.Read,
                    DateCreated = now, CreatedByAppUserId = ownerByOrg[orgId],
                });
            roleByOrg[orgId] = role.Id;
        }

        var roleIds = roleByOrg.Values.ToList();
        var already = await db.OrganizationRoleMemberships
            .Where(rm => roleIds.Contains(rm.OrganizationRoleId))
            .Select(rm => rm.OrganizationUserMembershipId)
            .ToHashSetAsync();

        var added = 0;
        foreach (var m in memberships)
        {
            if (!roleByOrg.TryGetValue(m.OrganizationId, out var roleId)) continue;
            if (already.Contains(m.Id)) continue;
            db.OrganizationRoleMemberships.Add(new OrganizationRoleMembership
            {
                Id = Guid.NewGuid(),
                OrganizationRoleId = roleId,
                OrganizationUserMembershipId = m.Id,
                DateCreated = now,
                CreatedByAppUserId = ownerByOrg[m.OrganizationId],
            });
            added++;
        }
        if (added > 0)
        {
            await db.SaveChangesAsync();
            Console.WriteLine($"[RosterSeeder] Assigned Investigator Role to {added} seeded member(s).");
        }
    }

}
