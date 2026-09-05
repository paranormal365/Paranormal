using Ben.Data.Common.Constants;
using Microsoft.EntityFrameworkCore;
using Ben.Data.WebApi.Services;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Provides endpoints for discovering and managing the calling user's organization memberships.
/// </summary>
/// <remarks>
/// All endpoints require an authenticated user (<c>[Authorize]</c>).
/// Privileged operations such as managing <em>other</em> users' memberships are
/// handled by <see cref="OrganizationSecurityController"/>.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/security/organizations")]
public class OrganizationMembershipController : BenControllerBase
{
    private readonly IOrganizationSecurityService _organizationSecurityService;
    private readonly SiteSettingsService _siteSettings;

    /// <summary>Initialises the controller with its required service dependencies.</summary>
    public OrganizationMembershipController(
        IOrganizationSecurityService organizationSecurityService,
        SiteSettingsService siteSettings)
    {
        _organizationSecurityService = organizationSecurityService;
        _siteSettings = siteSettings;
    }

    /// <summary>Searches for users within the calling user's security scope.</summary>
    /// <param name="q">Optional free-text query filtered against <c>Email</c>, <c>UserName</c>, and <c>DisplayName</c>.</param>
    /// <param name="skip">Zero-based pagination offset.</param>
    /// <param name="take">Maximum results to return (server-side clamped to 100).</param>
    /// <param name="cancellationToken">Propagates cancellation from the HTTP request.</param>
    /// <remarks>SuperAdmins see all users; others see only users sharing an active organization membership.</remarks>
    [HttpGet("users/search")]
    public async Task<ActionResult<IEnumerable<UserSearchResultResponse>>> SearchUsers(
        [FromQuery] string? q,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25,
        CancellationToken cancellationToken = default)
    {
        var actingUserId = GetCurrentUserIdOrThrow();
        var users = await _organizationSecurityService.SearchUsersAsync(actingUserId, q, skip, take, cancellationToken);

        return Ok(users.Select(u => new UserSearchResultResponse
        {
            AppUserId = u.Id,
            DisplayName = u.DisplayName,
            UserName = u.UserName,
            Email = u.Email
        }));
    }

    /// <summary>Returns all organizations the authenticated user is an active member of.</summary>
    /// <param name="cancellationToken">Propagates cancellation from the HTTP request.</param>
    /// <remarks>SuperAdmins receive every organization in the system.</remarks>
    [HttpGet("mine")]
    public async Task<ActionResult<IEnumerable<OrganizationSummaryResponse>>> GetMyOrganizations(CancellationToken cancellationToken)
    {
        var appUserId = GetCurrentUserIdOrThrow();
        var organizations = await _organizationSecurityService.GetOrganizationsForUserAsync(appUserId, cancellationToken);

        return Ok(organizations.Select(o => new OrganizationSummaryResponse
        {
            OrganizationId = o.Id,
            Name = o.Name,
            UrlName = o.UrlName,
            DateCreated = o.DateCreated,
            CreatedByAppUserId = o.CreatedByAppUserId
        }));
    }

    /// <summary>
    /// The groups the caller actually belongs to — for the sidebar (item 159).
    /// </summary>
    /// <remarks>
    /// Deliberately NOT <see cref="GetMyOrganizations"/>: that one expands to every organization
    /// for a SuperAdmin, which is right for an admin list and wrong for a menu that says "your
    /// groups". This reads membership rows only, so a SuperAdmin sees the groups they are a
    /// member of, and an impersonated session sees the impersonated person's — the token decides,
    /// which is the whole fidelity rule.
    /// </remarks>
    [HttpGet("my-memberships")]
    public async Task<ActionResult<IEnumerable<MyMembershipOrganizationResponse>>> GetMyMembershipOrganizations(
        CancellationToken cancellationToken)
    {
        var appUserId = GetCurrentUserIdOrThrow();
        var organizations = await _organizationSecurityService.GetMembershipOrganizationsAsync(appUserId, cancellationToken);
        return Ok(organizations.Select(o => new MyMembershipOrganizationResponse(o.Id, o.Name)));
    }

