using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Overflow seats through the Stripe rails (item 144 meets phases 1–3).
/// </summary>
/// <remarks>
/// A seat is one member's flat-priced ride on a band the group already bought, so the tests pin
/// what makes it NOT a subscription: no PeriodOpener, no snapshot, the group's period untouched,
/// the frozen offer price forever — and the renewal rule that a member who left is never charged
/// for a seat they no longer occupy.
/// </remarks>
public sealed class StripeSeatTests
{
    private sealed class FakeGateway : IStripeGateway
    {
        public readonly List<StripeRenewalCharge> Charges = [];
        public bool IsConfigured => true;
        public Task<StripeCheckoutHandle> CreateCheckoutSessionAsync(StripeCheckoutSpec spec, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<StripeChargeOutcome> ChargeSavedCardAsync(StripeRenewalCharge charge, CancellationToken ct)
        {
            Charges.Add(charge);
            return Task.FromResult(new StripeChargeOutcome(true, $"pi_seat_{Charges.Count}", null));
        }
        public StripeCompletedCheckout? ParseCompletedCheckout(string payload, string signatureHeader)
            => throw new NotSupportedException();
    }

    private static IDbContextFactory<BenDataContext> Db()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static StripeFulfillmentService Fulfillment(IDbContextFactory<BenDataContext> db)
        => new(db, NullLogger<StripeFulfillmentService>.Instance);

    private sealed record Seeded(Guid OrgId, Guid MemberId, Guid SeatId);

    private static async Task<Seeded> SeedSeatAsync(
        IDbContextFactory<BenDataContext> factory,
        SubscriptionStatus seatStatus = SubscriptionStatus.PendingPayment,
        DateTime? periodEnd = null, bool memberStillActive = true)
    {
        await using var db = await factory.CreateDbContextAsync();
        var memberId = Guid.NewGuid();
        db.AppUsers.Add(new AppUser
        {
            Id = memberId, UserName = "m@t.com", NormalizedUserName = "M@T.COM",
            Email = "m@t.com", DisplayName = "Emma Rodriguez", DateCreated = DateTime.UtcNow,
        });
        var orgId = Guid.NewGuid();
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Full House", UrlName = $"fh-{Guid.NewGuid():N}"[..12],
            CreatedByAppUserId = memberId, DateCreated = DateTime.UtcNow,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = memberId,
            Role = OrganizationMemberRole.Member, IsActive = memberStillActive,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = memberId,
        });
        var seatId = Guid.NewGuid();
        db.MemberSeatSubscriptions.Add(new MemberSeatSubscription
        {
            Id = seatId, OrganizationId = orgId, AppUserId = memberId,
            Status = seatStatus, Interval = BillingInterval.Monthly, PriceAtStart = 5m,
            CurrentPeriodStart = seatStatus == SubscriptionStatus.Active ? DateTime.UtcNow.AddMonths(-1) : null,
            CurrentPeriodEnd = periodEnd,
            ProviderName = seatStatus == SubscriptionStatus.Active ? "Stripe" : null,
            ProviderCustomerRef = seatStatus == SubscriptionStatus.Active ? "cus_member" : null,
            ProviderPaymentMethodRef = seatStatus == SubscriptionStatus.Active ? "pm_member" : null,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = memberId,
        });
        await db.SaveChangesAsync();
        return new Seeded(orgId, memberId, seatId);
    }

    private static StripeCompletedCheckout SeatCheckout(Guid seatId, string intent = "pi_seat_pay")
        => new($"cs_{intent}", intent, "cus_member", "pm_member",
               new Dictionary<string, string>
               { [StripeFulfillmentService.CheckoutFacts.Keys.Seat] = seatId.ToString() });

    [Fact]
    public async Task A_paid_seat_activates_with_the_frozen_price_and_the_member_named_on_the_ledger()
    {
        var factory = Db();
        var seed = await SeedSeatAsync(factory);

        await Fulfillment(factory).FulfillAsync(SeatCheckout(seed.SeatId));

        await using var db = await factory.CreateDbContextAsync();
        var seat = await db.MemberSeatSubscriptions.SingleAsync();
        Assert.Equal(SubscriptionStatus.Active, seat.Status);
        Assert.Equal("Stripe", seat.ProviderName);
        Assert.Equal("pm_member", seat.ProviderPaymentMethodRef);
        Assert.Equal(seat.CurrentPeriodStart!.Value.AddMonths(1), seat.CurrentPeriodEnd);

        var payment = await db.BillingLedgerEntries.SingleAsync(e => e.Kind == BillingLedgerKind.Payment);
        Assert.Equal(5m, payment.Amount);
        Assert.Contains("Emma Rodriguez", payment.Description);
        Assert.NotNull(payment.ReceiptNumber);
        // The payer owns the row — which is what lets them reprint their own receipt without
        // holding the group's settings keys.
        Assert.Equal(seed.MemberId, payment.CreatedByAppUserId);

        // A seat buys ONE ride, never the group's machinery: no period was opened, no contract
        // snapshotted, and the group's subscription table is exactly as empty as it started.
        Assert.Empty(db.OrganizationSubscriptions);
        Assert.Empty(db.SubscriptionContractTerms);
    }

    [Fact]
    public async Task A_seat_payment_delivered_twice_is_one_activation_and_one_receipt()
    {
        var factory = Db();
        var seed = await SeedSeatAsync(factory);
        var checkout = SeatCheckout(seed.SeatId);

        await Fulfillment(factory).FulfillAsync(checkout);
        await Fulfillment(factory).FulfillAsync(checkout);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.BillingLedgerEntries.CountAsync(e => e.Kind == BillingLedgerKind.Payment));
    }

    [Fact]
    public async Task A_due_seat_renews_at_its_frozen_price_stitched_onto_the_old_period()
    {
        var factory = Db();
        var end = DateTime.UtcNow.AddHours(6);
        var seed = await SeedSeatAsync(factory, SubscriptionStatus.Active, periodEnd: end);

        var gateway = new FakeGateway();
        await new StripeRenewalJob(factory, gateway, Fulfillment(factory),
            NullLogger<StripeRenewalJob>.Instance).RunAsync(default);

        var charge = Assert.Single(gateway.Charges);
        Assert.Equal(5m, charge.Total);
        Assert.Equal("cus_member", charge.CustomerRef);

        await using var db = await factory.CreateDbContextAsync();
        var seat = await db.MemberSeatSubscriptions.SingleAsync();
        Assert.Equal(end, seat.CurrentPeriodStart);
        Assert.Equal(end.AddMonths(1), seat.CurrentPeriodEnd);
    }

    [Fact]
    public async Task A_member_who_left_is_never_charged_for_the_seat_they_no_longer_occupy()
    {
        var factory = Db();
        await SeedSeatAsync(factory, SubscriptionStatus.Active,
            periodEnd: DateTime.UtcNow.AddHours(6), memberStillActive: false);

        var gateway = new FakeGateway();
        await new StripeRenewalJob(factory, gateway, Fulfillment(factory),
            NullLogger<StripeRenewalJob>.Instance).RunAsync(default);

        Assert.Empty(gateway.Charges);   // the seat simply runs out
    }
}
