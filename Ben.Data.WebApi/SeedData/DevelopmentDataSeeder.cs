using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.SeedData;

/// <summary>
/// Seeds rich development / demo data for front-end testing.
/// Creates multiple organizations with public cases, timeline entries,
/// investigations, and community votes so all home-page and public-discovery
/// UI surfaces have meaningful data without any manual setup.
/// </summary>
/// <remarks>
/// <para>
/// Guarded by <c>SeedData:DevData:Enabled</c> in <c>appsettings.Development.json</c>.
/// Setting this to <c>false</c> (or omitting it) is safe — the seeder exits
/// immediately and leaves the database unchanged.
/// </para>
/// <para>
/// Idempotent: each run checks for existing records by a stable identifier
/// (e.g. <c>UrlName</c>, <c>CaseYear+OrgCaseNumber</c>) before inserting.
/// Running it multiple times does not create duplicates.
/// </para>
/// <para>
/// Depends on: <see cref="SuperAdminSeeder"/> and <see cref="OrganizationSeeder"/>
/// having already run (needs the haveben / BenCo users to exist).
/// </para>
/// <para>
/// What gets seeded:
/// <list type="bullet">
///   <item><description>Two additional organizations with org addresses.</description></item>
///   <item><description>Five public cases across three cities (supports map clustering).</description></item>
///   <item><description>Timeline entries: client reports, investigator notes, and research notes.</description></item>
///   <item><description>One scheduled investigation with attendees.</description></item>
///   <item><description>Case votes from multiple users (confirms/disputes/inconclusive).</description></item>
///   <item><description>A sample Draft client request from Daniel Park.</description></item>
///   <item><description>An Assigned client request + Accepted case for Daniel Park (BenCo/tgh, case manager=Sarah) — enables /my-cases dashboard testing.</description></item>
/// </list>
/// </para>
/// </remarks>
internal static class DevelopmentDataSeeder
{
    internal static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        var enabled = config.GetValue<bool>("SeedData:DevData:Enabled");
        if (!enabled) return;

        var ownerEmail = config["SeedData:SuperAdmin:Email"];
        if (string.IsNullOrWhiteSpace(ownerEmail)) return;

        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var dbFactory   = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BenDataContext>>();

        // ── Resolve existing users (created by SuperAdminSeeder + OrganizationSeeder) ──
        var owner  = await userManager.FindByEmailAsync(ownerEmail);
        if (owner is null)
        {
            Console.WriteLine("[DevDataSeeder] SuperAdmin not found — skipping.");
            return;
        }

        var sarah  = await userManager.FindByEmailAsync("sarah.mitchell@benco.dev");
        var james  = await userManager.FindByEmailAsync("james.thornton@benco.dev");
        var emma   = await userManager.FindByEmailAsync("emma.rodriguez@benco.dev");
        var daniel = await userManager.FindByEmailAsync("daniel.park@benco.dev");