    /// <summary>One row of the caller's own groups, shaped for a navigation link.</summary>
    public sealed record MyMembershipOrganizationResponse(Guid OrganizationId, string Name);

    /// <summary>
    /// What the caller may see in one group — the UI's mirror of the Phase-D gates (item 156).
    /// </summary>
    /// <remarks>
    /// The hub's Cases and Investigations tabs render from this rather than from bare
    /// membership, so a tab never appears whose every fetch the server would refuse — the
    /// server-guard-needs-a-UI-path rule. One round trip, extensible per area.
    /// </remarks>
    [HttpGet("{organizationId:guid}/my-permissions")]
    public async Task<ActionResult<MyOrgPermissionsResponse>> GetMyPermissions(
        Guid organizationId, CancellationToken cancellationToken)
    {
        var appUserId = GetCurrentUserIdOrThrow();
        var isSuperAdmin = User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin);

        var areas = new Dictionary<Ben.Data.Common.Enums.OrganizationPermissionArea, AreaActions>();
        foreach (var (area, table) in AreaProbeTables)
        {
            areas[area] = new AreaActions(
                Create: await MayAsync(table, Ben.Data.Common.Enums.OrganizationSecurityAction.Create),
                Read:   await MayAsync(table, Ben.Data.Common.Enums.OrganizationSecurityAction.Read),
                Update: await MayAsync(table, Ben.Data.Common.Enums.OrganizationSecurityAction.Update),
                Delete: await MayAsync(table, Ben.Data.Common.Enums.OrganizationSecurityAction.Delete));
        }

        // What the PLAN allows, alongside what the person may do — two different questions that a
        // screen usually has to ask together. Item 193: the private-engagement toggle rendered for
        // every group because the browser had no way to know the plan refused it, so a free-tier
        // group could tick it and collect a 400. A control should say what it will do before it is
        // used, not after.
        var capabilities = new Dictionary<Ben.Data.Common.Enums.TierCapability, bool>();
        using (var capScope = HttpContext.RequestServices.CreateScope())
        {
            var capFactory = capScope.ServiceProvider
                .GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<Ben.Data.Source.Context.BenDataContext>>();
            await using var capDb = await capFactory.CreateDbContextAsync(cancellationToken);

            foreach (var capability in Enum.GetValues<Ben.Data.Common.Enums.TierCapability>())
            {
                var (included, _) = await Ben.Data.Source.Services.TierAreaResolution.HasCapabilityAsync(
                    capDb, organizationId, capability, cancellationToken);
                capabilities[capability] = included;
            }
        }

        return Ok(new MyOrgPermissionsResponse(
            CanReadCases: areas[Ben.Data.Common.Enums.OrganizationPermissionArea.Cases].Read,
            CanReadInvestigations: areas[Ben.Data.Common.Enums.OrganizationPermissionArea.Investigations].Read,
            Areas: areas,
            Capabilities: capabilities));

