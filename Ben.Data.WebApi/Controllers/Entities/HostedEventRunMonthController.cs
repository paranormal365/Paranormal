using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Extra months for one show's run: how many it has, buying more, and applying months held (10/24/2026).
/// See <see cref="EventRunMonth"/> for Ben's rules.
/// </summary>
/// <remarks>
/// The same permission every change to an event needs, because each of these lengthens it; buying as the group
/// also spends the group's money, which the same key already covers for credits.
/// </remarks>
[Route("api/organizations/{orgId:guid}/events/{eventId:guid}/run-months")]
public sealed class HostedEventRunMonthController : OrgCmsControllerBase
{
    private readonly IStripeGateway _stripe;
    private readonly IConfiguration _configuration;

    public HostedEventRunMonthController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper, IOrganizationSecurityService security,
        IStripeGateway stripe, IConfiguration configuration)
        : base(dbFactory, mapper, security)
    { _stripe = stripe; _configuration = configuration; }

    [HttpGet]
    public async Task<ActionResult<EventRunMonthsRecord>> Get(Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await MayChangeAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var show = await ShowAsync(db, orgId, eventId, ct);
        if (show is null) return NotFound();
        return Ok(await DescribeAsync(db, show, orgId, userId.Value, ct));
    }

    /// <summary>Opens a checkout for <paramref name="count"/> months, paid by the group or by the reader.</summary>
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(Services.RateLimiting.HostedBookingPolicy)]
    [HttpPost("checkout")]
    public async Task<ActionResult<StartCheckoutResponse>> Checkout(
        Guid orgId, Guid eventId, [FromQuery] int count, [FromQuery] bool asGroup, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await MayChangeAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var show = await ShowAsync(db, orgId, eventId, ct);
        if (show is null) return NotFound();

        string buyer;
        string? customerRef = null;
        if (asGroup)
        {
            buyer = await db.Organizations.AsNoTracking().Where(o => o.Id == orgId).Select(o => o.Name).FirstAsync(ct);
            customerRef = await db.OrganizationSubscriptions.AsNoTracking()
                .Where(x => x.OrganizationId == orgId).Select(x => x.ProviderCustomerRef).FirstOrDefaultAsync(ct);
        }
        else
        {
            buyer = await db.AppUsers.AsNoTracking().Where(u => u.Id == userId.Value)
                .Select(u => u.DisplayName ?? u.Email ?? "IsHaunted member").FirstAsync(ct);
        }

        var baseUrl = (_configuration["AppBaseUrl"] ?? "").TrimEnd('/');
        var outcome = await EventRunMonthCheckout.StartAsync(
            db, _stripe, show.Id, asGroup ? orgId : null, buyer, customerRef, userId.Value, count,
            returnUrl: $"{baseUrl}/organizations/{orgId}/events/{eventId}", ct);

        return outcome switch
        {
            { Response: { } ok } => Ok(ok),
            { Refusal: { } why } => BadRequest(why),
            _ => Problem(outcome.Unavailable, statusCode: 503),
        };
    }

    /// <summary>
    /// Applies months already held — the group's, or the reader's own — to this show. Free: they were paid for.
    /// Oldest-lapsing first. Never a credit: publishing doesn't look here.
    /// </summary>
    [HttpPost("apply")]
    public async Task<ActionResult<EventRunMonthsRecord>> Apply(
        Guid orgId, Guid eventId, [FromQuery] int count, [FromQuery] bool fromGroup, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await MayChangeAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var show = await ShowAsync(db, orgId, eventId, ct);
        if (show is null) return NotFound();

        var now = DateTime.UtcNow;
        var (_, cap) = await EventRunMonths.ReadAsync(db, ct);
        var applied = await EventRunMonths.AppliedToAsync(db, show.Id, ct);
        if (EventRunMonths.WhyNotExtend(show, applied, count, cap, LastEnds(show), now) is { } why)
            return BadRequest(why);

        var held = await Held(db, fromGroup ? orgId : null, fromGroup ? null : userId.Value, now)
            .OrderBy(m => m.ExpiresUtc).Take(count).ToListAsync(ct);
        if (held.Count < count)
            return BadRequest(held.Count == 0
                ? (fromGroup ? "The group holds no extra months." : "You hold no extra months.")
                : $"Only {held.Count} {(held.Count == 1 ? "month is" : "months are")} held. Apply {held.Count} or fewer.");

        foreach (var m in held)
        {
            m.AppliedToHostedEventId = show.Id;
            m.AppliedUtc = now;
            m.DateUpdated = now;
            m.UpdatedByAppUserId = userId.Value;
        }
        await db.SaveChangesAsync(ct);
        return Ok(await DescribeAsync(db, show, orgId, userId.Value, ct));
    }

    private Task<bool> MayChangeAsync(Guid userId, Guid orgId, CancellationToken ct)
        => IsCmsAuthorizedAsync(userId, orgId, OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct);

    private static Task<HostedEvent?> ShowAsync(BenDataContext db, Guid orgId, Guid eventId, CancellationToken ct)
        => db.HostedEvents.AsNoTracking().Include(e => e.Nights)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);

    private static DateTime LastEnds(HostedEvent show)
        => HostedEventCalendarSync.Window(show, [.. show.Nights.OrderBy(n => n.Date)]).EndUtc;

    private static IQueryable<EventRunMonth> Held(BenDataContext db, Guid? orgId, Guid? personId, DateTime now)
        => db.EventRunMonths.Where(m => m.AppliedToHostedEventId == null && m.RefundedUtc == null && m.ExpiresUtc > now
                                     && (orgId != null ? m.OwnerOrganizationId == orgId : m.OwnerAppUserId == personId));

    private static async Task<EventRunMonthsRecord> DescribeAsync(
        BenDataContext db, HostedEvent show, Guid orgId, Guid userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var offer = await EventCreditOffer.ReadAsync(db, ct);
        var (percent, cap) = await EventRunMonths.ReadAsync(db, ct);
        var applied = await EventRunMonths.AppliedToAsync(db, show.Id, ct);
        var subscriber = await EventCreditOffer.IsPaidSubscriberAsync(db, orgId, now, ct);
        var first = show.Nights.OrderBy(n => n.Date).Select(n => (DateTime?)n.Date).FirstOrDefault();
        return new EventRunMonthsRecord(
            show.DatesAreSeparate, applied, cap,
            show.DatesAreSeparate && first is { } f ? EventRunMonths.LimitFor(f, applied).AddDays(-1) : null,
            EventRunMonths.PriceOfAMonth(offer, subscriber, percent),
            EventRunMonths.PriceOfAMonth(offer, false, percent),
            await Held(db, orgId, null, now).CountAsync(ct),
            await Held(db, null, userId, now).CountAsync(ct),
            EventRunMonths.WhyNotExtend(show, applied, 1, cap, LastEnds(show), now),
            offer.OnSale);
    }
}
