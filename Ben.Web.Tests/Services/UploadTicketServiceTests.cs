using Ben.Web.Website.Services;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The ticket that lets browser JavaScript send upload chunks without ever holding the bearer
/// token: bound to one session, opaque in transit, worthless anywhere else.
/// </summary>
public class UploadTicketServiceTests
{
    private static BrowserTicketStore NewStore() => new(new MemoryCache(new MemoryCacheOptions()));

    private static UploadTicketService Create() => new(NewStore());

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

        // Issued by a different store entirely — another machine, or this one before a restart.
        // A handle means nothing without the server that minted it, which is the point of it
        // carrying no payload (item 201).
        var foreign = Create().Protect(sessionId, "token");
        Assert.Null(svc.Unprotect(sessionId, foreign));
    }

    [Fact]
    public void Upload_and_media_tickets_are_not_interchangeable()
    {
        // ONE store, as deployed — both services resolve the same singleton — so this really does
        // test the scope separation rather than two isolated caches never seeing each other's
        // handles. A media ticket must never authorise an upload chunk, nor the reverse: their
        // lifetimes differ by eleven hours.
        var store = NewStore();
        var uploads = new UploadTicketService(store);
        var media = new MediaTicketService(store);
        var id = Guid.NewGuid();

        Assert.Null(uploads.Unprotect(id, media.Protect(id, "token")));
        Assert.Null(media.Unprotect(id, uploads.Protect(id, "token")));
    }
}
