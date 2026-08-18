using Ben.Data.Common.Enums;
using Ben.Service.Models.Entities;
using Ben.Web.Services;
using Ben.Web.Services.WebApi;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>Tests for Phase 4 messaging and calendar adapter methods.</summary>
public class Phase4AdapterTests
{
    private static Mock<IWebApiClient> ApiMock() => new();
    private static Mock<IWebApiAuthService> AuthMock() => new();

    private static BenAdminClientAdapter Build(Mock<IWebApiClient> api)
        => new BenAdminClientAdapter(api.Object, AuthMock().Object,
            Microsoft.Extensions.Options.Options.Create(new WebApiOptions()));

    // ── Messaging ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOrgInboxAsync_GetsFromCorrectUrl()
    {
        var orgId = Guid.NewGuid();
        var api   = ApiMock();
        api.Setup(x => x.GetAsync<IReadOnlyList<OrgMessageRecord>>(
                $"/api/organizations/{orgId}/messages/inbox", It.IsAny<CancellationToken>()))
           .ReturnsAsync([new() { Id = Guid.NewGuid(), Body = "Hi", AuthorAppUserId = Guid.NewGuid() }]);

        var result = await Build(api).GetOrgInboxAsync(orgId);

        Assert.Single(result);
        api.Verify(x => x.GetAsync<IReadOnlyList<OrgMessageRecord>>(
            $"/api/organizations/{orgId}/messages/inbox", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOrgInboxAsync_WhenApiReturnsNull_ReturnsEmpty()
    {
        var api = ApiMock();
        api.Setup(x => x.GetAsync<IReadOnlyList<OrgMessageRecord>>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((IReadOnlyList<OrgMessageRecord>?)null);

        var result = await Build(api).GetOrgInboxAsync(Guid.NewGuid());

        Assert.Empty(result);
    }

    [Fact]
    public async Task SendOrgMessageAsync_PostsToCorrectUrl()
    {
        var orgId = Guid.NewGuid();
        var api   = ApiMock();
        api.Setup(x => x.PostAsync<SendOrgMessageRequest, OrgMessageRecord>(
                $"/api/organizations/{orgId}/messages",
                It.IsAny<SendOrgMessageRequest>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(new OrgMessageRecord { Id = Guid.NewGuid(), Body = "Welcome!", AuthorAppUserId = Guid.NewGuid() });

        var req = new SendOrgMessageRequest(OrgMessageChannel.OrgBroadcast, "Hello", "Welcome!", false, null, null, []);
        var result = await Build(api).SendOrgMessageAsync(orgId, req);

        Assert.NotNull(result);
        api.Verify(x => x.PostAsync<SendOrgMessageRequest, OrgMessageRecord>(
            $"/api/organizations/{orgId}/messages", req, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOrgMessageAsync_GetsFromCorrectUrl()
    {
        var orgId     = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var api       = ApiMock();
        api.Setup(x => x.GetAsync<OrgMessageRecord>(
                $"/api/organizations/{orgId}/messages/{messageId}", It.IsAny<CancellationToken>()))
           .ReturnsAsync(new OrgMessageRecord { Id = messageId, Body = "Test", AuthorAppUserId = Guid.NewGuid() });

        await Build(api).GetOrgMessageAsync(orgId, messageId);

        api.Verify(x => x.GetAsync<OrgMessageRecord>(
            $"/api/organizations/{orgId}/messages/{messageId}", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Calendar event types ──────────────────────────────────────────────────

    [Fact]
    public async Task GetCalendarEventTypesAsync_GetsFromCorrectUrl()
    {
        var orgId = Guid.NewGuid();
        var api   = ApiMock();
        api.Setup(x => x.GetAsync<IReadOnlyList<OrgCalendarEventTypeRecord>>(
                $"/api/organizations/{orgId}/calendar-event-types", It.IsAny<CancellationToken>()))
           .ReturnsAsync([new() { Id = Guid.NewGuid(), OrganizationId = orgId, Name = "Meeting" }]);

        var result = await Build(api).GetCalendarEventTypesAsync(orgId);

        Assert.Single(result);
        api.Verify(x => x.GetAsync<IReadOnlyList<OrgCalendarEventTypeRecord>>(
            $"/api/organizations/{orgId}/calendar-event-types", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateCalendarEventTypeAsync_PostsToCorrectUrl()
    {
        var orgId = Guid.NewGuid();
        var api   = ApiMock();
        api.Setup(x => x.PostAsync<UpsertCalendarEventTypeRequest, OrgCalendarEventTypeRecord>(
                $"/api/organizations/{orgId}/calendar-event-types",
                It.IsAny<UpsertCalendarEventTypeRequest>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(new OrgCalendarEventTypeRecord { Id = Guid.NewGuid(), OrganizationId = orgId, Name = "Investigation" });

        var req = new UpsertCalendarEventTypeRequest("Investigation", "text-danger", null, 1, true);
        await Build(api).CreateCalendarEventTypeAsync(orgId, req);

        api.Verify(x => x.PostAsync<UpsertCalendarEventTypeRequest, OrgCalendarEventTypeRecord>(
            $"/api/organizations/{orgId}/calendar-event-types", req, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Calendar events ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetCalendarEventsAsync_GetsFromCorrectUrl_NoFilter()
    {
        var orgId = Guid.NewGuid();
        var api   = ApiMock();
        api.Setup(x => x.GetAsync<IReadOnlyList<OrgCalendarEventRecord>>(
                $"/api/organizations/{orgId}/calendar", It.IsAny<CancellationToken>()))
           .ReturnsAsync([]);

        await Build(api).GetCalendarEventsAsync(orgId);

        api.Verify(x => x.GetAsync<IReadOnlyList<OrgCalendarEventRecord>>(
            $"/api/organizations/{orgId}/calendar", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCalendarEventsAsync_IncludesDateRangeInUrl()
    {
        var orgId = Guid.NewGuid();
        var api   = ApiMock();
        string? capturedUrl = null;
        api.Setup(x => x.GetAsync<IReadOnlyList<OrgCalendarEventRecord>>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Callback<string, CancellationToken>((url, _) => capturedUrl = url)
           .ReturnsAsync([]);

        var from = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var to   = new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc);
        await Build(api).GetCalendarEventsAsync(orgId, from, to);

        Assert.NotNull(capturedUrl);
        Assert.Contains("from=", capturedUrl);
        Assert.Contains("to=", capturedUrl);
    }

    [Fact]
    public async Task CreateCalendarEventAsync_PostsToCorrectUrl()
    {
        var orgId = Guid.NewGuid();
        var api   = ApiMock();
        var now   = DateTime.UtcNow;
        api.Setup(x => x.PostAsync<UpsertCalendarEventRequest, OrgCalendarEventRecord>(
                $"/api/organizations/{orgId}/calendar",
                It.IsAny<UpsertCalendarEventRequest>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(new OrgCalendarEventRecord { Id = Guid.NewGuid(), OrganizationId = orgId, Title = "Meeting" });

        var req = new UpsertCalendarEventRequest("Meeting", null, null, now, now.AddHours(1), false, false, null, null, null);
        await Build(api).CreateCalendarEventAsync(orgId, req);

        api.Verify(x => x.PostAsync<UpsertCalendarEventRequest, OrgCalendarEventRecord>(
            $"/api/organizations/{orgId}/calendar", req, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteCalendarEventAsync_DeletesCorrectUrl()
    {
        var orgId   = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var api     = ApiMock();
        api.Setup(x => x.DeleteAsync($"/api/organizations/{orgId}/calendar/{eventId}",
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(true);

        await Build(api).DeleteCalendarEventAsync(orgId, eventId);

        api.Verify(x => x.DeleteAsync(
            $"/api/organizations/{orgId}/calendar/{eventId}", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Attendees ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddCalendarAttendeeAsync_PostsToAttendeesUrl()
    {
        var orgId   = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var userId  = Guid.NewGuid();
        var api     = ApiMock();
        api.Setup(x => x.PostAsync<AddAttendeeRequest, OrgCalendarEventAttendeeRecord>(
                $"/api/organizations/{orgId}/calendar/{eventId}/attendees",
                It.IsAny<AddAttendeeRequest>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(new OrgCalendarEventAttendeeRecord
           {
               Id = Guid.NewGuid(), OrgCalendarEventId = eventId, AppUserId = userId,
               RsvpStatus = RsvpStatus.Invited,
           });

        var req = new AddAttendeeRequest(userId, "Lead investigator");
        var result = await Build(api).AddCalendarAttendeeAsync(orgId, eventId, req);

        Assert.Equal(RsvpStatus.Invited, result!.RsvpStatus);
        api.Verify(x => x.PostAsync<AddAttendeeRequest, OrgCalendarEventAttendeeRecord>(
            $"/api/organizations/{orgId}/calendar/{eventId}/attendees", req,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RsvpCalendarEventAsync_PutsToRsvpUrl()
    {
        var orgId      = Guid.NewGuid();
        var eventId    = Guid.NewGuid();
        var attendeeId = Guid.NewGuid();
        var api        = ApiMock();
        api.Setup(x => x.PutAsync<object, OrgCalendarEventAttendeeRecord>(
                $"/api/organizations/{orgId}/calendar/{eventId}/attendees/{attendeeId}/rsvp",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(new OrgCalendarEventAttendeeRecord
           {
               Id = attendeeId, OrgCalendarEventId = eventId, AppUserId = Guid.NewGuid(),
               RsvpStatus = RsvpStatus.Accepted,
           });

        var result = await Build(api).RsvpCalendarEventAsync(orgId, eventId, attendeeId, RsvpStatus.Accepted);

        Assert.Equal(RsvpStatus.Accepted, result!.RsvpStatus);
        api.Verify(x => x.PutAsync<object, OrgCalendarEventAttendeeRecord>(
            $"/api/organizations/{orgId}/calendar/{eventId}/attendees/{attendeeId}/rsvp",
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
