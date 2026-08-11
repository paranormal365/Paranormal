using System.Reflection;
using Ben.Data.Common.Constants;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Controllers.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Regression coverage for the Phase-A fix to <see cref="EntityReadControllerBase{TEntity,TRecord}"/>:
/// its <c>GetAll</c>/<c>GetById</c> return every row unfiltered, so every subclass MUST either
/// (a) declare its own class-level <c>[Authorize(Policy = RoleNames.SuperAdmin)]</c>, or
/// (b) override both actions as <c>[NonAction]</c> and replace them with real permission-aware
/// endpoints (the pattern <see cref="OrganizationController"/> already uses).
/// <para>
/// These controllers are thin pass-throughs with no method-body logic of their own — the fix is
/// entirely attribute-based — so a direct-instantiation unit test (this codebase's normal style,
/// which bypasses the ASP.NET authorization pipeline) can't exercise it. This reflects over the
/// actual attribute instead, and is written to auto-cover any *future* subclass too: forgetting
/// to gate a new one fails this test rather than shipping an open endpoint silently.
/// </para>
/// </summary>
public class EntityReadControllerBaseAuthorizationTests
{
    /// <summary>
    /// The 12 "*Type" lookup subclasses (<c>UserAddressTypeController</c>,
    /// <c>OrganizationPhoneTypeController</c>, etc.) are a genuine third category this reflection
    /// check needs to know about explicitly: their entities carry nothing but an <c>Id</c> — pure
    /// enum-as-table reference data (e.g. populating a "Home/Work/Mobile" dropdown) with no
    /// personal or sensitive content — so leaving them at the base class's permissive
    /// "any authenticated user" default is correct, not an oversight, and locking them to
    /// SuperAdmin would break ordinary users' address/phone/email/link/note type pickers
    /// app-wide. Named explicitly (rather than matched by a "*Type" suffix pattern) so a future
    /// entity that happens to be named similarly but actually carries real data doesn't silently
    /// slip through this allowlist.
    /// </summary>
    private static readonly HashSet<string> KnownSafeUnguardedLookupControllers =
    [
        "OrganizationAddressTypeController",
        "OrganizationEmailTypeController",
        "OrganizationLinkTypeController",
        "OrganizationNoteTypeController",
        "OrganizationPhoneTypeController",
        "UploadFileTypeController",
        "UserAddressTypeController",
        "UserEmailTypeController",
        "UserLinkTypeController",
        "UserMessageTypeController",
        "UserNoteTypeController",
        "UserPhoneTypeController",
    ];

    private static IEnumerable<Type> AllEntityReadSubclasses() =>
        typeof(EntityReadControllerBase<,>).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && t.BaseType is { IsGenericType: true } bt
                        && bt.GetGenericTypeDefinition() == typeof(EntityReadControllerBase<,>));

    private static bool OverridesGetAllAndGetByIdAsNonAction(Type controllerType)
    {
        bool IsNonActionOverride(string methodName) =>
            controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == methodName)
                .Any(m => m.GetCustomAttribute<NonActionAttribute>() is not null);

        return IsNonActionOverride(nameof(EntityReadControllerBase<object, object>.GetAll))
            && IsNonActionOverride(nameof(EntityReadControllerBase<object, object>.GetById));
    }

    private static bool HasSuperAdminPolicy(Type controllerType) =>
        controllerType.GetCustomAttributes<AuthorizeAttribute>(inherit: false)
            .Any(a => a.Policy == RoleNames.SuperAdmin);

    [Fact]
    public void AllEntityReadSubclasses_AreEitherSuperAdminGated_OrOverrideWithRealChecks()
    {
        var subclasses = AllEntityReadSubclasses().ToList();

        // Sanity check the reflection query itself found the controller family this test targets,
        // so a refactor that silently breaks the query (e.g. renaming the base class) fails loudly
        // here rather than the test just vacuously passing with zero types checked.
        Assert.True(subclasses.Count >= 14, $"Expected at least 14 EntityReadControllerBase subclasses, found {subclasses.Count}.");

        // Guard the allowlist itself against staleness: every entry must still name a real,
        // still-unguarded subclass, so a rename/removal/gating of one of these lookup
        // controllers doesn't leave a dead entry silently masking something else.
        var stillUnguardedNames = subclasses
            .Where(t => !HasSuperAdminPolicy(t) && !OverridesGetAllAndGetByIdAsNonAction(t))
            .Select(t => t.Name)
            .ToHashSet();
        var staleAllowlistEntries = KnownSafeUnguardedLookupControllers
            .Where(name => !stillUnguardedNames.Contains(name))
            .ToList();
        Assert.True(staleAllowlistEntries.Count == 0,
            "KnownSafeUnguardedLookupControllers has stale entries that no longer match an " +
            $"unguarded subclass (renamed, removed, or now gated?): {string.Join(", ", staleAllowlistEntries)}");

        var unguarded = stillUnguardedNames
            .Where(name => !KnownSafeUnguardedLookupControllers.Contains(name))
            .ToList();

        Assert.True(unguarded.Count == 0,
            "The following EntityReadControllerBase subclasses have no SuperAdmin gate, don't " +
            "override GetAll/GetById, and aren't in the reviewed lookup-type allowlist — their " +
            "full, unfiltered row list is reachable by any authenticated user. If this is a new " +
            "non-sensitive lookup entity, add it to KnownSafeUnguardedLookupControllers; " +
            $"otherwise it needs [Authorize(Policy = RoleNames.SuperAdmin)]: {string.Join(", ", unguarded)}");
    }

    [Theory]
    [InlineData(typeof(UserAddressController))]
    [InlineData(typeof(UserEmailController))]
    [InlineData(typeof(UserPhoneController))]
    [InlineData(typeof(UserMessageController))]
    [InlineData(typeof(UserMessageToController))]
    [InlineData(typeof(UserNoteController))]
    [InlineData(typeof(UserLinkController))]
    [InlineData(typeof(OrganizationEmailController))]
    [InlineData(typeof(OrganizationPhoneController))]
    [InlineData(typeof(OrganizationNoteController))]
    [InlineData(typeof(OrganizationAddressController))]
    [InlineData(typeof(OrganizationLinkController))]
    [InlineData(typeof(OrganizationPageController))]
    [InlineData(typeof(AppUserController))]
    public void KnownSensitiveSubclass_RequiresSuperAdminPolicy(Type controllerType)
    {
        Assert.True(HasSuperAdminPolicy(controllerType),
            $"{controllerType.Name} must carry [Authorize(Policy = RoleNames.SuperAdmin)] — it exposes " +
            "every row of its table via GetAll/GetById with no ownership filtering.");
    }

    [Fact]
    public void OrganizationController_UsesOverridePattern_NotSuperAdminGate()
    {
        // Documents/guards the *other* acceptable pattern: OrganizationController deliberately does
        // NOT carry the SuperAdmin policy (its GetAllWithPermissions/GetByIdWithPermissions must
        // stay reachable by ordinary org members) — it earns that by overriding the two unsafe
        // base actions as [NonAction] instead. If either half of that regresses, this fails.
        Assert.False(HasSuperAdminPolicy(typeof(OrganizationController)),
            "OrganizationController should rely on its NonAction overrides, not a class-level SuperAdmin gate " +
            "(adding one would also lock down GetAllWithPermissions/Create/Update/Delete for ordinary org admins).");

        Assert.True(OverridesGetAllAndGetByIdAsNonAction(typeof(OrganizationController)),
            "OrganizationController must override GetAll/GetById as [NonAction] — without the SuperAdmin gate, " +
            "leaving the base implementations live would expose every organization's row unfiltered.");
    }
}
