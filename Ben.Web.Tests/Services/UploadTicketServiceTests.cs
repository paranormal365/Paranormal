using Ben.Web.Website.Services;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The ticket that lets browser JavaScript send upload chunks without ever holding the bearer
/// token: bound to one session, opaque in transit, worthless anywhere else.
/// </summary>
public class UploadTicketServiceTests
{
    private static UploadTicketService Create()
        => new(new EphemeralDataProtectionProvider());

    [Fact]
    public void A_ticket_round_trips_for_its_own_session()
    {
        var svc = Create();
        var sessionId = Guid.NewGuid();

        var ticket = svc.Protect(sessionId, "the-access-token");

        Assert.DoesNotContain("the-access-token", ticket);   // opaque in a URL, a log, a referrer
        Assert.Equal("the-access-token", svc.Unprotect(sessionId, ticket));
    }

    [Fact]
    public void A_ticket_lifted_from_one_session_opens_no_other()
    {
        var svc = Create();
        var ticket = svc.Protect(Guid.NewGuid(), "token");

        Assert.Null(svc.Unprotect(Guid.NewGuid(), ticket));
    }

    [Fact]
    public void A_tampered_or_foreign_ticket_reads_as_nothing()
    {
        var svc = Create();
        var sessionId = Guid.NewGuid();

        Assert.Null(svc.Unprotect(sessionId, "not-a-ticket"));

        // Minted under a different key ring entirely — a ticket from another machine or epoch.
        var foreign = Create().Protect(sessionId, "token");
        Assert.Null(svc.Unprotect(sessionId, foreign));
    }

    [Fact]
    public void Upload_and_media_tickets_are_not_interchangeable()
    {
        // Same key ring, different protector purposes: a media ticket must never authorise an
        // upload chunk, nor the reverse.
        var provider = new EphemeralDataProtectionProvider();
        var uploads = new UploadTicketService(provider);
        var media = new MediaTicketService(provider);
        var id = Guid.NewGuid();

        Assert.Null(uploads.Unprotect(id, media.Protect(id, "token")));
        Assert.Null(media.Unprotect(id, uploads.Protect(id, "token")));
    }
}
