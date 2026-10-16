using Ben.Service.Models.Entities;
using Ben.Web.Services;
using Ben.Web.Services.WebApi;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The adapter calls added or changed for the 10/16/2026 gaps keep the server's sentence.
/// </summary>
/// <remarks>
/// Each one used to return a bare null or false, so the screen made up its own reason: check-in
/// said "Check the arrival time is not in the future." whatever had gone wrong, and Attended said
/// "That could not be saved."
/// </remarks>
public class Oct16GapsAdapterTests
{
    private static BenAdminClientAdapter Build(Mock<IWebApiClient> api)
        => new(api.Object, new Mock<IWebApiAuthService>().Object,
               Microsoft.Extensions.Options.Options.Create(new WebApiOptions()));

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid VisitId = Guid.NewGuid();
    private static string VisitUrl => $"/api/organizations/{OrgId}/investigations/{VisitId}";

    [Fact]
    public async Task Check_in_keeps_the_servers_sentence()
    {
        var api = new Mock<IWebApiClient>();
        api.Setup(x => x.SendExpectingReasonAsync<object, InvestigationRosterEntry>(
                HttpMethod.Post, $"{VisitUrl}/check-in", It.IsAny<object>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((null, "You've been signed out. Sign in again."));

        var (entry, error) = await Build(api).CheckInToInvestigationAsync(OrgId, VisitId, null);

        Assert.Null(entry);
        Assert.Equal("You've been signed out. Sign in again.", error);
    }

    [Fact]
    public async Task Attended_keeps_the_servers_sentence()
    {
        var rowId = Guid.NewGuid();
        var api = new Mock<IWebApiClient>();
        api.Setup(x => x.SendExpectingReasonAsync<object, InvestigationRosterEntry>(
                HttpMethod.Put, $"{VisitUrl}/attendees/{rowId}/attendance", It.IsAny<object>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((null, "Only whoever runs this visit can say who was there."));

        var (entry, error) = await Build(api).OverrideInvestigationAttendanceAsync(OrgId, VisitId, rowId, true);

        Assert.Null(entry);
        Assert.Equal("Only whoever runs this visit can say who was there.", error);
    }

    [Fact]
    public async Task Changing_a_caseless_visit_puts_to_its_address_and_reads_success_from_the_status()
    {
        var api = new Mock<IWebApiClient>();
        api.Setup(x => x.SendWithStatusAsync<UpdateOrgInvestigationRequest, object>(
                HttpMethod.Put, VisitUrl, It.IsAny<UpdateOrgInvestigationRequest?>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((null, null, 204));

        var (saved, error) = await Build(api).UpdateOrgInvestigationAsync(
            OrgId, VisitId, new UpdateOrgInvestigationRequest("Night", DateTime.UtcNow));

        Assert.True(saved);
        Assert.Null(error);
    }

    [Fact]
    public async Task A_refused_change_says_why()
    {
        var api = new Mock<IWebApiClient>();
        api.Setup(x => x.SendWithStatusAsync<UpdateOrgInvestigationRequest, object>(
                HttpMethod.Put, VisitUrl, It.IsAny<UpdateOrgInvestigationRequest?>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((null, "The end has to come after the start.", 400));

        var (saved, error) = await Build(api).UpdateOrgInvestigationAsync(
            OrgId, VisitId, new UpdateOrgInvestigationRequest("Night", DateTime.UtcNow));

        Assert.False(saved);
        Assert.Equal("The end has to come after the start.", error);
    }

    [Fact]
    public async Task A_refused_delete_says_why_and_a_refused_cancel_never_reads_as_done()
    {
        var api = new Mock<IWebApiClient>();
        api.Setup(x => x.SendWithStatusAsync<object, object>(
                HttpMethod.Delete, VisitUrl, It.IsAny<object?>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((null, "This visit has 1 recording, and deleting it would lose them. Cancel it instead.", 409));
        api.Setup(x => x.SendWithStatusAsync<object, object>(
                HttpMethod.Post, $"{VisitUrl}/cancel", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((null, null, 403));

        var (deleted, why) = await Build(api).DeleteOrgInvestigationAsync(OrgId, VisitId);
        var (canceled, cancelWhy) = await Build(api).CancelOrgInvestigationAsync(OrgId, VisitId);

        Assert.False(deleted);
        Assert.StartsWith("This visit has 1 recording", why);
        Assert.False(canceled);
        Assert.Contains("still scheduled", cancelWhy);
    }

    [Fact]
    public async Task Taking_a_guest_off_an_event_keeps_the_servers_sentence()
    {
        var eventId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var api = new Mock<IWebApiClient>();
        api.Setup(x => x.DeleteExpectingReasonAsync(
                $"/api/organizations/{OrgId}/calendar/{eventId}/attendees/{rowId}", It.IsAny<CancellationToken>()))
           .ReturnsAsync((false, "This guest list is answered from the event's Bookings page."));

        var (removed, error) = await Build(api).RemoveCalendarAttendeeAsync(OrgId, eventId, rowId);

        Assert.False(removed);
        Assert.Equal("This guest list is answered from the event's Bookings page.", error);
    }
}
