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
            now: now,
            latitude: 36.5034m, longitude: -86.8791m);

        await SeedCaseAsync(db, tgh,
            year: 2026, number: 2,
            title: "Bell Witch Cave — Annual Survey",
            description: "<p>Annual survey of paranormal activity at the historic Bell Witch Cave site. Audio anomalies and shadow figures reported by multiple independent visitors this season.</p>",
            street: "430 Keysburg Rd", city: "Adams", state: "TN", zip: "37010", country: "US",
            status: CaseStatus.Public, isPublic: true, pseudonym: null,
            opened: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            creator: sarah,
            confirms: 8, disputes: 0, inconclusive: 3, voters: [owner, james, emma, daniel],
            now: now,
            latitude: 36.5726m, longitude: -87.0562m);

        await SeedCaseAsync(db, nps,
            year: 2026, number: 1,
            title: "The Old Ryman Auditorium Back Stage",
            description: "<p>Multiple touring musicians and staff report unexplained shadows and cold spots in the backstage area of the historic auditorium. Residual energy from past performers is suspected.</p>",
            street: "116 5th Ave N", city: "Nashville", state: "TN", zip: "37219", country: "US",
            status: CaseStatus.Public, isPublic: true, pseudonym: "Anonymous Venue Staff",
            opened: new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc),
            creator: owner,
            confirms: 3, disputes: 2, inconclusive: 1, voters: [sarah, emma],
            now: now,
            latitude: 36.1612m, longitude: -86.7775m);

        await SeedCaseAsync(db, nps,
            year: 2026, number: 2,
            title: "The Hermitage Hotel — Presidential Suite",
            description: "<p>Hotel guests in the historic presidential suite on the top floor have consistently reported auditory phenomena — music from a previous era, footsteps, and faint conversation — particularly between 2 and 4 AM.</p>",
            street: "231 6th Ave N", city: "Nashville", state: "TN", zip: "37219", country: "US",
            status: CaseStatus.Haunted, isPublic: true, pseudonym: "Hotel Guest #2024-7",
            opened: new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc),
            creator: emma,
            confirms: 12, disputes: 0, inconclusive: 4, voters: [owner, sarah, james, daniel],
            now: now,
            latitude: 36.1653m, longitude: -86.7823m);

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
                now: now,
                latitude: 36.1043m, longitude: -86.7699m);
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
                // Not "The Park Family" — a pseudonym built from the client's real surname
                // defeats itself, and item 176's leak check now warns on exactly that.
                // (And not "The Belmont Family" either: the case sits on Belmont Blvd.)
                PublicPseudonym = "The Caldwell Family",
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
                Visibility = CaseTimelineVisibility.OrgOnly,
                DateCreated = now.AddDays(-28), CreatedByAppUserId = daniel.Id,
            });
            db.CaseTimelineEntries.Add(new CaseTimelineEntry
            {
                Id = Guid.NewGuid(), CaseId = danielCase.Id, AuthorAppUserId = sarah.Id,
                EntryType = CaseTimelineEntryType.InvestigatorNote,
                EventDateTime = now.AddDays(-25),
                Title = "Case Accepted — Initial Review",
                Body = "<p>Case accepted following review of client submission. Will schedule initial contact and site assessment.</p>",
                Visibility = CaseTimelineVisibility.OrgOnly,
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

        // Both organizations are created above if missing, so this is defensive rather than
        // expected — but the compiler is right that FirstOrDefaultAsync can return null, and a seed
        // that throws takes the whole API startup down with it.
        if (tgh is not null && nps is not null)
            await SeedSharedPlaceAsync(db, tgh, nps, owner, emma, sarah, now);

        if (tgh is not null && nps is not null)
            await SeedLocalDiscoveryAsync(db, tgh, nps, owner, emma, now);

        if (tgh is not null)
            await SeedEquipmentAsync(db, tgh, owner, sarah, james, now);

        // Both the regular user the documents are written for and the owner account, so the
        // editor can be driven by hand from either.
        await SeedVideoEditorMediaAsync(db, sarah, now);
        await SeedVideoEditorMediaAsync(db, owner, now);

        Console.WriteLine("[DevDataSeeder] Development seed data applied successfully.");
    }

    /// <summary>
    /// Four small clips in the media library, so the video editor has something to open.
    /// </summary>
    /// <remarks>
    /// <para>The editor's help documents are mostly pictures of the editor, and an editor with an
    /// empty timeline demonstrates nothing — every screenshot would be of the same grey rectangle.
    /// These are generated files (two camera clips, a room-tone recording and a site photo,
    /// 172 KB in total) that live in <c>SeedData/Media</c> and are loaded as ordinary uploads
    /// belonging to the seeded regular user, which is who the documents are written for.</para>
    ///
    /// <para>Stored as <c>FileData</c> rather than on disk: a seeded row pointing at a storage
    /// path would break the moment the database and the file store disagreed, and these are small
    /// enough that the blob is the simpler half of that trade.</para>
    ///
    /// <para>Typed as Case Evidence because that is what investigation footage is; the media
    /// library filters by content type, not by file type, so the editor lists them either way.</para>
    /// </remarks>
    private static async Task SeedVideoEditorMediaAsync(BenDataContext db, AppUser owner, DateTime now)
    {
        var evidenceType = await db.UploadFileTypes.FirstOrDefaultAsync(
            t => t.Name == UploadFileTypeSeeder.EvidenceFileTypeName);
        if (evidenceType is null)
        {
            Console.WriteLine("[DevDataSeeder] Case Evidence file type missing — skipping demo media.");
            return;
        }

        var mediaRoot = Path.Combine(AppContext.BaseDirectory, "SeedData", "Media");
        if (!Directory.Exists(mediaRoot))
        {
            Console.WriteLine($"[DevDataSeeder] No demo media at {mediaRoot} — skipping.");
            return;
        }

        var files = new (string File, string ContentType, string Description)[]
        {
            ("porch-camera.mp4",   "video/mp4",  "Front porch camera, 8 seconds. Static wide shot."),
            ("hallway-camera.mp4", "video/mp4",  "Upstairs hallway camera, 6 seconds."),
            ("basement-evp.m4a",   "audio/mp4",  "Basement EVP session — room tone with a low hum."),
            ("site-photo.jpg",     "image/jpeg", "Exterior of the property, taken on arrival."),
        };

        var added = 0;
        foreach (var (file, contentType, description) in files)
        {
            if (await db.UploadFiles.AnyAsync(f => f.AppUserId == owner.Id && f.FileName == file))
                continue;

            var path = Path.Combine(mediaRoot, file);
            if (!File.Exists(path)) continue;

            var bytes = await File.ReadAllBytesAsync(path);

            db.UploadFiles.Add(new UploadFile
            {
                Id                 = Guid.NewGuid(),
                UploadFileTypeId   = evidenceType.Id,
                AppUserId          = owner.Id,
                FileName           = file,
                StoredFileName     = $"{Guid.NewGuid():N}{Path.GetExtension(file)}",
                ContentType        = contentType,
                FileSize           = bytes.LongLength,
                FileData           = bytes,
                Description        = description,
                IsPublic           = false,
                DateCreated        = now,
                CreatedByAppUserId = owner.Id,
            });
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync();
            Console.WriteLine($"[DevDataSeeder] Added {added} demo media files for the video editor.");
        }
    }

    /// <summary>
    /// Personal equipment, group sharing, and two loans in different states.
    /// </summary>
    /// <remarks>
    /// <para>Nothing seeded equipment at all, so every equipment screen rendered its empty state
    /// on a fresh dev database: "You aren't borrowing anything", an empty personal inventory, and
    /// a public catalog with nothing in it. Three of the help documents are about exactly those
    /// screens, and a screenshot of an empty state teaches nobody anything.</para>
    ///
    /// <para><b>Both directions of a loan are seeded on one person.</b> Sarah borrows James's
    /// spirit box and James has asked to borrow her thermal camera, so her My Checkouts screen
    /// shows both halves — "Borrowing" and "Waiting on me" — at once. Seeding only one side leaves
    /// half of every borrowing screen empty, which is the failure this method exists to fix.</para>
    ///
    /// <para>The catalog needs items whose owners opted in, so two of Sarah's three pieces set
    /// <c>IncludeInGlobalCatalog</c> and the third does not: the difference between a listed and
    /// an unlisted piece is itself something the help explains.</para>
    /// </remarks>
    private static async Task SeedEquipmentAsync(
        BenDataContext db, Organization tgh, AppUser owner, AppUser sarah, AppUser james, DateTime now)
    {
        // Real makes and models rather than the generic placeholders: the catalog and the
        // "link to a make or model" help both describe browsing by manufacturer, which reads as
        // nonsense against a list of "Generic / Unbranded".
        var zoom   = await BrandAsync(db, "Zoom",      owner.Id, now);
        var flir   = await BrandAsync(db, "FLIR",      owner.Id, now);
        var pSb    = await BrandAsync(db, "GhostStop", owner.Id, now);

        var h6     = await ModelAsync(db, zoom, "Audio Recorder",            "H6 Handy Recorder",  owner.Id, now);
        var c5     = await ModelAsync(db, flir, "Thermal Imaging",           "C5 Compact Thermal", owner.Id, now);
        var remPod = await ModelAsync(db, pSb,  "REM-Pod / Trigger Device",  "REM-Pod EMT",        owner.Id, now);
        var sb7    = await ModelAsync(db, pSb,  "Spirit Box",                "SB7 Spirit Box",     owner.Id, now);

        if (h6 is null || c5 is null || remPod is null || sb7 is null)
        {
            Console.WriteLine("[DevDataSeeder] Equipment categories missing — skipping equipment seed.");
            return;
        }

        var recorder = await ItemAsync(db, "Field Recorder (H6)", sarah, h6,
            loanable: EquipmentLoanAudience.SharedGroups, inCatalog: true,
            notes: "Primary recorder for interior sessions. Four XLR inputs.", now);

        var thermal  = await ItemAsync(db, "Thermal Camera (C5)", sarah, c5,
            loanable: EquipmentLoanAudience.SharedGroups, inCatalog: true,
            notes: "Kept in the padded case with the spare battery.", now);

        // Deliberately unlisted and not loanable — the contrast is what the help describes.
        await ItemAsync(db, "REM-Pod", sarah, remPod,
            loanable: EquipmentLoanAudience.NotLoanable, inCatalog: false,
            notes: "Calibrated 08/2026.", now);

        var spiritBox = await ItemAsync(db, "Spirit Box (SB7)", james, sb7,
            loanable: EquipmentLoanAudience.SharedGroups, inCatalog: true,
            notes: "Sweep rate switch is stiff — push firmly.", now);

        await ShareAsync(db, recorder,  tgh, sarah.Id, now);
        await ShareAsync(db, thermal,   tgh, sarah.Id, now);
        await ShareAsync(db, spiritBox, tgh, james.Id, now);

        // Out with Sarah now, due back in five days.
        await CheckoutAsync(db, spiritBox, borrower: sarah, forOrg: tgh,
            status: EquipmentCheckoutStatus.CheckedOut,
            requestNotes: "For the Franklin walkthrough on Saturday.",
            checkedOut: now.AddDays(-3), due: now.AddDays(5), reviewedBy: james, now);

        // Waiting on Sarah to decide.
        await CheckoutAsync(db, thermal, borrower: james, forOrg: tgh,
            status: EquipmentCheckoutStatus.Requested,
            requestNotes: "Would like it for the basement survey if it's free.",
            checkedOut: null, due: null, reviewedBy: null, now);

        await db.SaveChangesAsync();
    }

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

        // Same helper the taxonomy seeder uses, so a seeded brand's address follows the one rule
        // the rest of the catalog follows rather than a second, nearly-identical one.
        await Services.EquipmentCatalogSlugs.AssignAsync(db, brand, default);
        await db.SaveChangesAsync();
        return brand;
    }

    private static async Task<EquipmentModel?> ModelAsync(
        BenDataContext db, EquipmentBrand brand, string categoryName, string name, Guid ownerId, DateTime now)
    {
        var category = await db.EquipmentCategories.FirstOrDefaultAsync(c => c.Name == categoryName);
        if (category is null) return null;   // taxonomy seeder has not run

        var existing = await db.EquipmentModels
            .FirstOrDefaultAsync(m => m.EquipmentBrandId == brand.Id && m.Name == name);
        if (existing is not null) return existing;

        var model = new EquipmentModel
        {
            Id = Guid.NewGuid(), EquipmentBrandId = brand.Id, EquipmentCategoryId = category.Id,
            Name = name, IsApproved = true, ApprovedByAppUserId = ownerId, DateApproved = now,
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
            AcquisitionDate = now.AddMonths(-14),
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

    private static async Task CheckoutAsync(
        BenDataContext db, EquipmentItem item, AppUser borrower, Organization forOrg,
        EquipmentCheckoutStatus status, string requestNotes,
        DateTime? checkedOut, DateTime? due, AppUser? reviewedBy, DateTime now)
    {
        if (await db.EquipmentCheckouts.AnyAsync(
                c => c.EquipmentItemId == item.Id && c.BorrowerAppUserId == borrower.Id))
            return;

        db.EquipmentCheckouts.Add(new EquipmentCheckout
        {
            Id = Guid.NewGuid(),
            EquipmentItemId = item.Id,
            BorrowerAppUserId = borrower.Id,
            BorrowedForOrganizationId = forOrg.Id,
            Status = status,
            RequestNotes = requestNotes,
            DateNeededFrom = checkedOut ?? now.AddDays(2),
            DateDue = due,
            DateCheckedOut = checkedOut,
            CheckedOutConfirmedByAppUserId = checkedOut is null ? null : borrower.Id,
            ReviewedByAppUserId = reviewedBy?.Id,
            DateReviewed = reviewedBy is null ? null : now.AddDays(-3),
            DateCreated = now.AddDays(-4), CreatedByAppUserId = borrower.Id,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Data for the home page's "What's Near You" panel — a findable group and public events.
    /// </summary>
    /// <remarks>
    /// <para>Backlog item #88. Without this the panel is correct and empty: no seeded organization
    /// address sets <c>IsSearchable</c> (it defaults to false), and nothing seeded an
    /// <c>OrgCalendarEvent</c> at all. A feature that renders "Nothing found" on a fresh dev
    /// database cannot be click-tested, and looks broken rather than unpopulated.</para>
    ///
    /// <para><b>The two halves are deliberately different, and that is the point of seeding both.</b>
    /// The group's address is marked searchable and <c>FullAddressAndMap</c>, so it appears exactly
    /// where it is — a group that opted in is a business listing. The events resolve through
    /// <c>PublicCoordinates</c> and appear only approximately. Seeing both on one screen is the
    /// cheapest way to notice if that asymmetry ever regresses into "redact everything".</para>
    ///
    /// <para>Events are placed at the seeded Bell Witch Cave, a <c>PlaceKind.PublicLocation</c>.
    /// A private residence would be excluded by <c>PublicEventController.VisibleEvents</c>, so
    /// seeding one there would produce an invisible event and a confusing afternoon.</para>
    /// </remarks>
    private static async Task SeedLocalDiscoveryAsync(
        BenDataContext db, Organization tgh, Organization nps,
        AppUser owner, AppUser emma, DateTime now)
    {
        // ── Make one group findable ──────────────────────────────────────────
        // Only TGH, not both: a panel where every group is findable cannot show that the flag is
        // what does the work.
        var tghAddress = await db.OrganizationAddresses
            .FirstOrDefaultAsync(a => a.OrganizationId == tgh.Id);

        if (tghAddress is not null && !tghAddress.IsSearchable)
        {
            tghAddress.IsSearchable      = true;
            tghAddress.SearchVisibility  = OrganizationAddressVisibility.Public;
            tghAddress.PublicDisplayMode = OrganizationAddressDisplayMode.FullAddressAndMap;
            tghAddress.SearchRadiusMiles = 50;
            await db.SaveChangesAsync();
            Console.WriteLine("[DevDataSeeder] Made Tennessee Ghost Hunters findable in nearby search.");
        }

        // ── Public events ────────────────────────────────────────────────────
        var placeId = new Guid("40000001-0000-0000-0000-000000000001");
        if (!await db.Places.AnyAsync(p => p.Id == placeId)) return;

        // ── A PAST public event with a confirmed outside attendee (item 111) ──
        // Daniel belongs to no group, which makes him the canonical stranger-attendee: the
        // evidence-submission door must open for him on attendance alone, and the e2e walk needs
        // that state to exist without driving the email-confirmation flow.
        var daniel = await db.Users.FirstOrDefaultAsync(u => u.Email == "daniel.park@benco.dev");
        if (!await db.OrgCalendarEvents.AnyAsync(e => e.Title == "Bell Witch Cave — Last Month's Open Night"))
        {
        var pastWalk = new OrgCalendarEvent
        {
            Id = Guid.NewGuid(), OrganizationId = tgh.Id,
            Title = "Bell Witch Cave — Last Month's Open Night",
            Description = "<p>The previous public night at the cave.</p>",
            PlaceId = placeId,
            StartDateTime = now.AddDays(-30).Date.AddHours(20),
            EndDateTime   = now.AddDays(-30).Date.AddHours(23),
            IsPublic = true,
            UrlName = $"{now.AddDays(-30):yyyy-MM-dd}-bell-witch-cave-open-night",
            DateCreated = now, CreatedByAppUserId = owner.Id,
        };
        db.OrgCalendarEvents.Add(pastWalk);

        if (daniel is not null)
            db.EventAttendanceInvites.Add(new EventAttendanceInvite
            {
                Id = Guid.NewGuid(), OrgCalendarEventId = pastWalk.Id,
                Email = daniel.Email!, DisplayName = "Daniel",
                DateConfirmed = now.AddDays(-31), ConfirmedByAppUserId = daniel.Id,
                DateExpires = now.AddDays(-31).AddDays(14),
                DateCreated = now.AddDays(-31), CreatedByAppUserId = daniel.Id,
            });

        await db.SaveChangesAsync();
        Console.WriteLine("[DevDataSeeder] Seeded the past public event with Daniel as a confirmed attendee (item 111).");
        }

        if (await db.OrgCalendarEvents.AnyAsync(e => e.IsPublic && e.StartDateTime > now)) return;

        // Dated from "now" rather than fixed, so the panel — which shows upcoming events only —
        // does not quietly empty out as the seed data ages.
        var walk = new OrgCalendarEvent
        {
            Id = Guid.NewGuid(), OrganizationId = tgh.Id,
            Title = "Bell Witch Cave — Public Night Walk",
            Description = "<p>An open evening at the cave. Bring a torch; we supply the recorders.</p>",
            PlaceId = placeId,
            StartDateTime = now.AddDays(14).Date.AddHours(20),
            EndDateTime   = now.AddDays(14).Date.AddHours(23),
            IsPublic = true,
            UrlName = $"{now.AddDays(14):yyyy-MM-dd}-bell-witch-cave-public-night-walk",
            AttendeeCapacity = 20,
            DateCreated = now, CreatedByAppUserId = owner.Id,
        };

        // A second event from the other group, at their own Nashville address rather than the cave.
        // The distance matters: the cave is ~33 miles from Nashville, so at the panel's default
        // 25-mile radius the walk alone is out of range. Seeding one event close and one far means
        // a fresh dev database shows something immediately AND the distance dropdown visibly does
        // something when widened — one event at 25 miles, two at 50.
        var npsAddress = await db.OrganizationAddresses
            .FirstOrDefaultAsync(a => a.OrganizationId == nps.Id);

        var talk = new OrgCalendarEvent
        {
            Id = Guid.NewGuid(), OrganizationId = nps.Id,
            Title = "Open Meeting — What We Found This Year",
            Description = "<p>Our annual public review of the season's investigations. Anyone welcome.</p>",
            // No PlaceId: an event may name an organization address instead, and VisibleEvents
            // allows a null Place. The nearby projection falls back to the address for coordinates.
            OrganizationAddressId = npsAddress?.Id,
            StartDateTime = now.AddDays(28).Date.AddHours(19),
            EndDateTime   = now.AddDays(28).Date.AddHours(21),
            IsPublic = true,
            UrlName = $"{now.AddDays(28):yyyy-MM-dd}-open-meeting-what-we-found-this-year",
            DateCreated = now, CreatedByAppUserId = emma.Id,
        };

        db.OrgCalendarEvents.AddRange(walk, talk);
        await db.SaveChangesAsync();
        Console.WriteLine("[DevDataSeeder] Created two public events for local discovery, and a past one with a confirmed attendee.");
    }

    /// <summary>
    /// One landmark investigated by two different organizations, with each sharing scope in use.
    /// </summary>
    /// <remarks>
    /// <para>Area 9's central claim is that several groups accumulate visits at the same place over
    /// years and comparing notes is useful. Nothing in the case-derived seed data exercises that —
    /// every backfilled place belongs to exactly one case of one organization — so the sharing
    /// rules could only be seen working by hand-building rows.</para>
    ///
    /// <para>Deliberately mixed: a group-only visit that must stay hidden from the other group, a
    /// shared one that must be visible to them, and a public one visible to anybody. If the
    /// visibility filter regresses, this is the data that shows it on screen rather than only in a
    /// test.</para>
    /// </remarks>
    private static async Task SeedSharedPlaceAsync(
        BenDataContext db, Organization tgh, Organization nps,
        AppUser owner, AppUser emma, AppUser sarah, DateTime now)
    {
        var placeId = new Guid("40000001-0000-0000-0000-000000000001");
        if (await db.Places.AnyAsync(p => p.Id == placeId)) return;

        db.Places.Add(new Place
        {
            Id = placeId,
            Name = "Bell Witch Cave",
            City = "Adams",
            State = "TN",
            ZipCode = "37010",
            Country = "US",
            Latitude = 36.5893000000m,
            Longitude = -87.0625000000m,
            DateGeocoded = now,
            // A landmark, so investigations here default to sharing with fellow investigators.
            Kind = PlaceKind.PublicLocation,
            DateCreated = now, CreatedByAppUserId = owner.Id,
        });

        // Tennessee Ghost Hunters: one shared, one kept back. The pair is the point — a group can
        // share some of its work at a place without sharing all of it.
        var tghShared = NewVisit(placeId, tgh.Id, owner.Id, now.AddDays(-120),
            "Bell Witch Cave — winter survey", InvestigationVisibility.PlaceInvestigators);
        var tghPrivate = NewVisit(placeId, tgh.Id, owner.Id, now.AddDays(-60),
            "Bell Witch Cave — follow-up (internal)", InvestigationVisibility.GroupOnly);

        // Nashville Paranormal Society: public, so even a signed-in stranger sees it.
        var npsPublic = NewVisit(placeId, nps.Id, emma.Id, now.AddDays(-30),
            "Bell Witch Cave — published walkthrough", InvestigationVisibility.Public);

        db.Investigations.AddRange(tghShared, tghPrivate, npsPublic);

        // Attendance, so the personal map has something on it. Self-reported: RecordedBy stays null.
        db.InvestigationAttendees.AddRange(
            Attended(tghShared.Id, owner.Id, isLead: true, now.AddDays(-120)),
            Attended(tghShared.Id, sarah.Id, isLead: false, now.AddDays(-120)),
            Attended(tghPrivate.Id, owner.Id, isLead: true, now.AddDays(-60)),
            Attended(npsPublic.Id, emma.Id, isLead: true, now.AddDays(-30)));

        await db.SaveChangesAsync();
        Console.WriteLine("[DevDataSeeder] Created Bell Witch Cave with visits from two organizations.");

        static Investigation NewVisit(
            Guid placeId, Guid orgId, Guid createdBy, DateTime when,
            string title, InvestigationVisibility visibility) => new()
            {
                Id = Guid.NewGuid(),
                OrganizationId = orgId,
                CaseId = null,
                PlaceId = placeId,
                Title = title,
                Visibility = visibility,
                ScheduledDateTime = when,
                EndDateTime = when.AddHours(5),
                Status = InvestigationStatus.Completed,
                Latitude = 36.5893000000m,
                Longitude = -87.0625000000m,
                DateGeocoded = when,
                DateCreated = when, CreatedByAppUserId = createdBy,
            };

        static InvestigationAttendee Attended(Guid invId, Guid userId, bool isLead, DateTime when) => new()
        {
            Id = Guid.NewGuid(), InvestigationId = invId, AppUserId = userId,
            Rsvp = RsvpStatus.Accepted, DidAttend = true, DateArrived = when,
            IsLead = isLead,
            // Null: they checked themselves in. Keeps the seed consistent with what the check-in
            // endpoint would have written.
            AttendanceRecordedByAppUserId = null,
            DateCreated = when, CreatedByAppUserId = userId,
        };
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
        DateTime now,
        decimal? latitude = null, decimal? longitude = null)
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
            Latitude = latitude, Longitude = longitude,
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
            Visibility = CaseTimelineVisibility.Public,
            DateCreated = now, CreatedByAppUserId = creator.Id,
        });
        db.CaseTimelineEntries.Add(new CaseTimelineEntry
        {
            Id = Guid.NewGuid(), CaseId = caseEntity.Id, AuthorAppUserId = creator.Id,
            EntryType = CaseTimelineEntryType.InvestigatorNote,
            EventDateTime = opened,
            Title = "Case Opened",
            Body = "<p>Case accepted for investigation. Preliminary review of client statement complete. Site visit scheduled.</p>",
            Visibility = CaseTimelineVisibility.Public,
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
                Visibility = CaseTimelineVisibility.Public,
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
