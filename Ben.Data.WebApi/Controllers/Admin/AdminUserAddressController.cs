using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Admin;
using Ben.Service.RepositoryService.Services;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Admin;

[Route("api/admin/user-addresses")]
public sealed class AdminUserAddressController : AdminEntityControllerBase<UserAddress, UserAddressAdminRecord>
{
    public AdminUserAddressController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper, IAuditLogService auditLog)
        : base(dbContextFactory, mapper, auditLog)
    {
    }

    public override async Task<ActionResult<UserAddressAdminRecord>> Create([FromBody] UserAddress entity, CancellationToken cancellationToken)
    {
        await ApplyGeocodingAsync(entity, cancellationToken);
        return await base.Create(entity, cancellationToken);
    }

    public override async Task<ActionResult<UserAddressAdminRecord>> Update(Guid id, [FromBody] UserAddress entity, CancellationToken cancellationToken)
    {
        await ApplyGeocodingAsync(entity, cancellationToken);
        return await base.Update(id, entity, cancellationToken);
    }

    private static async Task ApplyGeocodingAsync(UserAddress entity, CancellationToken cancellationToken)
    {
        // If the client already resolved coordinates (from the live preview), use them as-is.
        if (entity.Latitude.HasValue && entity.Longitude.HasValue)
            return;

        var result = await AddressGeocodingService.TryResolveCoordinatesAsync(
            entity.StreetAddress1, entity.StreetAddress2,
            entity.City, entity.State, entity.ZipCode, entity.Country, cancellationToken);
        entity.Latitude              = result.Latitude;
        entity.Longitude             = result.Longitude;
        entity.GeocodingResponseJson = result.RawResponseJson;
        entity.GeocodingResultType   = result.ResultType;
    }
}
