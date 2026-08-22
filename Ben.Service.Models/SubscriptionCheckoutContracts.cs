using Ben.Data.Common.Enums;

namespace Ben.Service.Models;

/// <summary>What a group is about to be charged, with the coupon line filled in or refused.</summary>
/// <param name="TierName">The band the group's current member count puts it on.</param>
/// <param name="Interval">The cadence being quoted.</param>
/// <param name="ListPrice">One period at that cadence, before any discount.</param>
/// <param name="Discount">The coupon line. Zero when no code was given or the code was refused.</param>
/// <param name="Payable">What would actually be charged.</param>
/// <param name="CouponRefusedBecause">
/// The sentence to show beside the coupon box when the typed code cannot be used — already
/// person-readable, never an HTTP status. Null when there was no code or the code applied.
/// </param>
/// <param name="CouponAppliesForPeriods">
/// How many periods the discount would run: 1, N, or null for forever. Shown so "20% off" and
/// "20% off your first month" cannot be confused at the moment of paying.
/// </param>
public record SubscriptionQuoteResponse(
    string TierName,
    BillingInterval Interval,
    decimal ListPrice,
    decimal Discount,
    decimal Payable,
    string? CouponRefusedBecause,
    int? CouponAppliesForPeriods);

/// <summary>Asking what a period would cost, optionally with a coupon code typed in.</summary>
public record SubscriptionQuoteRequest(BillingInterval Interval, string? CouponCode);
