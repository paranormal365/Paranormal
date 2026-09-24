using System.Globalization;

namespace Ben.Service.Models.Store;

/// <summary>
/// Dollars and cents for the gear store (storefront).
/// </summary>
/// <remarks>
/// <para>Stripe counts in whole cents; the database stores <c>decimal(18,2)</c> dollars. Every
/// conversion between the two goes through <see cref="Cents"/>, so an amount sent to Stripe and the
/// amount written on the order can never be rounded two different ways. Half a cent rounds AWAY
/// from zero — the register rule <c>TaxResolver.TaxOn</c> already uses — never to even.</para>
///
/// <para>Shared by the API and the website, so a total the page shows is formatted by the same
/// method as the one on the letter.</para>
/// </remarks>
public static class StoreMoney
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    /// <summary>The store's one currency, as Stripe spells it.</summary>
    public const string Currency = "usd";

    /// <summary>An amount in whole cents, half a cent rounding away from zero.</summary>
    public static long Cents(decimal dollars)
        => (long)Math.Round(dollars * 100m, 0, MidpointRounding.AwayFromZero);

    public static decimal FromCents(long cents) => cents / 100m;

    /// <summary>Rounds to the cent, half away from zero.</summary>
    public static decimal Round(decimal dollars) => Math.Round(dollars, 2, MidpointRounding.AwayFromZero);

    /// <summary>"$1,234.50" — always two places, whatever the server's culture.</summary>
    public static string Format(decimal dollars) => dollars.ToString("C2", Us);
}
