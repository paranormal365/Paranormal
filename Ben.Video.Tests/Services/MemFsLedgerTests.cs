using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Item #59-#65 flakiness investigation, phase 141 — <see cref="MemFsLedger"/> is the pure C#
/// "heap readout" the item #38 design doc specified but never built. See its own doc comment for
/// why it's a lower-bound approximation, not an exact live figure.
/// </summary>
public sealed class MemFsLedgerTests
{
    [Fact]
    public void Track_NewEntry_AddsToTotalAndCount()
    {
        var ledger = new MemFsLedger();

        ledger.Track("clip.mp4", 1000, "write");

        Assert.Equal(1000, ledger.TotalBytes);
        Assert.Equal(1, ledger.Count);
        Assert.Equal("write", ledger.Entries.Single().Category);
    }

    [Fact]
    public void Track_SameNameTwice_UpdatesInPlaceRatherThanDuplicating()
    {
        var ledger = new MemFsLedger();

        ledger.Track("temp.png", 500, "write");
        ledger.Track("temp.png", 900, "write"); // re-write with different content

        Assert.Equal(1, ledger.Count);
        Assert.Equal(900, ledger.TotalBytes);
    }

    [Fact]
    public void Track_ZeroSizeEntry_StillCountedButContributesNothingToTotal()
    {
        // FfmpegService.WriteFileAsync's case — an opaque JS File reference whose .size was
        // never read. The entry must still exist (Count reflects "how many things are resident")
        // even though it can't contribute a real number to TotalBytes.
        var ledger = new MemFsLedger();

        ledger.Track("from-file-picker.mp4", 0, "source-fallback");

        Assert.Equal(1, ledger.Count);
        Assert.Equal(0, ledger.TotalBytes);
    }

    [Fact]
    public void Untrack_RemovesEntry()
    {
        var ledger = new MemFsLedger();
        ledger.Track("a.mp4", 100);
        ledger.Track("b.mp4", 200);

        ledger.Untrack("a.mp4");

        Assert.Equal(1, ledger.Count);
        Assert.Equal(200, ledger.TotalBytes);
    }

    [Fact]
    public void Untrack_UnknownName_NoOp()
    {
        var ledger = new MemFsLedger();
        ledger.Track("a.mp4", 100);

        ledger.Untrack("never-tracked.mp4");

        Assert.Equal(1, ledger.Count);
        Assert.Equal(100, ledger.TotalBytes);
    }

    [Fact]
    public void Rename_PreservesSizeAndCategoryUnderNewName()
    {
        var ledger = new MemFsLedger();
        ledger.Track("preview_vid_0.mp4", 5000, "preview");

        ledger.Rename("preview_vid_0.mp4", "final_out.mp4");

        Assert.Equal(1, ledger.Count);
        var entry = ledger.Entries.Single();
        Assert.Equal("final_out.mp4", entry.Name);
        Assert.Equal(5000, entry.Bytes);
        Assert.Equal("preview", entry.Category);
        Assert.Equal(5000, ledger.TotalBytes);
    }

    [Fact]
    public void Rename_UnknownFrom_NoOp()
    {
        var ledger = new MemFsLedger();

        ledger.Rename("never-existed.mp4", "whatever.mp4");

        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public void Rename_ToAnAlreadyTrackedName_OverwritesTheDestination()
    {
        var ledger = new MemFsLedger();
        ledger.Track("a.mp4", 100);
        ledger.Track("b.mp4", 999); // will be clobbered by the rename below

        ledger.Rename("a.mp4", "b.mp4");

        Assert.Equal(1, ledger.Count);
        Assert.Equal(100, ledger.Entries.Single().Bytes);
    }

    [Fact]
    public void Clear_RemovesEverything()
    {
        var ledger = new MemFsLedger();
        ledger.Track("a.mp4", 100);
        ledger.Track("b.mp4", 200);

        ledger.Clear();

        Assert.Equal(0, ledger.Count);
        Assert.Equal(0, ledger.TotalBytes);
    }

    [Fact]
    public void TotalBytes_SumsAcrossMultipleCategories()
    {
        var ledger = new MemFsLedger();
        ledger.Track("src.mp4", 1_000_000, "source-fallback");
        ledger.Track("preview.mp4", 200_000, "preview");
        ledger.Track("frame_0001.png", 50_000, "temp");

        Assert.Equal(1_250_000, ledger.TotalBytes);
        Assert.Equal(3, ledger.Count);
    }
}
