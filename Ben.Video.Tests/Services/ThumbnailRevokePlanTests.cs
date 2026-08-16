using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Item #62 phase 170 — the set-membership rule behind the thumbnail-strip revoke.
///
/// <para>Each test below names the concrete live mechanism it pins; see
/// <see cref="ThumbnailRevokePlan"/> and README-phase-170.md for why a wholesale revoke of the
/// replaced strip was wrong.</para>
/// </summary>
public sealed class ThumbnailRevokePlanTests
{
    [Fact]
    public void UrlsNotReferencedAnywhere_AreOrphaned()
    {
        var orphans = ThumbnailRevokePlan.Orphaned(
            previous: ["blob:a", "blob:b"],
            stillReferenced: ["blob:new1", "blob:new2"]);

        Assert.Equal(["blob:a", "blob:b"], orphans);
    }

    [Fact]
    public void UrlCarriedIntoTheReplacementStrip_IsNotRevoked()
    {
        // The regeneration can legitimately return an identical URL for an unchanged frame.
        // Revoking it would kill a blob the NEW strip is about to render.
        var orphans = ThumbnailRevokePlan.Orphaned(
            previous: ["blob:keep", "blob:drop"],
            stillReferenced: ["blob:keep", "blob:fresh"]);

        Assert.Equal(["blob:drop"], orphans);
    }

    [Fact]
    public void UrlStillHeldByAnotherClip_IsNotRevoked()
    {
        // Mechanism 2, the one with a visible symptom: VideoEditor.AddClipToTimeline and
        // ClipStore.DuplicateClip copy the strip LIST but share the URL STRINGS, so two placements
        // of one source point at the same blobs. Refilling one used to blank the other.
        var orphans = ThumbnailRevokePlan.Orphaned(
            previous: ["blob:shared", "blob:mine"],
            stillReferenced: ["blob:shared"]); // the duplicate placement still renders it

        Assert.Equal(["blob:mine"], orphans);
    }

    [Fact]
    public void UrlStillHeldByAnImportRow_IsNotRevoked()
    {
        // The import pipeline assigns status.PreviewUrl = thumbs[0], and that row keeps rendering
        // <img src="@item.PreviewUrl"> after the import finishes — so thumbs[0] has a second,
        // non-clip owner that a strip-only view of the world misses entirely.
        var orphans = ThumbnailRevokePlan.Orphaned(
            previous: ["blob:first", "blob:second"],
            stillReferenced: ["blob:first"]); // held by the import row, not by any clip

        Assert.Equal(["blob:second"], orphans);
    }

    [Fact]
    public void RepeatedUrlInOneStrip_IsReturnedOnce()
    {
        // A clip shorter than the thumbnail interval can yield a strip with the same URL twice.
        // Returning it twice would revoke the same handle twice and trip phase 144's
        // double-revoke diagnostic with a false positive.
        var orphans = ThumbnailRevokePlan.Orphaned(
            previous: ["blob:dup", "blob:dup", "blob:other"],
            stillReferenced: []);

        Assert.Equal(["blob:dup", "blob:other"], orphans);
    }

    [Fact]
    public void EmptyAndNullEntries_AreSkipped()
    {
        var orphans = ThumbnailRevokePlan.Orphaned(
            previous: ["", "blob:real", null!],
            stillReferenced: []);

        Assert.Equal(["blob:real"], orphans);
    }

    [Fact]
    public void ComparisonIsOrdinal_NotCaseInsensitive()
    {
        // Blob URLs are case-sensitive opaque handles; treating "blob:AB" as a match for
        // "blob:ab" would silently skip a genuine orphan and leak it.
        var orphans = ThumbnailRevokePlan.Orphaned(
            previous: ["blob:AB"],
            stillReferenced: ["blob:ab"]);

        Assert.Equal(["blob:AB"], orphans);
    }

    [Fact]
    public void EmptyPreviousStrip_YieldsNothing()
    {
        Assert.Empty(ThumbnailRevokePlan.Orphaned([], ["blob:a"]));
    }
}