        if (sarah is null || james is null || emma is null || daniel is null)
        {
            Console.WriteLine("[DevDataSeeder] BenCo seed users not found — run OrganizationSeeder first.");
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;

        // ── Address types ─────────────────────────────────────────────────────
        var addrType = await db.OrganizationAddressTypes.FirstOrDefaultAsync()
                    ?? new OrganizationAddressType
                    {
                        Id = Guid.NewGuid(), Name = "Headquarters",
                        IsActive = true, IsPublic = true, SortOrder = 1,
                        DateCreated = now, CreatedByAppUserId = owner.Id,
                    };
        if (!await db.OrganizationAddressTypes.AnyAsync(t => t.Name == "Headquarters"))
        {
            db.OrganizationAddressTypes.Add(addrType);
            await db.SaveChangesAsync();
        }

        addrType = await db.OrganizationAddressTypes.FirstAsync(t => t.Name == "Headquarters");

        // ── Org 1: Tennessee Ghost Hunters ────────────────────────────────────
        var tgh = await db.Organizations.FirstOrDefaultAsync(o => o.UrlName == "tgh");
        if (tgh is null)
        {
            tgh = new Organization
            {
                Id = Guid.NewGuid(), Name = "Tennessee Ghost Hunters", UrlName = "tgh",
                IsAcceptingClients = true, IsAcceptingApplications = true,
                DateCreated = now, CreatedByAppUserId = owner.Id,
            };
            db.Organizations.Add(tgh);
            await db.SaveChangesAsync();
            Console.WriteLine("[DevDataSeeder] Created organization: Tennessee Ghost Hunters");
        }

        await SeedOrgMembersAsync(db, tgh, owner, sarah, james, now);
        await SeedOrgAddressAsync(db, tgh, addrType, "1200 Church St", "Nashville", "TN", "37203", "US", 36.1627m, -86.7816m, owner.Id, now);

        // ── Org 2: Nashville Paranormal Society ───────────────────────────────
        var nps = await db.Organizations.FirstOrDefaultAsync(o => o.UrlName == "nps");
        if (nps is null)
        {
            nps = new Organization
            {
                Id = Guid.NewGuid(), Name = "Nashville Paranormal Society", UrlName = "nps",
                IsAcceptingClients = true, IsAcceptingApplications = false,
                DateCreated = now, CreatedByAppUserId = owner.Id,
            };
            db.Organizations.Add(nps);
            await db.SaveChangesAsync();
            Console.WriteLine("[DevDataSeeder] Created organization: Nashville Paranormal Society");
        }

        await SeedOrgMembersAsync(db, nps, owner, emma, null, now);
        await SeedOrgAddressAsync(db, nps, addrType, "500 Commerce St", "Nashville", "TN", "37203", "US", 36.1651m, -86.7785m, owner.Id, now);

        // ── Cases ──────────────────────────────────────────────────────────────
        // Five cases spread across Nashville TN, Springfield TN, and Memphis TN.
        // This gives the home-page map both a cluster (two Nashville cases) and
        // isolated single-case markers (Springfield, Memphis).

        await SeedCaseAsync(db, tgh,
            year: 2026, number: 1,
            title: "Abandoned Springfield Farmhouse",
            description: "<p>Long-time residents report unexplained activity at an 1890s farmhouse on the outskirts of Springfield. Persistent knocking, temperature anomalies, and apparitions near the barn have been documented over three generations of occupants.</p>",
            street: "Old Hwy 41", city: "Springfield", state: "TN", zip: "37172", country: "US",
            status: CaseStatus.Haunted, isPublic: true, pseudonym: "The Hargrove Family",
            opened: new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
            creator: owner,
            confirms: 5, disputes: 1, inconclusive: 2, voters: [sarah, james, emma, daniel, owner],
            now: now);

        await SeedCaseAsync(db, tgh,
            year: 2026, number: 2,
            title: "Bell Witch Cave — Annual Survey",
            description: "<p>Annual survey of paranormal activity at the historic Bell Witch Cave site. Audio anomalies and shadow figures reported by multiple independent visitors this season.</p>",
            street: "430 Keysburg Rd", city: "Adams", state: "TN", zip: "37010", country: "US",
            status: CaseStatus.Public, isPublic: true, pseudonym: null,
            opened: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            creator: sarah,
            confirms: 8, disputes: 0, inconclusive: 3, voters: [owner, james, emma, daniel],
            now: now);

        await SeedCaseAsync(db, nps,
            year: 2026, number: 1,
            title: "The Old Ryman Auditorium Back Stage",
            description: "<p>Multiple touring musicians and staff report unexplained shadows and cold spots in the backstage area of the historic auditorium. Residual energy from past performers is suspected.</p>",
            street: "116 5th Ave N", city: "Nashville", state: "TN", zip: "37219", country: "US",
            status: CaseStatus.Public, isPublic: true, pseudonym: "Anonymous Venue Staff",
            opened: new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc),
            creator: owner,
            confirms: 3, disputes: 2, inconclusive: 1, voters: [sarah, emma],
            now: now);

