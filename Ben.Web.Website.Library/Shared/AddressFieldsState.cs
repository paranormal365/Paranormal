namespace Ben.Web.Website.Library.Shared;

/// <summary>
/// Mutable address fields shared between <see cref="AddressFieldsWithMap"/>
/// and any parent form that needs to read or pre-populate address fields.
/// Latitude and Longitude are written by the component whenever geocoding
/// resolves successfully, so the parent can include them in the save request.
/// </summary>
public class AddressFieldsState
{
    public string  StreetAddress1 { get; set; } = "";
    public string? StreetAddress2 { get; set; }
    public string  City           { get; set; } = "";
    public string  State          { get; set; } = "";
    public string  ZipCode        { get; set; } = "";
    public string  Country        { get; set; } = "US";

    /// <summary>Set by AddressFieldsWithMap after successful geocoding. Null when not yet resolved.</summary>
    public decimal? Latitude  { get; set; }
    /// <summary>Set by AddressFieldsWithMap after successful geocoding. Null when not yet resolved.</summary>
    public decimal? Longitude { get; set; }
}