        async Task<bool> MayAsync(
            Ben.Data.Common.Enums.OrganizationSecurityTable table,
            Ben.Data.Common.Enums.OrganizationSecurityAction action)
            => isSuperAdmin
            || await _organizationSecurityService.HasAccessAsync(
                   appUserId, organizationId, table, action, cancellationToken);
    }

    /// <summary>
    /// The caller's action-needed buckets across their groups (item 161): client requests
    /// awaiting an answer, and membership applications at the door.
    /// </summary>
    /// <remarks>
    /// <para>These are the two decisions that block OTHER people — a client waiting on an
    /// answer, an applicant waiting at the door — surfaced as banners under the site-wide
    /// announcement. Membership rows decide which groups are consulted: the token's person,
    /// so impersonation shows exactly the impersonated person's banners (the item-159
    /// fidelity rule) and a SuperAdmin sees their own groups' work, not every group's.</para>
    ///
    /// <para>Each bucket is counted only when the caller can OPEN the queue it names — the
    /// same read gates as the Requests and Members tabs — per the item-141 rule: never render
    /// a bucket the caller cannot open. Groups with nothing waiting are omitted.</para>
    /// </remarks>
    [HttpGet("action-needed")]
    public async Task<ActionResult<IEnumerable<OrgActionNeededResponse>>> GetActionNeeded(
        CancellationToken cancellationToken)
    {
        var appUserId    = GetCurrentUserIdOrThrow();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);

        using var scope = HttpContext.RequestServices.CreateScope();
        var dbFactory = scope.ServiceProvider
            .GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<Ben.Data.Source.Context.BenDataContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var orgs = await (
            from m in db.OrganizationUserMemberships.AsNoTracking()
            where m.AppUserId == appUserId && m.IsActive
            join o in db.Organizations.AsNoTracking() on m.OrganizationId equals o.Id
            select new { OrganizationId = o.Id, o.Name }).ToListAsync(cancellationToken);

        var results = new List<OrgActionNeededResponse>();
        foreach (var org in orgs)
        {
            var canOpenRequests = isSuperAdmin || await _organizationSecurityService.HasAccessAsync(
                appUserId, org.OrganizationId,
                Ben.Data.Common.Enums.OrganizationSecurityTable.Case,
                Ben.Data.Common.Enums.OrganizationSecurityAction.Read, cancellationToken);
            var canOpenApplications = isSuperAdmin || await _organizationSecurityService.HasAccessAsync(
                appUserId, org.OrganizationId,
                Ben.Data.Common.Enums.OrganizationSecurityTable.MembershipRequests,
                Ben.Data.Common.Enums.OrganizationSecurityAction.Read, cancellationToken);

            // "Waiting" mirrors the Requests queue's own definition: everything an org member
            // still owes an answer on, not just never-opened rows.
            var clientRequests = canOpenRequests
                ? await db.ClientRequestOrganizations.AsNoTracking().CountAsync(a =>
                        a.OrganizationId == org.OrganizationId &&
                        (a.Status == Ben.Data.Common.Enums.ClientOrgRequestStatus.Pending ||
                         a.Status == Ben.Data.Common.Enums.ClientOrgRequestStatus.Viewed ||
                         a.Status == Ben.Data.Common.Enums.ClientOrgRequestStatus.UnderReview),
                    cancellationToken)
                : 0;

            var applications = canOpenApplications
                ? await db.OrganizationMembershipRequests.AsNoTracking().CountAsync(r =>
                        r.OrganizationId == org.OrganizationId &&
                        r.Status == Ben.Data.Common.Enums.OrganizationMembershipRequestStatus.Pending,
                    cancellationToken)
                : 0;

            if (clientRequests > 0 || applications > 0)
                results.Add(new OrgActionNeededResponse(
                    org.OrganizationId, org.Name, clientRequests, applications));
        }

        return Ok(results);
    }

    /// <summary>One group's waiting work, for the caller's action-needed banners.</summary>
    public sealed record OrgActionNeededResponse(
        Guid OrganizationId, string OrganizationName,
        int PendingClientRequests, int PendingMembershipRequests);

    /// <summary>
    /// The permission areas this group's plan includes (item 156 Phase E) — what the role editor
    /// grays against, with the plan's name for the upgrade note.
    /// </summary>
    /// <remarks>
    /// <b>Members only.</b> It answers with the group's plan NAME, and it used to answer for any
    /// group to anybody signed in — so a stranger could read which plan any group was on, one id
    /// at a time. That is commercial information about somebody else's business, and no part of
    /// it belongs to a person with no relationship to the group (2026-09-04 sweep).
    /// </remarks>
    [HttpGet("{organizationId:guid}/included-areas")]
    public async Task<ActionResult<OrgIncludedAreasResponse>> GetIncludedAreas(
        Guid organizationId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserIdOrThrow();
        if (!User.IsInRole(RoleNames.SuperAdmin)
            && !await _organizationSecurityService.BelongsToAsync(userId, organizationId, cancellationToken))
            return Forbid();

        using var scope = HttpContext.RequestServices.CreateScope();
        var dbFactory = scope.ServiceProvider
            .GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<Ben.Data.Source.Context.BenDataContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var (areas, tierName) = await Ben.Data.Source.Services.TierAreaResolution
            .ResolveAsync(db, organizationId, cancellationToken);

        return Ok(new OrgIncludedAreasResponse([.. areas.OrderBy(a => (int)a)], tierName));
    }

    /// <summary>The plan's included role areas, and its name for the upgrade note.</summary>
    /// <summary>
    /// One representative table per area, for asking "may this person act in this area at all".
    /// </summary>
    /// <remarks>
    /// An area covers several tables (Cases covers the case, its timeline, its files…), and a
    /// role's permissions are stored per TABLE. A UI affordance is about the area, so each area
    /// is probed through the table its role permissions are actually written against — the one
    /// the role editor edits. Per-row rules (who may edit THIS case) stay where they are, on the
    /// server, per request.
    /// </remarks>
    private static readonly (Ben.Data.Common.Enums.OrganizationPermissionArea Area,
                             Ben.Data.Common.Enums.OrganizationSecurityTable Table)[] AreaProbeTables =
    [
        (Ben.Data.Common.Enums.OrganizationPermissionArea.OrganizationProfile, Ben.Data.Common.Enums.OrganizationSecurityTable.Organization),
        (Ben.Data.Common.Enums.OrganizationPermissionArea.Membership,          Ben.Data.Common.Enums.OrganizationSecurityTable.MembershipRequests),
        (Ben.Data.Common.Enums.OrganizationPermissionArea.Cases,               Ben.Data.Common.Enums.OrganizationSecurityTable.Case),
        (Ben.Data.Common.Enums.OrganizationPermissionArea.Investigations,      Ben.Data.Common.Enums.OrganizationSecurityTable.Investigation),
        (Ben.Data.Common.Enums.OrganizationPermissionArea.Equipment,           Ben.Data.Common.Enums.OrganizationSecurityTable.Equipment),
        (Ben.Data.Common.Enums.OrganizationPermissionArea.PublicPages,         Ben.Data.Common.Enums.OrganizationSecurityTable.OrganizationPage),
        (Ben.Data.Common.Enums.OrganizationPermissionArea.Files,               Ben.Data.Common.Enums.OrganizationSecurityTable.OrganizationFiles),
        (Ben.Data.Common.Enums.OrganizationPermissionArea.Clients,             Ben.Data.Common.Enums.OrganizationSecurityTable.ClientRequest),
        (Ben.Data.Common.Enums.OrganizationPermissionArea.Calendar,            Ben.Data.Common.Enums.OrganizationSecurityTable.OrgCalendar),
    ];

    public sealed record OrgIncludedAreasResponse(
        IReadOnlyList<Ben.Data.Common.Enums.OrganizationPermissionArea> Areas, string? TierName);

    /// <summary>
    /// What the caller may DO in one group, per permission area.
    /// </summary>
    /// <remarks>
    /// <para>Two booleans until 2026-08-26, and that was the whole of IH-03. A Case Manager holds
    /// create, update and delete on Cases — none of which this could express — so the browser had
    /// no way to offer a button that the grant had just unlocked. The server refused nothing; it
    /// simply never told anyone what they could do, and an owner configuring roles saw no
    /// difference whatsoever.</para>
    ///
    /// <para>Keyed by area rather than flattened into named booleans so a new area is a row,
    /// not a schema change — the same reasoning as the tier tables. <see cref="CanReadCases"/>
    /// and <see cref="CanReadInvestigations"/> remain so existing callers keep working.</para>
    /// </remarks>
    public sealed record MyOrgPermissionsResponse(
        bool CanReadCases,
        bool CanReadInvestigations,
        IReadOnlyDictionary<Ben.Data.Common.Enums.OrganizationPermissionArea, AreaActions> Areas,
        IReadOnlyDictionary<Ben.Data.Common.Enums.TierCapability, bool> Capabilities);

    /// <summary>What one person may do in one area. Absent action means refused.</summary>
    public sealed record AreaActions(bool Create, bool Read, bool Update, bool Delete);

    /// <summary>Creates a new organization with the authenticated user as its <see cref="Ben.Data.Common.Enums.OrganizationMemberRole.Owner"/>.</summary>
    /// <param name="request">Name and URL slug for the new organization.</param>
    /// <param name="cancellationToken">Propagates cancellation from the HTTP request.</param>
    /// <returns>A <c>201 Created</c> response with the new organization summary, <c>400</c> if the
    /// name/urlName is blank or the urlName is already taken, or <c>403</c> when self-registration
    /// is switched off site-wide.</returns>
    /// <remarks>
    /// <b>The setting this enforces did nothing for months.</b> Site Settings has offered
    /// "Allow groups to self-register — when off, only a SuperAdmin can create one" since the
    /// settings page shipped, and no code anywhere read it: an administrator could switch it off,
    /// watch the page report "Off", and still have every signed-in visitor founding groups. A
    /// policy control whose failure mode is believing you closed a door is worse than no control,
    /// which is why this now refuses here AND the website hides the button
    /// (<c>SiteFeaturesProvider.AllowOrganizationSelfRegistration</c>) — a server rule the UI
    /// never surfaces is the same bug wearing a different coat.
    ///
    /// <para>Unset reads as ON. Self-registration is how the product has always worked and the
    /// billing model depends on groups signing themselves up, so introducing the check must not
    /// switch it off for a site that never touched the setting.</para>
    /// </remarks>
    [HttpPost("register")]
    public async Task<ActionResult<OrganizationSummaryResponse>> RegisterOrganization(
        [FromBody] RegisterOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        var appUserId = GetCurrentUserIdOrThrow();

        // SuperAdmins are exempt: the switch exists to close the public door, and they create
        // groups through the admin page regardless.
        if (!User.IsInRole(RoleNames.SuperAdmin)
            && !await _siteSettings.GetBoolAsync(
                    SiteSettingKeys.AllowOrganizationSelfRegistration, whenUnset: true, cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                "New groups are not being accepted at the moment. Please contact us if you would like to start one.");
        }

        var organization = await _organizationSecurityService.RegisterOrganizationAsync(
            appUserId, request.Name, request.UrlName, request.Kind, cancellationToken);

        return CreatedAtAction(
            nameof(GetMyOrganizations),
            new { },
            new OrganizationSummaryResponse
            {
                OrganizationId = organization.Id,
                Name = organization.Name,
                UrlName = organization.UrlName,
                DateCreated = organization.DateCreated,
                CreatedByAppUserId = organization.CreatedByAppUserId
            });
    }

    /// <summary>Request body for <see cref="RegisterOrganization"/>.</summary>
    public sealed class RegisterOrganizationRequest
    {
        /// <summary>Human-readable display name of the new organization.</summary>
        public required string Name { get; set; }
        /// <summary>URL-safe slug (e.g. <c>my-org</c>); must be unique across all organizations.</summary>
        public required string UrlName { get; set; }
        /// <summary>What kind of group this is (2026-08-24). Defaults to an investigation
        /// group, which is what every caller written before this meant.</summary>
        public Ben.Data.Common.Enums.OrganizationKind Kind { get; set; }
            = Ben.Data.Common.Enums.OrganizationKind.InvestigationGroup;
    }

    /// <summary>Lightweight organization projection returned by membership endpoints.</summary>
    public sealed class OrganizationSummaryResponse
    {
        public Guid OrganizationId { get; set; }
        public required string Name { get; set; }
        public required string UrlName { get; set; }
        public DateTime DateCreated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
    }

    /// <summary>Lightweight user projection returned by the user-search endpoint.</summary>
    public sealed class UserSearchResultResponse
    {
        public Guid AppUserId { get; set; }
        public string? DisplayName { get; set; }
        public string? UserName { get; set; }
        public string? Email { get; set; }
    }
}