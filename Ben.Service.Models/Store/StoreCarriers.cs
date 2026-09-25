namespace Ben.Service.Models.Store;

/// <summary>
/// The carriers an order can ship with, and the tracking page for each (storefront).
/// </summary>
/// <remarks>
/// The admin picks a carrier and types the tracking number; the link in the shipped letter and on
/// the order page is built here rather than typed, so a mistyped URL cannot reach a buyer. "Other"
/// has no link — the admin may paste one, which <c>StoreOrderTransitions.ShipAsync</c> accepts only
/// as an https URL.
/// </remarks>
public static class StoreCarriers
{
    public const string Ups = "UPS";
    public const string Usps = "USPS";
    public const string FedEx = "FedEx";
    public const string Dhl = "DHL";
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> All = [Usps, Ups, FedEx, Dhl, Other];

    public static bool IsKnown(string? carrier) => carrier is not null && All.Contains(carrier, StringComparer.Ordinal);

    /// <summary>The carrier's tracking page for a number, or null for "Other", an unknown carrier or no number.</summary>
    public static string? TrackingUrl(string? carrier, string? trackingNumber)
    {
        var number = trackingNumber?.Trim();
        if (string.IsNullOrEmpty(number)) return null;
        var n = Uri.EscapeDataString(number);
        return carrier switch
        {
            Usps  => $"https://tools.usps.com/go/TrackConfirmAction?tLabels={n}",
            Ups   => $"https://www.ups.com/track?tracknum={n}",
            FedEx => $"https://www.fedex.com/fedextrack/?trknbr={n}",
            Dhl   => $"https://www.dhl.com/us-en/home/tracking/tracking-express.html?submit=1&tracking-id={n}",
            _     => null,
        };
    }
}
