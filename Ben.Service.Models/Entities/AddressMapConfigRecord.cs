namespace Ben.Service.Models.Entities;

/// <summary>Flat record matching OrganizationAddressMapConfig for API transport.</summary>
public record AddressMapConfigRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationAddressId { get; init; }
    public bool IsOnMap { get; init; }
    public bool ShowMarker { get; init; } = true;
    public bool ShowRegion { get; init; }
    public double RegionRadiusMiles { get; init; } = 1.0;
    public string MarkerColor { get; init; } = "#e63535";
    public string? MarkerIconKey { get; init; }
    public string RegionFillColor { get; init; } = "#3388ff";
    public double RegionFillOpacity { get; init; } = 0.2;
    public string RegionStrokeColor { get; init; } = "#1155cc";
    public double RegionStrokeOpacity { get; init; } = 0.8;
    public double RegionStrokeWidth { get; init; } = 2.0;
}
