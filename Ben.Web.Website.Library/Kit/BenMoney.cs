using System.Globalization;

namespace Ben.Web.Website.Library.Kit;

/// <summary>
/// A price as a person reads it: whole dollars without a ".00", and cents when there are cents.
/// </summary>
/// <remarks>
/// Six screens printed prices with <c>C0</c>, which rounds — and the pricing page's own comment
/// said that was "honest only because the bands are priced in whole dollars". The live bands are
/// $19.99, $39.99, $59.99 and $99.99, so the page a group buys from was quoting $20, $40, $60 and
/// $100 (found 2026-10-01 while the home page started reading the same rows). One rule, here.
/// </remarks>
public static class BenMoney
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    public static string Format(decimal amount)
        => amount % 1 == 0 ? amount.ToString("C0", Us) : amount.ToString("C2", Us);
}
