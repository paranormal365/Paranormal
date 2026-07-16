using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.RepositoryService.EntityInterfaces;
using Ben.Service.RepositoryService.GenericInterfaces;
using Ben.Service.RepositoryService.Services;
using Microsoft.EntityFrameworkCore;

namespace Ben.Service.RepositoryService.Repositories;

public class OrganizationAddressRepository : RepositoryBase<OrganizationAddress>, IOrganizationAddressRepository
{
    public OrganizationAddressRepository(IDbContextFactory<BenDataContext> context) : base(context)
    {
    }

    public override void Create(OrganizationAddress item)
    {
        var geocodingResult = AddressGeocodingService.TryResolveCoordinates(
            item.StreetAddress1,
            item.StreetAddress2,
            item.City,
            item.State,
            item.ZipCode,
            item.Country);

        item.Latitude = geocodingResult.Latitude;
        item.Longitude = geocodingResult.Longitude;
        item.GeocodingResponseJson = geocodingResult.RawResponseJson;
        item.GeocodingResultType = geocodingResult.ResultType;

        base.Create(item);
    }

    public override void Update(OrganizationAddress item)
    {
        var geocodingResult = AddressGeocodingService.TryResolveCoordinates(
            item.StreetAddress1,
            item.StreetAddress2,
            item.City,
            item.State,
            item.ZipCode,
            item.Country);

        item.Latitude = geocodingResult.Latitude;
        item.Longitude = geocodingResult.Longitude;
        item.GeocodingResponseJson = geocodingResult.RawResponseJson;
        item.GeocodingResultType = geocodingResult.ResultType;

        base.Update(item);
    }
}
