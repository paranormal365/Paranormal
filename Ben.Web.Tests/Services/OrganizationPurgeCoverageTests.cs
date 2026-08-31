using System.Reflection;
using Ben.Data.Source.Entities;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Every table that hangs off a group must be named in the purge, or the purge is a 500.
/// </summary>
/// <remarks>
/// <para><b>Why a reflection scan rather than a unit test of behaviour.</b> The purge deletes
/// bottom-up through roughly forty tables. Behaviour tests prove the ones somebody thought to
/// seed; what actually breaks this feature is a table added NEXT YEAR whose author never heard of
/// it. Every foreign key onto Organizations is NoAction here, so a missed table is a constraint
/// violation — the transaction rolls back and a SuperAdmin is told the group cannot be deleted,
/// with no clue why.</para>
///
/// <para>This test finds every entity carrying an <c>OrganizationId</c> and asserts the purge's
/// source mentions it. A crude check, deliberately: it cannot tell whether the delete is CORRECT,
/// only that the table was not forgotten — and forgetting is the failure that actually happens.
/// The same shape as <c>ReachableComponentTests</c>, and for the same reason.</para>
/// </remarks>
public sealed class OrganizationPurgeCoverageTests
{
    /// <summary>Walks up from the test binaries to the repository root.</summary>
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("Could not find the repository root.");
    }

    private static string PurgeSource()
        => File.ReadAllText(Path.Combine(
            RepoRoot().FullName, "Ben.Data.WebApi", "Services", "Admin", "OrganizationPurge.cs"));

    /// <summary>
    /// Entities the purge deliberately does NOT delete, each with the reason it survives.
    /// </summary>
    /// <remarks>
    /// Listed rather than silently skipped: a table that is exempt should say so out loud, so the
    /// next person can disagree with the reason instead of guessing whether it was an oversight.
    /// </remarks>
    private static readonly Dictionary<string, string> DeliberatelyKept = new()
    {
        // Nothing here yet. Places and AppUsers carry no OrganizationId, so they never reach this
        // scan — they survive by not being the group's property at all, which is the point.
    };

    /// <summary>Whether the purge names this entity, allowing for the -y/-ies plural.</summary>
    private static bool Mentions(string source, string entityName)
    {
        if (source.Contains(entityName, StringComparison.Ordinal)) return true;

        return entityName.EndsWith('y')
            && source.Contains($"{entityName[..^1]}ies", StringComparison.Ordinal);
    }

    [Fact]
    public void Every_table_that_belongs_to_a_group_is_named_in_the_purge()
    {
        var source = PurgeSource();

        var entities = typeof(Organization).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                     && t.Namespace == typeof(Organization).Namespace
                     && t.GetProperty("OrganizationId",
                            BindingFlags.Public | BindingFlags.Instance) is not null)
            .Select(t => t.Name)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        Assert.NotEmpty(entities);   // a scan that finds nothing proves nothing

        var missing = entities
            .Where(name => !DeliberatelyKept.ContainsKey(name))
            // The DbSet name is the plural the context declares, and English plurals are not a
            // substring of the singular: BillingLedgerEntry becomes BillingLedgerEntries, which
            // does not contain "BillingLedgerEntry". So the -y/-ies form is checked too. Irregular
            // plurals (CaseRelatedPerson -> CaseRelatedPeople) still contain the stem the loop
            // searches for, which is why the singular is tried first.
            .Where(name => !Mentions(source, name))
            .ToList();

        Assert.True(missing.Count == 0,
            "These belong to a group and the purge never mentions them, so deleting a group that "
          + "has any will fail on a foreign key and roll back:\n  "
          + string.Join("\n  ", missing));
    }

    /// <summary>
    /// The three things that must survive a purge, asserted by their absence from it.
    /// </summary>
    /// <remarks>
    /// People, places and lookups are not the group's property. This is a cheap guard against
    /// somebody "completing" the cascade one day by adding the obvious-looking line.
    /// </remarks>
    [Theory]
    [InlineData("db.AppUsers", "a person is not a group's property and may belong to other groups")]
    [InlineData("db.Places", "a place is shared, and its public archive is built from many people's visits")]
    [InlineData("db.UploadFileTypes", "site-wide lookup data that no group owns")]
    [InlineData("db.EquipmentModels", "the equipment catalogue is site-wide reference data")]
    public void The_purge_never_deletes_what_a_group_does_not_own(string forbidden, string why)
    {
        var source = PurgeSource();

        Assert.False(source.Contains($"{forbidden}.Where", StringComparison.Ordinal)
                  && source.Contains("ExecuteDeleteAsync", StringComparison.Ordinal)
                  && source.Contains($"{forbidden}.Where", StringComparison.Ordinal),
            $"The purge appears to delete from {forbidden}, but {why}.");
    }

    /// <summary>
    /// A group's own field sessions go; a person's private ones do not.
    /// </summary>
    /// <remarks>
    /// Somebody who scouted a building on their own account keeps that recording when a group they
    /// belonged to is deleted — it was never the group's to lose. Pinned in source because the
    /// natural simplification (delete every session by every member) reads as more thorough and is
    /// actually theft.
    /// </remarks>
    [Fact]
    public void Personal_field_sessions_are_not_swept_up_with_the_group()
    {
        var source = PurgeSource();

        Assert.Contains("InvestigationId", source);
        Assert.DoesNotContain("db.FieldSessionUploads.Where(x => x.SubmittedByAppUserId", source);
    }
}
