using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Data.Common.Enums;

namespace Ben.Web.Services;

/// <summary>
/// Defines the SuperAdmin operations available to Blazor library components.
/// </summary>
/// <remarks>
/// Implemented by <c>BenAdminClientAdapter</c> in <c>Ben.Web.WebApp</c>, which
/// delegates every call to the typed <c>IWebApiClient</c> HTTP client.
/// Library components depend on this interface so that <c>Ben.Web.Library</c>
/// does not need a direct reference to the WebApp project.
/// <para>
/// All methods require an active SuperAdmin bearer token; calls made by a
/// non-SuperAdmin session will be rejected by the WebApi with HTTP 403.
/// </para>
/// </remarks>
public interface IBenAdminClient :
    IBenOrganizationClient,
    IBenMembershipClient,
    IBenCaseClient,
    IBenInvestigationClient,
    IBenUserClient,
    IBenMediaClient,
    IBenCmsClient,
    IBenPlacesClient,
    IBenPlatformClient,
    IBenEquipmentClient,
    IBenAccountClient,
    IBenFeedClient,
    IBenPublicationClient
{
    // Every member now lives in one of the slices above. This interface remains the single name
    // components inject and the single thing BenAdminClientAdapter implements, so nothing that
    // uses it had to change — the split is about where the declarations live and what a new
    // caller is allowed to depend on, not about rewriting call sites.
}
