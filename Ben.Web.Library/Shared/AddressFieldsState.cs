namespace Ben.Web.Library.Shared;

/// <summary>
/// Mutable address fields shared between <see cref="AddressFieldsWithMap"/>
/// and any parent form that needs to read or pre-populate address fields.
/// </summary>
public class AddressFieldsState
{
    public string  StreetAddress1 { get; set; } = "";
    public string? StreetAddress2 { get; set; }
    public string  City           { get; set; } = "";
    public string  State          { get; set; } = "";
    public string  ZipCode        { get; set; } = "";
    public string  Country        { get; set; } = "US";
}