        await SeedCaseAsync(db, nps,
            year: 2026, number: 2,
            title: "The Hermitage Hotel — Presidential Suite",
            description: "<p>Hotel guests in the historic presidential suite on the top floor have consistently reported auditory phenomena — music from a previous era, footsteps, and faint conversation — particularly between 2 and 4 AM.</p>",
            street: "231 6th Ave N", city: "Nashville", state: "TN", zip: "37219", country: "US",
            status: CaseStatus.Haunted, isPublic: true, pseudonym: "Hotel Guest #2024-7",
            opened: new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc),
            creator: emma,
            confirms: 12, disputes: 0, inconclusive: 4, voters: [owner, sarah, james, daniel],
            now: now);

        // BenCo org — use the existing BenCo org if present
        var benco = await db.Organizations.FirstOrDefaultAsync(o => o.UrlName == "benco");
        if (benco is not null)
        {
            await SeedCaseAsync(db, benco,
                year: 2026, number: 1,
                title: "Historic Shelby Street Bridge",
                description: "<p>Civil War-era bridge with a long history of reported apparitions. Several visitors and joggers have filed independent reports of a uniformed figure near the center span, especially on overcast evenings.</p>",
                street: "Shelby Street Bridge", city: "Nashville", state: "TN", zip: "37206", country: "US",
                status: CaseStatus.Public, isPublic: true, pseudonym: null,
                opened: new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc),
                creator: owner,
                confirms: 4, disputes: 3, inconclusive: 2, voters: [sarah, emma, daniel],
                now: now);
        }

        // ── Investigation for case 1 ───────────────────────────────────────────
        var tghCase1 = await db.Cases.FirstOrDefaultAsync(
            c => c.OrganizationId == tgh.Id && c.CaseYear == 2026 && c.OrgCaseNumber == 1);
        if (tghCase1 is not null && !await db.Investigations.AnyAsync(i => i.CaseId == tghCase1.Id))
        {
            var inv = new Investigation
            {
                Id = Guid.NewGuid(), CaseId = tghCase1.Id,
                Title = "Initial Night Investigation",
                Description = "Baseline EMF sweep and audio recording session. Focus on the main barn and east wing of the farmhouse.",
                Location = "Springfield Farmhouse — Barn + East Wing",
                ScheduledDateTime = new DateTime(2026, 3, 22, 20, 0, 0, DateTimeKind.Utc),
                EndDateTime       = new DateTime(2026, 3, 23, 2, 0, 0, DateTimeKind.Utc),
                Status = InvestigationStatus.Completed,
                Notes = "<p>Three distinct knocking sequences recorded in the barn. EMF spiked to 4.2 mG near the grain silo at 11:47 PM. Temperature dropped 8°F in the east wing hallway with no apparent source. Team recommends a follow-up visit.</p>",
                DateCreated = now, CreatedByAppUserId = owner.Id,
            };
            db.Investigations.Add(inv);

            db.InvestigationAttendees.Add(new InvestigationAttendee
            {
                Id = Guid.NewGuid(), InvestigationId = inv.Id, AppUserId = owner.Id,
                AssignedRole = "Lead Investigator", DidAttend = true,
                DateCreated = now, CreatedByAppUserId = owner.Id,
            });
            db.InvestigationAttendees.Add(new InvestigationAttendee
            {
                Id = Guid.NewGuid(), InvestigationId = inv.Id, AppUserId = sarah.Id,
                AssignedRole = "Audio Technician", DidAttend = true,
                DateCreated = now, CreatedByAppUserId = owner.Id,
            });
            db.InvestigationAttendees.Add(new InvestigationAttendee
            {
                Id = Guid.NewGuid(), InvestigationId = inv.Id, AppUserId = james.Id,
                AssignedRole = "EMF Specialist", DidAttend = true,
                DateCreated = now, CreatedByAppUserId = owner.Id,
            });

            await db.SaveChangesAsync();
            Console.WriteLine("[DevDataSeeder] Created investigation for Springfield Farmhouse case.");
        }

        // ── Client request from Daniel Park (Draft) ───────────────────────────
        if (!await db.ClientRequests.AnyAsync(cr => cr.AppUserId == daniel.Id))
        {
            db.ClientRequests.Add(new ClientRequest
            {
                Id = Guid.NewGuid(), AppUserId = daniel.Id,
                Status = ClientRequestStatus.Draft,
                StreetAddress1 = "4512 Belmont Blvd", City = "Nashville",
                State = "TN", ZipCode = "37215", Country = "US",
                Gender = ClientGender.Male, BirthYear = 1988,
                Description = "Multiple residents in our home have heard unexplained footsteps on the second floor after midnight. Objects have been found moved in the morning. This started approximately six months ago following a renovation.",
                DateCreated = now, CreatedByAppUserId = daniel.Id,
            });
            await db.SaveChangesAsync();
            Console.WriteLine("[DevDataSeeder] Created client request (Draft) for Daniel Park.");
        }

        // ── Accepted case for Daniel Park — enables /my-cases testing ─────────
        // Creates a separate Assigned request + Accepted case so the client dashboard has data.
        var danielAcceptedReq = await db.ClientRequests
            .FirstOrDefaultAsync(cr => cr.AppUserId == daniel.Id && cr.Status == ClientRequestStatus.Assigned);
        if (danielAcceptedReq is null && tgh is not null)
        {
            danielAcceptedReq = new ClientRequest
            {
                Id = Guid.NewGuid(), AppUserId = daniel.Id,
                Status = ClientRequestStatus.Assigned,
                StreetAddress1 = "4512 Belmont Blvd", City = "Nashville",
                State = "TN", ZipCode = "37215", Country = "US",
                Latitude = 36.1043m, Longitude = -86.7930m,
                Gender = ClientGender.Male, BirthYear = 1988,
                Description = "<p>Persistent unexplained activity at a residential property. Footsteps, moved objects, and temperature anomalies reported over six months.</p>",
                DateCreated = now.AddDays(-30), CreatedByAppUserId = daniel.Id,
            };
            db.ClientRequests.Add(danielAcceptedReq);
            await db.SaveChangesAsync();

            // Create the accepted case linked to Daniel's request
            var nextNum = (await db.Cases
                .Where(c => c.OrganizationId == tgh.Id && c.CaseYear == 2026)
                .MaxAsync(c => (int?)c.OrgCaseNumber) ?? 2) + 1;

            var danielCase = new Case
            {
                Id = Guid.NewGuid(), OrganizationId = tgh.Id,
                ClientRequestId = danielAcceptedReq.Id,
                CaseManagerAppUserId = sarah.Id,
                CaseYear = 2026, OrgCaseNumber = nextNum,
                Title = "Park Residence, Nashville TN",
                Description = "<p>Client reports persistent activity over six months: footsteps on the second floor after midnight and objects found displaced. Initial evidence review underway.</p>",
                StreetAddress1 = "4512 Belmont Blvd", City = "Nashville",
                State = "TN", ZipCode = "37215", Country = "US",
                Latitude = 36.1043m, Longitude = -86.7930m,
                Status = CaseStatus.Accepted, IsPublic = false,
                PublicPseudonym = "The Park Family",
                DateCaseOpened = now.AddDays(-25),
                DateCreated = now.AddDays(-25), CreatedByAppUserId = sarah.Id,
            };
            db.Cases.Add(danielCase);

            // Client report entry from Daniel
            db.CaseTimelineEntries.Add(new CaseTimelineEntry
            {
                Id = Guid.NewGuid(), CaseId = danielCase.Id, AuthorAppUserId = daniel.Id,
                EntryType = CaseTimelineEntryType.ClientReport,
                EventDateTime = now.AddDays(-28),
                Title = "Initial Occurrence — Footsteps",
                Body = "<p>Heard distinct footsteps on the second floor at approximately 2:15 AM. No one was upstairs. Lasted about 90 seconds then stopped. Second occurrence this week.</p>",
                IsPublic = false,
                DateCreated = now.AddDays(-28), CreatedByAppUserId = daniel.Id,
            });
            db.CaseTimelineEntries.Add(new CaseTimelineEntry
            {
                Id = Guid.NewGuid(), CaseId = danielCase.Id, AuthorAppUserId = sarah.Id,
                EntryType = CaseTimelineEntryType.InvestigatorNote,
                EventDateTime = now.AddDays(-25),
                Title = "Case Accepted — Initial Review",
                Body = "<p>Case accepted following review of client submission. Will schedule initial contact and site assessment.</p>",
                IsPublic = false,
                DateCreated = now.AddDays(-25), CreatedByAppUserId = sarah.Id,
            });

            // Upcoming investigation
            db.Investigations.Add(new Investigation
            {
                Id = Guid.NewGuid(), CaseId = danielCase.Id,
                Title = "Initial Site Assessment",
                Description = "First visit to the property. EMF baseline, audio placement, walkthrough with client.",
                Location = "Park Residence — Full property",
                ScheduledDateTime = now.AddDays(5),
                EndDateTime       = now.AddDays(5).AddHours(4),
                Status = InvestigationStatus.Scheduled,
                DateCreated = now.AddDays(-20), CreatedByAppUserId = sarah.Id,
            });

            await db.SaveChangesAsync();

            // Seed a short message thread so the Messages tab has content
            if (!await db.CaseMessages.AnyAsync(m => m.CaseId == danielCase.Id))
            {
                db.CaseMessages.AddRange(
                    new CaseMessage
                    {
                        Id = new Guid("11000001-0000-0000-0000-000000000001"),
                        CaseId = danielCase.Id, AuthorAppUserId = sarah.Id,
                        Body = "Hi Daniel, we've reviewed your submission and scheduled an initial site assessment for next week. We'll reach out if we need anything before then.",
                        SenderSide = Ben.Data.Common.Enums.CaseMessageSide.Organization,
                        IsReadByClient = false, IsReadByOrg = true,
                        DateCreated = now.AddDays(-3), CreatedByAppUserId = sarah.Id,
                    },
                    new CaseMessage
                    {
                        Id = new Guid("11000001-0000-0000-0000-000000000002"),
                        CaseId = danielCase.Id, AuthorAppUserId = daniel.Id,
                        Body = "Thank you! The activity has been a bit more frequent this week — mainly in the upstairs hallway around 2am. Should I log these somewhere?",
                        SenderSide = Ben.Data.Common.Enums.CaseMessageSide.Client,
                        IsReadByClient = true, IsReadByOrg = true,
                        DateCreated = now.AddDays(-2), CreatedByAppUserId = daniel.Id,
                    },
                    new CaseMessage
                    {
                        Id = new Guid("11000001-0000-0000-0000-000000000003"),
                        CaseId = danielCase.Id, AuthorAppUserId = sarah.Id,
                        Body = "Yes! Please use the 'Log Occurrence' button on your case page. Include the time and a brief description. That helps us know where to focus our equipment.",
                        SenderSide = Ben.Data.Common.Enums.CaseMessageSide.Organization,
                        IsReadByClient = false, IsReadByOrg = true,
                        DateCreated = now.AddDays(-1), CreatedByAppUserId = sarah.Id,
                    });
                await db.SaveChangesAsync();
            }

            Console.WriteLine("[DevDataSeeder] Created accepted case for Daniel Park (client dashboard test data).");

            // Seed a published report so the client-side report view has content
            if (!await db.CaseReports.AnyAsync(r => r.CaseId == danielCase.Id))
            {
                var report = new CaseReport
                {
                    Id                   = new Guid("30000001-0000-0000-0000-000000000001"),
                    CaseId               = danielCase.Id,
                    Title                = "Initial Assessment — Park Residence",
                    Summary              = "Team conducted a baseline sweep of the property on 2026-08-07. Activity was primarily concentrated in the upstairs hallway.",
                    Conclusion           = "Evidence is consistent with a Type 2 residual haunting. Further investigations are recommended to capture additional audio and visual evidence.",
                    Status               = Ben.Data.Common.Enums.CaseReportStatus.Published,
                    PublishedAt          = now.AddDays(-1),
                    PublishedByAppUserId = sarah.Id,
                    ExpectedDeliveryDate = now.AddDays(14),
                    DateCreated          = now.AddDays(-3),
                    CreatedByAppUserId   = sarah.Id,
                };
                db.CaseReports.Add(report);
                await db.SaveChangesAsync();
            }
        }

        Console.WriteLine("[DevDataSeeder] Development seed data applied successfully.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a public <see cref="Case"/> with full timeline entries and community votes.
    /// Skips creation if a case with the same org/year/number already exists.
    /// </summary>
    private static async Task SeedCaseAsync(
        BenDataContext db,
        Organization org,
        int year, int number,
        string title, string? description,
        string street, string city, string state, string zip, string country,
        CaseStatus status, bool isPublic, string? pseudonym,
        DateTime opened,
        AppUser creator,
        int confirms, int disputes, int inconclusive,
        AppUser[] voters,
        DateTime now)
    {
        if (await db.Cases.AnyAsync(c => c.OrganizationId == org.Id && c.CaseYear == year && c.OrgCaseNumber == number))
            return;

        var caseEntity = new Case
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id,
            CaseYear = year, OrgCaseNumber = number,
            Title = title, Description = description,
            StreetAddress1 = street, City = city, State = state,
            ZipCode = zip, Country = country,
            Status = status, IsPublic = isPublic,
            PublicPseudonym = pseudonym,
            DateCaseOpened = opened,
            DateCreated = now, CreatedByAppUserId = creator.Id,
        };
        db.Cases.Add(caseEntity);
        await db.SaveChangesAsync();

        // Timeline entries
        db.CaseTimelineEntries.Add(new CaseTimelineEntry
        {
            Id = Guid.NewGuid(), CaseId = caseEntity.Id, AuthorAppUserId = creator.Id,
            EntryType = CaseTimelineEntryType.ClientReport,
            EventDateTime = opened.AddDays(-7),
            Title = "Initial Client Report",
            Body = $"<p>Client contacted us regarding unusual activity at their property in {city}, {state}.</p>",
            IsPublic = true,
            DateCreated = now, CreatedByAppUserId = creator.Id,
        });
        db.CaseTimelineEntries.Add(new CaseTimelineEntry
        {
            Id = Guid.NewGuid(), CaseId = caseEntity.Id, AuthorAppUserId = creator.Id,
            EntryType = CaseTimelineEntryType.InvestigatorNote,
            EventDateTime = opened,
            Title = "Case Opened",
            Body = "<p>Case accepted for investigation. Preliminary review of client statement complete. Site visit scheduled.</p>",
            IsPublic = true,
            DateCreated = now, CreatedByAppUserId = creator.Id,
        });
        if (status == CaseStatus.Haunted)
        {
            db.CaseTimelineEntries.Add(new CaseTimelineEntry
            {
                Id = Guid.NewGuid(), CaseId = caseEntity.Id, AuthorAppUserId = creator.Id,
                EntryType = CaseTimelineEntryType.ResearchNote,
                EventDateTime = opened.AddDays(14),
                Title = "Historical Research — Property Records",
                Body = "<p>Property records reveal multiple previous owners who each reported similar activity. Oldest documented account dates to 1947.</p>",
                IsPublic = true,
                DateCreated = now, CreatedByAppUserId = creator.Id,
            });
        }
        await db.SaveChangesAsync();

        // Community case votes (spread across voters using modulo for variety)
        var voteTypes = new[] { EvidenceVoteType.Confirms, EvidenceVoteType.Disputes, EvidenceVoteType.Inconclusive };
        var totalVotes = confirms + disputes + inconclusive;
        var allVoters  = voters.Take(totalVotes).ToArray();

        for (int i = 0; i < allVoters.Length; i++)
        {
            EvidenceVoteType vt;
            if (i < confirms)           vt = EvidenceVoteType.Confirms;
            else if (i < confirms + disputes) vt = EvidenceVoteType.Disputes;
            else                        vt = EvidenceVoteType.Inconclusive;

            db.CaseVotes.Add(new CaseVote
            {
                Id = Guid.NewGuid(), CaseId = caseEntity.Id,
                VoterAppUserId = allVoters[i].Id,
                VoteType = vt,
                DateVoted = now.AddDays(-i),
            });
        }
        await db.SaveChangesAsync();
        Console.WriteLine($"[DevDataSeeder] Seeded case: {org.Name} #{year}-{number:D3} ({city}, {state})");
    }

    /// <summary>
    /// Ensures the owner is the org Owner and optionally adds an Admin and a Member membership.
    /// Safe to call on existing orgs — skips any memberships that already exist.
    /// </summary>
    private static async Task SeedOrgMembersAsync(
        BenDataContext db, Organization org, AppUser owner,
        AppUser? admin, AppUser? member, DateTime now)
    {
        if (!await db.OrganizationUserMemberships.AnyAsync(
                m => m.OrganizationId == org.Id && m.AppUserId == owner.Id))
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = org.Id, AppUserId = owner.Id,
                Role = OrganizationMemberRole.Owner, IsActive = true,
                DateCreated = now, CreatedByAppUserId = owner.Id,
            });
        }
        if (admin is not null && !await db.OrganizationUserMemberships.AnyAsync(
                m => m.OrganizationId == org.Id && m.AppUserId == admin.Id))
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = org.Id, AppUserId = admin.Id,
                Role = OrganizationMemberRole.Administrator, IsActive = true,
                DateCreated = now, CreatedByAppUserId = owner.Id,
            });
        }
        if (member is not null && !await db.OrganizationUserMemberships.AnyAsync(
                m => m.OrganizationId == org.Id && m.AppUserId == member.Id))
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = org.Id, AppUserId = member.Id,
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = now, CreatedByAppUserId = owner.Id,
            });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Ensures the organization has at least one public address.
    /// Skips if an address already exists for the org.
    /// </summary>
    private static async Task SeedOrgAddressAsync(
        BenDataContext db, Organization org, OrganizationAddressType addrType,
        string street, string city, string state, string zip, string country,
        decimal lat, decimal lon, Guid creatorId, DateTime now)
    {
        if (await db.OrganizationAddresses.AnyAsync(a => a.OrganizationId == org.Id))
            return;

        db.OrganizationAddresses.Add(new OrganizationAddress
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id,
            OrganizationAddressTypeId = addrType.Id,
            StreetAddress1 = street, City = city, State = state,
            ZipCode = zip, Country = country,
            Latitude = lat, Longitude = lon,
            Visibility = OrganizationAddressVisibility.Public,
            PublicDisplayMode = OrganizationAddressDisplayMode.MapPinOnly,
            MemberDisplayMode = OrganizationAddressDisplayMode.FullAddressAndMap,
            SortOrder = 1,
            DateCreated = now, CreatedByAppUserId = creatorId,
        });
        await db.SaveChangesAsync();
    }
}
