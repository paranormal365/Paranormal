using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.RepositoryService.EntityInterfaces;
using Ben.Service.RepositoryService.GenericInterfaces;
using Ben.Service.RepositoryService.Services;
using Microsoft.EntityFrameworkCore;

namespace Ben.Service.RepositoryService.Repositories;

public class UserAddressRepository : RepositoryBase<UserAddress>, IUserAddressRepository
{
    public UserAddressRepository(IDbContextFactory<BenDataContext> context) : base(context)
    {
    }

    public override void Create(UserAddress item)
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

    public override void Update(UserAddress item)
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
