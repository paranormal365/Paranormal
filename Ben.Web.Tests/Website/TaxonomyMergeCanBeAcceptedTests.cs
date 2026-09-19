using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// An offer the server makes has a way to accept it.
/// </summary>
/// <remarks>
/// <para><b>What this guards.</b> Renaming a taxonomy row onto an existing name answers 409 with a
/// <c>TaxonomyMergeOffer</c> — "that name already exists; merging moves everything across and
/// cannot be undone". The record's own remarks say it is "returned rather than performed" precisely
/// so a person can confirm it. The 2026-09-17 audit found that nothing could:</para>
///
/// <list type="bullet">
/// <item>For experience types the rename WAS called, and the adapter used a plain <c>PutAsync</c>
/// that discards a 409 — so the dialog said "Save failed" and the offer was written, sent and never
/// read. The endpoint that accepts it had no caller anywhere.</item>
/// <item>For equipment brands and models, all four routes — rename and merge, twice — had no
/// caller at all. Ben's question was "what happens when I try to change Samsung to Sansung?", the
/// server half shipped the day it was asked, and the button never did. A typo was permanently
/// unfixable: delete is refused while anything references the row, and nothing could rename.</item>
/// </list>
///
/// <para><b>Why a source scan.</b> The endpoints work when called. What was broken was that
/// nothing called them, and no controller test can fail for having no caller — the merge
/// endpoints even have unit tests, which passed throughout. Only the chain is checkable:
/// route → adapter → page → a control that accepts.</para>
///
/// <para>The conflict-aware helper matters as much as the call. <c>PutAsync</c> returns null on a
/// 409 and throws the body away; <c>SendExpectingConflictAsync</c> is the one that keeps the shape.
/// Calling the right route through the wrong helper is the exact defect this file describes, so it
/// is asserted rather than assumed.</para>
/// </remarks>
public sealed class TaxonomyMergeCanBeAcceptedTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(RepoRoot().FullName, Path.Combine(parts)));

    private static string EquipmentAdapter()
        => Read("Ben.Web.Services", "WebApi", "BenAdminClientAdapter.Equipment.cs");

    private static string PlatformAdapter()
        => Read("Ben.Web.Services", "WebApi", "BenAdminClientAdapter.Platform.cs");

    [Theory]
    [InlineData("/brands/{id}")]
    [InlineData("/brands/{id}/merge-into/{targetId}")]
    [InlineData("/models/{id}")]
    [InlineData("/models/{id}/merge-into/{targetId}")]
    public void Every_equipment_taxonomy_rename_and_merge_route_has_a_client(string route)
    {
        // The adapter composes these against AdminTaxonomyBase, so the tail is what to match.
        var tail = route.Replace("{id}", "{id}").Replace("{targetId}", "{targetId}");

        Assert.Contains(tail, EquipmentAdapter());
    }

    [Fact]
    public void The_experience_type_merge_route_has_a_client()
        => Assert.Contains("/types/{id}/merge-into/{targetId}", PlatformAdapter());

    /// <summary>
    /// A rename must go through the helper that KEEPS a 409's body. PutAsync returns null and
    /// throws the offer away, which is how the experience-type offer came to be unreadable while
    /// the route was being called perfectly well.
    /// </summary>
    [Theory]
    [InlineData("RenameEquipmentBrandAsync")]
    [InlineData("RenameEquipmentModelAsync")]
    public void An_equipment_rename_keeps_the_conflict_body(string method)
    {
        var adapter = EquipmentAdapter();
        var at = adapter.IndexOf(method, StringComparison.Ordinal);

        Assert.True(at >= 0, $"{method} is not in the equipment adapter");

        // The helper call sits within the method body that follows the signature.
        var body = adapter[at..Math.Min(adapter.Length, at + 600)];
        Assert.Contains("SendExpectingConflictAsync", body);
    }

    [Fact]
    public void The_experience_type_rename_keeps_the_conflict_body()
    {
        var adapter = PlatformAdapter();
        var at = adapter.IndexOf("UpdateExperienceTypeAsync", StringComparison.Ordinal);

        Assert.True(at >= 0);
        Assert.Contains("SendExpectingConflictAsync", adapter[at..Math.Min(adapter.Length, at + 600)]);
    }

    /// <summary>And a page that offers the merge, not just a page that can receive it.</summary>
    [Theory]
    [InlineData("SuperAdmin", "AdminEquipmentTaxonomy.razor", "MergeEquipmentBrandAsync")]
    [InlineData("SuperAdmin", "AdminEquipmentTaxonomy.razor", "MergeEquipmentModelAsync")]
    [InlineData("SuperAdmin", "AdminEquipmentTaxonomy.razor", "RenameEquipmentBrandAsync")]
    [InlineData("SuperAdmin", "AdminEquipmentTaxonomy.razor", "RenameEquipmentModelAsync")]
    [InlineData("SuperAdmin", "AdminExperienceTaxonomy.razor", "MergeExperienceTypeAsync")]
    public void A_page_calls_the_client_method(string folder, string file, string method)
        => Assert.Contains(method, Read("Ben.Web.Website.Library", folder, file));

    /// <summary>
    /// The offer is rendered with a control that takes it. A page that stored the offer and drew
    /// nothing would satisfy every assertion above.
    /// </summary>
    [Theory]
    [InlineData("AdminEquipmentTaxonomy.razor", "equipment-merge-offer", "equipment-merge-accept")]
    [InlineData("AdminExperienceTaxonomy.razor", "taxonomy-merge-offer", "taxonomy-merge-accept")]
    public void The_offer_is_shown_with_a_button_that_accepts_it(
        string file, string offerTestId, string acceptTestId)
    {
        var page = Read("Ben.Web.Website.Library", "SuperAdmin", file);

        Assert.Contains(offerTestId, page);
        Assert.Contains(acceptTestId, page);
        // And the server's own sentence, rather than wording invented here: it is the only warning
        // anybody gets that two rows are about to become one, and it cannot be undone.
        Assert.Contains("offer.Message", page);
    }
}
