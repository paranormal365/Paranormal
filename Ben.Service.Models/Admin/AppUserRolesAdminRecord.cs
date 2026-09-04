using System.ComponentModel.DataAnnotations;

namespace Ben.Service.Models.Admin;

/// <summary>
/// The whole set of site roles a person is to hold (item 216). A set rather than a delta, because
/// the screen that sends it is a row of checkboxes and a Save button: what the administrator
/// sees ticked is exactly what the server ends up with.
/// </summary>
/// <param name="Roles">Role names as listed under Site Roles. Matched without regard to case.</param>
public sealed record AdminSetUserRolesRequest(
    [property: Required] IReadOnlyList<string> Roles);

/// <summary>The site roles a person holds after a change.</summary>
public sealed record AppUserRolesAdminRecord(Guid UserId, IReadOnlyList<string> Roles);
