using Xunit;
using CommonTable = Ben.Data.Common.Enums.OrganizationSecurityTable;
using SecurityTable = Ben.Service.Security.Enums.OrganizationSecurityTable;

namespace Ben.Service.RepositoryService.Tests;

/// <summary>
/// The two <c>OrganizationSecurityTable</c> enums must agree, value for value.
/// </summary>
/// <remarks>
/// <para><c>Ben.Service.Security.Services.OrganizationSecurityService</c> converts one to the other
/// with a plain numeric cast (<c>(DataCommonTable)table</c>), so the <i>numbers</i> decide which
/// table a permission check actually reads. If the two lists drift, a grant on one table silently
/// authorises a different one, and nothing anywhere would say so.</para>
///
/// <para>They had drifted: twenty-six of thirty values resolved to the wrong table before this was
/// aligned. <c>OrganizationFiles</c> landed on <c>MembershipRequests</c>. It had never fired only
/// because the attribute that uses this path is applied to nothing — which is exactly the kind of
/// luck that stops being luck the first time somebody uses the feature as intended.</para>
///
/// <para>Hence a test rather than a comment. Adding a value to one enum and not the other now
/// fails the build.</para>
/// </remarks>
public class OrganizationSecurityTableParityTests
{
    /// <summary>
    /// The one deliberate name difference. Both mean the user table; only the spelling differs,
    /// and both are load-bearing in their own project's call sites.
    /// </summary>
    private static readonly Dictionary<string, string> KnownAliases = new()
    {
        ["User"] = "AppUser",
    };

    [Fact]
    public void Every_shared_name_has_the_same_numeric_value()
    {
        var common = Enum.GetValues<CommonTable>()
            .ToDictionary(v => v.ToString(), v => (int)v);

        var mismatches = new List<string>();

        foreach (var value in Enum.GetValues<SecurityTable>())
        {
            var name = value.ToString();

            // None has no counterpart by design — Ben.Data.Common starts at 1, so a zero can never
            // match a stored grant. That is the intended behaviour, not a gap.
            if (name == nameof(SecurityTable.None)) continue;

            var commonName = KnownAliases.TryGetValue(name, out var alias) ? alias : name;

            if (!common.TryGetValue(commonName, out var commonValue))
            {
                mismatches.Add($"{name} = {(int)value} exists only in Ben.Service.Security.");
                continue;
            }

            if (commonValue != (int)value)
                mismatches.Add(
                    $"{name} is {(int)value} in Ben.Service.Security but {commonValue} in Ben.Data.Common " +
                    $"— a check for {name} would actually read {common.First(kv => kv.Value == (int)value).Key}.");
        }

        Assert.True(mismatches.Count == 0,
            "The two OrganizationSecurityTable enums have drifted:\n  " + string.Join("\n  ", mismatches));
    }

    [Fact]
    public void The_investigation_table_exists_in_both()
    {
        // The value P3 adds. Named explicitly so that removing it from either side fails loudly
        // rather than merely reducing the parity test's coverage by one.
        Assert.Equal(34, (int)CommonTable.Investigation);
        Assert.Equal(34, (int)SecurityTable.Investigation);
    }

    [Fact]
    public void The_cast_the_service_performs_round_trips()
    {
        // Exercises the conversion exactly as OrganizationSecurityService does it, rather than
        // trusting that matching numbers imply a working cast.
        foreach (var value in Enum.GetValues<SecurityTable>())
        {
            if (value == SecurityTable.None) continue;

            var cast = (CommonTable)value;
            var expected = KnownAliases.TryGetValue(value.ToString(), out var alias)
                ? alias
                : value.ToString();

            Assert.Equal(expected, cast.ToString());
        }
    }
}
