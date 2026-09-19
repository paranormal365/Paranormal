using System.Text.RegularExpressions;
using Ben.Data.Common.Enums;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Every cap, capability and permission area the site declares has a human label on the price-band
/// screen.
/// </summary>
/// <remarks>
/// <para>Each of the three switches on <c>AdminSubscriptionTiers.razor</c> ends in
/// <c>_ => value.ToString()</c>, so a value nobody labelled does not break the page — it renders
/// as <c>EventFilesMegabytes</c>, which is a label the way <c>avatar.default.upload-file-id</c> was
/// a label. The fallback is right (a half-labelled screen beats a crash) and it is exactly why
/// nothing else notices.</para>
///
/// <para>A source scan rather than a unit test because the labels live in a Razor switch with no
/// seam: the page is a component, the method is private, and the only honest way to ask whether a
/// value is spelled out is to read the file. Item 235 appended seven values across three enums,
/// which is when this was written.</para>
/// </remarks>
public sealed class TierLabelCoverageTests
{
    private static string PageSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName,
            "Ben.Web.Website.Library", "SuperAdmin", "AdminSubscriptionTiers.razor");
        Assert.True(File.Exists(path), $"The price-band page was not where this test looked: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// The arm for one enum value: `Something.Value` followed by `=>` and a quoted label.
    /// </summary>
    private static bool IsLabelled(string source, string enumName, string valueName)
        => Regex.IsMatch(
            source,
            $@"{Regex.Escape(enumName)}\.{Regex.Escape(valueName)}\s*=>\s*""[^""]+""");

    [Fact]
    public void Every_subscription_limit_has_a_label()
    {
        var source = PageSource();
        var unlabelled = Enum.GetNames<SubscriptionLimit>()
            .Where(name => !IsLabelled(source, nameof(SubscriptionLimit), name))
            .ToList();

        Assert.True(unlabelled.Count == 0, Message("cap", "LimitLabel", unlabelled));
    }

    [Fact]
    public void Every_tier_capability_has_a_label()
    {
        var source = PageSource();
        var unlabelled = Enum.GetNames<TierCapability>()
            .Where(name => !IsLabelled(source, nameof(TierCapability), name))
            .ToList();

        Assert.True(unlabelled.Count == 0, Message("capability", "CapabilityLabel", unlabelled));
    }

    [Fact]
    public void Every_permission_area_has_a_label()
    {
        var source = PageSource();
        var unlabelled = Enum.GetNames<OrganizationPermissionArea>()
            .Where(name => !IsLabelled(source, nameof(OrganizationPermissionArea), name))
            .ToList();

        Assert.True(unlabelled.Count == 0, Message("area", "AreaLabel", unlabelled));
    }

    private static string Message(string noun, string method, IReadOnlyList<string> unlabelled)
        => $"""
            {unlabelled.Count} {noun}(s) have no label on the price-band screen, so a SuperAdmin
            sets them by their enum name.

            Add an arm to {method} in Ben.Web.Website.Library/SuperAdmin/AdminSubscriptionTiers.razor.

              {string.Join("\n  ", unlabelled)}
            """;
}
