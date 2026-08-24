using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Service.RepositoryService.GenericInterfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Controllers;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Security.Claims;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for <see cref="NotificationSummaryController"/> — verifies each unread bucket counts the
/// right rows for the right user, that read/other-user rows are excluded, and that the oldest
/// timestamp is reported for age-based badge colouring.
/// </summary>
public class NotificationSummaryControllerTests
{
    private static readonly DateTime Older = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Newer = new(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static NotificationSummaryController Build(
        IDbContextFactory<BenDataContext> factory, Guid? userId)
    {
        var ctrl = new NotificationSummaryController(factory, new Mock<IOrganizationSecurityService>().Object);
        var claims = userId.HasValue
            ? new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())
              ], "Bearer"))
            : new ClaimsPrincipal(new ClaimsIdentity());
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claims }
        };
        return ctrl;
    }

    private static async Task<NotificationSummaryResponse> GetSummaryAsync(
        IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var result = await Build(factory, userId).GetSummary(default);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<NotificationSummaryResponse>(ok.Value);
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Groundwork for the anonymous question channel: a message flagged to hide its sender must
    /// give up BOTH the name and the id. Leaving the id would deanonymise it just as thoroughly,
    /// and the name's fallback is the author's email address.
    /// </summary>
    [Fact]
    public async Task AMessageMarkedAnonymousRevealsNeitherTheSendersNameNorTheirId()
    {
        var factory = TestDbFactory.Create();
        var authorId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser
            { Id = authorId, UserName = "asker@t", Email = "asker@t", DisplayName = "The Asker" });
            db.Users.Add(new AppUser
            { Id = recipientId, UserName = "owner@t", Email = "owner@t", DisplayName = "The Owner" });

            var typeId = Guid.NewGuid();
            db.UserMessageTypes.Add(new UserMessageType
            { Id = typeId, Name = "Equipment Question", IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = authorId });

            var messageId = Guid.NewGuid();
            db.UserMessages.Add(new UserMessage
            {
                Id = messageId, UserMessageTypeId = typeId,
                MessageSubject = "A question about your equipment",
                MessageBody = "Does it come with the windshield?",
                HideSenderIdentity = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = authorId,
            });
            db.UserMessageTos.Add(new UserMessageTo
            { Id = Guid.NewGuid(), MessageId = messageId, ToAppUserId = recipientId });
            await db.SaveChangesAsync();
        }

        var ctrl = new MyMessagesController(factory)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, recipientId.ToString())], "Bearer"))
                }
            }
        };

        var result = await ctrl.GetMine(unreadOnly: false, default);
        var messages = Assert.IsAssignableFrom<IEnumerable<MyMessageRecord>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ToList();

        var message = Assert.Single(messages);
        Assert.True(message.SenderHidden);
        Assert.Null(message.SentByDisplayName);
        Assert.Null(message.SentByAppUserId);

        // The database still knows — anonymity is a presentation rule, not lost provenance.
        await using var check = await factory.CreateDbContextAsync();
        Assert.Equal(authorId, (await check.UserMessages.SingleAsync()).CreatedByAppUserId);
    }

    [Fact]
    public async Task GetSummary_ReturnsUnauthorized_WhenNoUserClaim()
    {
        var result = await Build(CreateFactory(), userId: null).GetSummary(default);
        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task GetSummary_ReturnsAllZero_WhenNothingIsWaiting()
    {
        var summary = await GetSummaryAsync(CreateFactory(), Guid.NewGuid());

        Assert.Equal(0, summary.TotalCount);
        Assert.Null(summary.OldestUnreadUtc);
    }

    // ── Org messages ──────────────────────────────────────────────────────────

    [Fact]
    public async Task OrgMessages_CountsOnlyUnreadRowsAddressedToMe()
    {
        var factory = CreateFactory();
        var me      = Guid.NewGuid();
        var other   = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var orgId = AddOrg(db);
            var m1 = AddOrgMessage(db, Older, orgId);
            var m2 = AddOrgMessage(db, Newer, orgId);
            var m3 = AddOrgMessage(db, Newer, orgId);

            db.OrgMessageRecipients.AddRange(
                new OrgMessageRecipient { Id = Guid.NewGuid(), OrgMessageId = m1, RecipientAppUserId = me,    DateRead = null },
                new OrgMessageRecipient { Id = Guid.NewGuid(), OrgMessageId = m2, RecipientAppUserId = me,    DateRead = null },
                new OrgMessageRecipient { Id = Guid.NewGuid(), OrgMessageId = m3, RecipientAppUserId = me,    DateRead = Newer },  // already read
                new OrgMessageRecipient { Id = Guid.NewGuid(), OrgMessageId = m3, RecipientAppUserId = other, DateRead = null });  // someone else
            await db.SaveChangesAsync();
        }

        var summary = await GetSummaryAsync(factory, me);

        Assert.Equal(2, summary.OrgMessages.Count);
        Assert.Equal(Older, summary.OrgMessages.OldestUnreadUtc);
        Assert.Equal(2, summary.TotalCount);
    }

    // ── Case messages, org side ───────────────────────────────────────────────

    [Fact]
    public async Task CaseMessagesAsOrgMember_CountsClientMessagesAwaitingMyOrg()
    {
        var factory = CreateFactory();
        var me      = Guid.NewGuid();
        var myOrg   = Guid.NewGuid();
        var otherOrg = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = myOrg, AppUserId = me, IsActive = true,
                DateCreated = Older, CreatedByAppUserId = me,
            });

            var myCase    = AddCase(db, myOrg);
            var otherCase = AddCase(db, otherOrg);

            db.CaseMessages.AddRange(
                // awaiting my org — counts
                NewCaseMessage(myCase, CaseMessageSide.Client, isReadByOrg: false, at: Older),
                // already handled by the org
                NewCaseMessage(myCase, CaseMessageSide.Client, isReadByOrg: true, at: Newer),
                // our own outgoing message
                NewCaseMessage(myCase, CaseMessageSide.Organization, isReadByOrg: true, at: Newer),
                // a different org's case
                NewCaseMessage(otherCase, CaseMessageSide.Client, isReadByOrg: false, at: Newer));
            await db.SaveChangesAsync();
        }

        var summary = await GetSummaryAsync(factory, me);

        Assert.Equal(1, summary.CaseMessagesAsOrgMember.Count);
        Assert.Equal(Older, summary.CaseMessagesAsOrgMember.OldestUnreadUtc);
    }

    [Fact]
    public async Task CaseMessagesAsOrgMember_IgnoresInactiveMembership()
    {
        var factory = CreateFactory();
        var me      = Guid.NewGuid();
        var org     = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = org, AppUserId = me, IsActive = false,
                DateCreated = Older, CreatedByAppUserId = me,
            });
            var c = AddCase(db, org);
            db.CaseMessages.Add(NewCaseMessage(c, CaseMessageSide.Client, isReadByOrg: false, at: Older));
            await db.SaveChangesAsync();
        }

        var summary = await GetSummaryAsync(factory, me);

        Assert.Equal(0, summary.CaseMessagesAsOrgMember.Count);
    }

    // ── Case messages, client side ────────────────────────────────────────────

    [Fact]
    public async Task CaseMessagesAsClient_CountsOrgRepliesOnMyOwnCase()
    {
        var factory = CreateFactory();
        var me      = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var requestId = Guid.NewGuid();
            db.ClientRequests.Add(new ClientRequest
            {
                Id = requestId, AppUserId = me, Status = ClientRequestStatus.Submitted,
                StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201",
                Country = "US", DateCreated = Older, CreatedByAppUserId = me,
            });
            var caseId = AddCase(db, Guid.NewGuid(), clientRequestId: requestId);

            db.CaseMessages.AddRange(
                NewCaseMessage(caseId, CaseMessageSide.Organization, isReadByClient: false, at: Older),
                NewCaseMessage(caseId, CaseMessageSide.Organization, isReadByClient: true,  at: Newer),
                NewCaseMessage(caseId, CaseMessageSide.Client,       isReadByClient: true,  at: Newer));
            await db.SaveChangesAsync();
        }

        var summary = await GetSummaryAsync(factory, me);

        Assert.Equal(1, summary.CaseMessagesAsClient.Count);
        Assert.Equal(Older, summary.CaseMessagesAsClient.OldestUnreadUtc);
    }

    [Fact]
    public async Task CaseMessagesAsClient_IncludesCasesSharedWithMeAsCoClient()
    {
        var factory = CreateFactory();
        var me      = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var caseId = AddCase(db, Guid.NewGuid());   // someone else's case…
            db.CaseClientAccesses.Add(new CaseClientAccess          // …shared with me
            {
                Id = Guid.NewGuid(), CaseId = caseId, AppUserId = me,
                DateCreated = Older, CreatedByAppUserId = me,
            });
            db.CaseMessages.Add(NewCaseMessage(caseId, CaseMessageSide.Organization, isReadByClient: false, at: Newer));
            await db.SaveChangesAsync();
        }

        var summary = await GetSummaryAsync(factory, me);

        Assert.Equal(1, summary.CaseMessagesAsClient.Count);
    }

    // ── System messages ───────────────────────────────────────────────────────

    [Fact]
    public async Task SystemMessages_CountsUnreadRowsForMeOnly()
    {
        var factory = CreateFactory();
        var me      = Guid.NewGuid();
        var other   = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var typeId = Guid.NewGuid();
            db.UserMessageTypes.Add(new UserMessageType
            {
                Id = typeId, Name = "System Notification", IsActive = true, IsPublic = false,
                SortOrder = 1, DateCreated = Older, CreatedByAppUserId = me,
            });

            var msg1 = Guid.NewGuid();
            var msg2 = Guid.NewGuid();
            db.UserMessages.AddRange(
                new UserMessage { Id = msg1, UserMessageTypeId = typeId, MessageBody = "a", DateCreated = Older, CreatedByAppUserId = me },
                new UserMessage { Id = msg2, UserMessageTypeId = typeId, MessageBody = "b", DateCreated = Newer, CreatedByAppUserId = me });

            db.UserMessageTos.AddRange(
                new UserMessageTo { MessageId = msg1, ToAppUserId = me,    DateLastRead = null },
                new UserMessageTo { MessageId = msg2, ToAppUserId = me,    DateLastRead = Newer },  // read
                new UserMessageTo { MessageId = msg2, ToAppUserId = other, DateLastRead = null });  // not mine
            await db.SaveChangesAsync();
        }

        var summary = await GetSummaryAsync(factory, me);

        Assert.Equal(1, summary.SystemMessages.Count);
        Assert.Equal(Older, summary.SystemMessages.OldestUnreadUtc);
    }

    // ── Pending permission requests ───────────────────────────────────────────

    [Fact]
    public async Task PendingPermissionRequests_CountsOnlyPendingOnFilesIOwn()
    {
        var factory = CreateFactory();
        var me      = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var myFile     = AddFile(db, ownerId: me);
            var othersFile = AddFile(db, ownerId: Guid.NewGuid());

            db.UploadFilePermissionRequests.AddRange(
                NewRequest(myFile,     FilePermissionRequestStatus.Pending,  Older),
                NewRequest(myFile,     FilePermissionRequestStatus.Approved, Newer),  // already handled
                NewRequest(othersFile, FilePermissionRequestStatus.Pending,  Newer)); // not my file
            await db.SaveChangesAsync();
        }

        var summary = await GetSummaryAsync(factory, me);

        Assert.Equal(1, summary.PendingPermissionRequests.Count);
        Assert.Equal(Older, summary.PendingPermissionRequests.OldestUnreadUtc);
    }

    // ── Roll-up ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task TotalAndOldest_RollUpAcrossBuckets()
    {
        var factory = CreateFactory();
        var me      = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            // The message needs a group: an org-less unread has no surface that can show it,
            // so item 173 deliberately keeps it OFF the bell rather than in a count nothing opens.
            var m = AddOrgMessage(db, Newer, AddOrg(db));
            db.OrgMessageRecipients.Add(new OrgMessageRecipient
            {
                Id = Guid.NewGuid(), OrgMessageId = m, RecipientAppUserId = me, DateRead = null
            });

            var myFile = AddFile(db, ownerId: me);
            db.UploadFilePermissionRequests.Add(
                NewRequest(myFile, FilePermissionRequestStatus.Pending, Older));   // the oldest thing waiting
            await db.SaveChangesAsync();
        }

        var summary = await GetSummaryAsync(factory, me);

        Assert.Equal(2, summary.TotalCount);
        Assert.Equal(Older, summary.OldestUnreadUtc);
    }

    // ── Seed helpers ──────────────────────────────────────────────────────────

    private static Guid AddOrg(BenDataContext db, string name = "Org")
    {
        var id = Guid.NewGuid();
        db.Organizations.Add(new Organization
        {
            Id = id, Name = name, UrlName = $"org-{id:N}",
            DateCreated = Older, CreatedByAppUserId = Guid.NewGuid(),
        });
        return id;
    }

    private static Guid AddOrgMessage(BenDataContext db, DateTime at, Guid? orgId = null)
    {
        var id = Guid.NewGuid();
        db.OrgMessages.Add(new OrgMessage
        {
            Id = id, AuthorAppUserId = Guid.NewGuid(), Body = "hello",
            OrganizationId = orgId,
            ChannelType = OrgMessageChannel.OrgBroadcast,
            DateCreated = at, CreatedByAppUserId = Guid.NewGuid(),
        });
        return id;
    }

    private static Guid AddCase(BenDataContext db, Guid orgId, Guid? clientRequestId = null)
    {
        var id = Guid.NewGuid();
        db.Cases.Add(new Case
        {
            Id = id, OrganizationId = orgId, ClientRequestId = clientRequestId,
            Title = "Case", Status = CaseStatus.Active, CaseYear = 2026, OrgCaseNumber = 1,
            StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201",
            Country = "US", DateCaseOpened = Older,
            DateCreated = Older, CreatedByAppUserId = Guid.NewGuid(),
        });
        return id;
    }

    private static CaseMessage NewCaseMessage(
        Guid caseId, CaseMessageSide side, DateTime at,
        bool isReadByOrg = false, bool isReadByClient = false)
        => new()
        {
            Id = Guid.NewGuid(), CaseId = caseId, AuthorAppUserId = Guid.NewGuid(),
            Body = "msg", SenderSide = side,
            IsReadByOrg = isReadByOrg, IsReadByClient = isReadByClient,
            DateCreated = at, CreatedByAppUserId = Guid.NewGuid(),
        };

    private static Guid AddFile(BenDataContext db, Guid ownerId)
    {
        var id = Guid.NewGuid();
        db.UploadFiles.Add(new UploadFile
        {
            Id = id, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
            FileName = "f.wav", StoredFileName = "s.wav", ContentType = "audio/wav",
            FileSize = 1, DateCreated = Older, CreatedByAppUserId = ownerId,
        });
        return id;
    }

    private static UploadFilePermissionRequest NewRequest(
        Guid fileId, FilePermissionRequestStatus status, DateTime at)
        => new()
        {
            Id = Guid.NewGuid(), UploadFileId = fileId,
            RequestedByAppUserId = Guid.NewGuid(), OrganizationId = Guid.NewGuid(),
            PermissionType = FilePermissionType.Use, RequestStatus = status,
            DateCreated = at, CreatedByAppUserId = Guid.NewGuid(),
        };

    // ── Investigation invites ─────────────────────────────────────────────────

    /// <summary>
    /// Seeds an investigation plus one attendee row. The controller compares
    /// <c>ScheduledDateTime</c> against the real <c>DateTime.UtcNow</c>, so schedules are
    /// expressed as offsets from now rather than the fixed Older/Newer constants.
    /// </summary>
    private static void AddInvite(
        BenDataContext db, Guid userId, TimeSpan scheduledIn, DateTime invitedAt,
        RsvpStatus rsvp = RsvpStatus.Invited,
        InvestigationStatus status = InvestigationStatus.Scheduled)
    {
        var invId = Guid.NewGuid();
        db.Investigations.Add(new Investigation
        {
            Id = invId, CaseId = Guid.NewGuid(), Title = "Night visit",
            ScheduledDateTime = DateTime.UtcNow.Add(scheduledIn), Status = status,
            DateCreated = invitedAt, CreatedByAppUserId = Guid.NewGuid(),
        });
        db.InvestigationAttendees.Add(new InvestigationAttendee
        {
            Id = Guid.NewGuid(), InvestigationId = invId, AppUserId = userId,
            Rsvp = rsvp, DateCreated = invitedAt, CreatedByAppUserId = Guid.NewGuid(),
        });
    }

    [Fact]
    public async Task GetSummary_CountsUnansweredInvitesToUpcomingInvestigations()
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            AddInvite(db, userId, TimeSpan.FromDays(3), invitedAt: Newer);
            AddInvite(db, userId, TimeSpan.FromDays(9), invitedAt: Older);
            await db.SaveChangesAsync();
        }

        var summary = await GetSummaryAsync(factory, userId);

        Assert.Equal(2, summary.InvestigationInvites.Count);
        // The invite's own age, not the visit date — the shared badge classifier reads this
        // as "waiting since", and a future date would come out as negative age.
        Assert.Equal(Older, summary.InvestigationInvites.OldestUnreadUtc);
    }

    [Theory]
    [InlineData(RsvpStatus.Accepted)]
    [InlineData(RsvpStatus.Declined)]
    [InlineData(RsvpStatus.Tentative)]
    public async Task GetSummary_IgnoresInvitesTheUserAlreadyAnswered(RsvpStatus answered)
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            AddInvite(db, userId, TimeSpan.FromDays(3), Newer, rsvp: answered);
            await db.SaveChangesAsync();
        }

        Assert.Equal(0, (await GetSummaryAsync(factory, userId)).InvestigationInvites.Count);
    }

    [Fact]
    public async Task GetSummary_IgnoresInvitesThatAreNoLongerActionable()
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            // Already happened — an unanswered RSVP for last week is history, not a task.
            AddInvite(db, userId, TimeSpan.FromDays(-2), Newer);
            // Called off, so there is nothing left to answer.
            AddInvite(db, userId, TimeSpan.FromDays(3), Newer,
                status: InvestigationStatus.Cancelled);
            await db.SaveChangesAsync();
        }

        Assert.Equal(0, (await GetSummaryAsync(factory, userId)).InvestigationInvites.Count);
    }

    [Fact]
    public async Task GetSummary_IgnoresInvitesAddressedToSomeoneElse()
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            AddInvite(db, Guid.NewGuid(), TimeSpan.FromDays(3), Newer);
            await db.SaveChangesAsync();
        }

        Assert.Equal(0, (await GetSummaryAsync(factory, userId)).InvestigationInvites.Count);
    }

    // ── Item 173: per-group breakdowns — each row opens exactly what it counts ──

    [Fact]
    public async Task OrgMessages_BreakdownSlicesPerGroup_AndTheAggregateIsTheirSum()
    {
        var factory = CreateFactory();
        var me = Guid.NewGuid();
        Guid orgA = default, orgB = default;

        await using (var db = await factory.CreateDbContextAsync())
        {
            orgA = AddOrg(db, "Alpha");
            orgB = AddOrg(db, "Beta");
            foreach (var (org, when) in new[] { (orgA, Older), (orgA, Newer), (orgB, Newer) })
            {
                var m = AddOrgMessage(db, when, org);
                db.OrgMessageRecipients.Add(new OrgMessageRecipient
                {
                    Id = Guid.NewGuid(), OrgMessageId = m, RecipientAppUserId = me, DateRead = null
                });
            }
            await db.SaveChangesAsync();
        }

        var summary = await GetSummaryAsync(factory, me);

        var slices = summary.OrgMessagesByOrg!;
        Assert.Equal(2, slices.Count);
        var alpha = Assert.Single(slices, x => x.OrganizationId == orgA);
        Assert.Equal("Alpha", alpha.OrganizationName);
        Assert.Equal(2, alpha.Count);
        Assert.Equal(Older, alpha.OldestUnreadUtc);
        var beta = Assert.Single(slices, x => x.OrganizationId == orgB);
        Assert.Equal(1, beta.Count);

        // The bell's number is the sum of the rows underneath it — never more, never less.
        Assert.Equal(3, summary.OrgMessages.Count);
        Assert.Equal(slices.Sum(x => x.Count), summary.OrgMessages.Count);
    }

    [Fact]
    public async Task CaseMessagesAsOrgMember_BreakdownSlicesPerCase()
    {
        var factory = CreateFactory();
        var me = Guid.NewGuid();
        Guid orgA = default, orgB = default;

        await using (var db = await factory.CreateDbContextAsync())
        {
            orgA = AddOrg(db, "Alpha");
            orgB = AddOrg(db, "Beta");
            foreach (var org in new[] { orgA, orgB })
                db.OrganizationUserMemberships.Add(new OrganizationUserMembership
                {
                    Id = Guid.NewGuid(), OrganizationId = org, AppUserId = me, IsActive = true,
                    Role = OrganizationMemberRole.Owner,   // the bypass keeps routing out of the way
                    DateCreated = Older, CreatedByAppUserId = me,
                });

            var caseA = AddCase(db, orgA);
            var caseB = AddCase(db, orgB);
            db.CaseMessages.AddRange(
                NewCaseMessage(caseA, CaseMessageSide.Client, isReadByOrg: false, at: Older),
                NewCaseMessage(caseA, CaseMessageSide.Client, isReadByOrg: false, at: Newer),
                NewCaseMessage(caseB, CaseMessageSide.Client, isReadByOrg: false, at: Newer));
            await db.SaveChangesAsync();
        }

        var summary = await GetSummaryAsync(factory, me);

        // One row per CASE (Ben: "show the cases"), each carrying its group's name, and the
        // aggregate is still the fold of the slices.
        var slices = summary.CaseMessagesAsOrgMemberByCase!;
        Assert.Equal(2, slices.Count);
        var sliceA = Assert.Single(slices, x => x.OrganizationId == orgA);
        Assert.Equal(2, sliceA.Count);
        Assert.Equal("Case", sliceA.CaseTitle);
        Assert.Equal("Alpha", sliceA.OrganizationName);
        Assert.Equal(1, Assert.Single(slices, x => x.OrganizationId == orgB).Count);
        Assert.Equal(3, summary.CaseMessagesAsOrgMember.Count);
        Assert.Equal(slices.Sum(x => x.Count), summary.CaseMessagesAsOrgMember.Count);
    }

    // ── item 186 F3: feed activity ────────────────────────────────────────────

    /// <summary>Turns the feed on and puts two people in the database.</summary>
    private static async Task<(IDbContextFactory<BenDataContext> Factory, Guid Author, Guid Other)>
        SeedFeedAsync()
    {
        var factory = CreateFactory();
        Guid author = Guid.NewGuid(), other = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser
        {
            Id = author, UserName = "a@t.com", NormalizedUserName = "A@T.COM",
            Email = "a@t.com", NormalizedEmail = "A@T.COM", DateCreated = DateTime.UtcNow,
        });
        db.Users.Add(new AppUser
        {
            Id = other, UserName = "b@t.com", NormalizedUserName = "B@T.COM",
            Email = "b@t.com", NormalizedEmail = "B@T.COM", DateCreated = DateTime.UtcNow,
        });
        db.SiteSettings.Add(new SiteSetting
        {
            Id = Guid.NewGuid(), Key = SiteSettingKeys.FeaturePublicFeed, Value = "true",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = author,
        });
        await db.SaveChangesAsync();
        return (factory, author, other);
    }

    private static OrgMessage FeedPost(Guid authorId, Guid? parentId = null) => new()
    {
        Id = Guid.NewGuid(),
        AuthorAppUserId = authorId,
        ParentMessageId = parentId,
        ChannelType = OrgMessageChannel.PublicFeed,
        Body = parentId is null ? "A post." : "An answer.",
        IsPublic = true,
        DateCreated = DateTime.UtcNow,
        CreatedByAppUserId = authorId,
    };

    /// <summary>
    /// Somebody answering your post is activity worth a badge (item 186 F3).
    /// </summary>
    /// <remarks>
    /// Before F3 the bucket counted mentions only, so the most ordinary thing that can happen on
    /// a feed — being replied to — reached nobody.
    /// </remarks>
    [Fact]
    public async Task AReplyToYourPostCountsAsFeedActivity()
    {
        var (factory, author, other) = await SeedFeedAsync();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var root = FeedPost(author);
            db.OrgMessages.Add(root);
            db.OrgMessages.Add(FeedPost(other, root.Id));
            await db.SaveChangesAsync();
        }

        var summary = await GetSummaryAsync(factory, author);
        Assert.Equal(1, summary.FeedMentions.Count);
    }

    [Fact]
    public async Task YourOwnReplyToYourOwnPostIsNotActivity()
    {
        var (factory, author, _) = await SeedFeedAsync();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var root = FeedPost(author);
            db.OrgMessages.Add(root);
            db.OrgMessages.Add(FeedPost(author, root.Id));
            await db.SaveChangesAsync();
        }

        Assert.Equal(0, (await GetSummaryAsync(factory, author)).FeedMentions.Count);
    }

    [Fact]
    public async Task OpeningTheThreadClearsTheReply()
    {
        var (factory, author, other) = await SeedFeedAsync();
        Guid rootId;

        await using (var db = await factory.CreateDbContextAsync())
        {
            var root = FeedPost(author);
            rootId = root.Id;
            db.OrgMessages.Add(root);
            db.OrgMessages.Add(FeedPost(other, root.Id));
            await db.SaveChangesAsync();
        }

        Assert.Equal(1, (await GetSummaryAsync(factory, author)).FeedMentions.Count);

        // The same marker a mention is cleared by: the view recorded against the ROOT post.
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrgMessageViews.Add(new OrgMessageView
            {
                OrgMessageId = rootId, ViewerAppUserId = author, DateViewed = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(0, (await GetSummaryAsync(factory, author)).FeedMentions.Count);
    }

    [Fact]
    public async Task AHiddenReplyIsWithdrawnFromTheBadge()
    {
        var (factory, author, other) = await SeedFeedAsync();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var root = FeedPost(author);
            var reply = FeedPost(other, root.Id);
            reply.HiddenUtc = DateTime.UtcNow;
            db.OrgMessages.Add(root);
            db.OrgMessages.Add(reply);
            await db.SaveChangesAsync();
        }

        Assert.Equal(0, (await GetSummaryAsync(factory, author)).FeedMentions.Count);
    }

    /// <summary>
    /// A like is applause, not a message: deliberately NOT on the badge.
    /// </summary>
    /// <remarks>
    /// A badge that ticks on every like is a badge nobody reads by the end of the week, which
    /// would cost the mentions their meaning too — they share the bucket.
    /// </remarks>
    [Fact]
    public async Task ALikeIsNotActivity()
    {
        var (factory, author, other) = await SeedFeedAsync();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var root = FeedPost(author);
            db.OrgMessages.Add(root);
            await db.SaveChangesAsync();

            db.OrgMessageLikes.Add(new OrgMessageLike
            {
                OrgMessageId = root.Id, LikerAppUserId = other, DateLiked = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(0, (await GetSummaryAsync(factory, author)).FeedMentions.Count);
    }
}
