using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

public sealed class OverwriteEditCalculatorTests
{
    [Fact]
    public void Resolve_NoOverlap_Before_ReturnsUnchanged()
    {
        var existing = new TrimmedSegment(Start: 0, Duration: 5, SourceStart: 0, SourceEnd: 5);

        var result = OverwriteEditCalculator.Resolve(insertStart: 5, insertDuration: 3, existing);

        Assert.Single(result);
        Assert.Equal(existing, result[0]);
    }

    [Fact]
    public void Resolve_NoOverlap_After_ReturnsUnchanged()
    {
        var existing = new TrimmedSegment(Start: 10, Duration: 5, SourceStart: 0, SourceEnd: 5);

        var result = OverwriteEditCalculator.Resolve(insertStart: 0, insertDuration: 5, existing);

        Assert.Single(result);
        Assert.Equal(existing, result[0]);
    }

    [Fact]
    public void Resolve_TouchingExactly_NotTreatedAsOverlap()
    {
        // Existing ends exactly where the new clip starts — touching, not overlapping.
        var existing = new TrimmedSegment(Start: 0, Duration: 5, SourceStart: 0, SourceEnd: 5);

        var result = OverwriteEditCalculator.Resolve(insertStart: 5, insertDuration: 3, existing);

        Assert.Single(result);
        Assert.Equal(existing, result[0]);
    }

    [Fact]
    public void Resolve_FullyCovered_ReturnsEmpty()
    {
        // Existing clip 2..7 is entirely inside the new clip's 0..10 span.
        var existing = new TrimmedSegment(Start: 2, Duration: 5, SourceStart: 0, SourceEnd: 5);

        var result = OverwriteEditCalculator.Resolve(insertStart: 0, insertDuration: 10, existing);

        Assert.Empty(result);
    }

    [Fact]
    public void Resolve_ExactlyCovered_ReturnsEmpty()
    {
        var existing = new TrimmedSegment(Start: 0, Duration: 5, SourceStart: 0, SourceEnd: 5);

        var result = OverwriteEditCalculator.Resolve(insertStart: 0, insertDuration: 5, existing);

        Assert.Empty(result);
    }

    [Fact]
    public void Resolve_OverlapAtEnd_TrimsExistingEndBack()
    {
        // Existing 0..10 (source 0..10); new clip lands at 7..12 → existing should become 0..7.
        var existing = new TrimmedSegment(Start: 0, Duration: 10, SourceStart: 0, SourceEnd: 10);

        var result = OverwriteEditCalculator.Resolve(insertStart: 7, insertDuration: 5, existing);

        Assert.Single(result);
        Assert.Equal(0, result[0].Start, precision: 9);
        Assert.Equal(7, result[0].Duration, precision: 9);
        Assert.Equal(0, result[0].SourceStart, precision: 9);
        Assert.Equal(7, result[0].SourceEnd, precision: 9);
    }

    [Fact]
    public void Resolve_OverlapAtStart_TrimsExistingStartForward()
    {
        // Existing 5..15 (source 0..10); new clip lands at 0..8 → existing should become 8..15,
        // with its source window shifted forward by the trimmed amount (3s).
        var existing = new TrimmedSegment(Start: 5, Duration: 10, SourceStart: 0, SourceEnd: 10);

        var result = OverwriteEditCalculator.Resolve(insertStart: 0, insertDuration: 8, existing);

        Assert.Single(result);
        Assert.Equal(8, result[0].Start, precision: 9);
        Assert.Equal(7, result[0].Duration, precision: 9);
        Assert.Equal(3, result[0].SourceStart, precision: 9);
        Assert.Equal(10, result[0].SourceEnd, precision: 9);
    }

    [Fact]
    public void Resolve_NewClipLandsInsideExisting_SplitsIntoFrontAndBack()
    {
        // Existing 0..20 (source 0..20); new clip lands at 8..12 → existing splits into
        // a front remainder 0..8 (source 0..8) and a back remainder 12..20 (source 12..20).
        var existing = new TrimmedSegment(Start: 0, Duration: 20, SourceStart: 0, SourceEnd: 20);

        var result = OverwriteEditCalculator.Resolve(insertStart: 8, insertDuration: 4, existing);

        Assert.Equal(2, result.Count);
        Assert.Equal(0, result[0].Start, precision: 9);
        Assert.Equal(8, result[0].Duration, precision: 9);
        Assert.Equal(0, result[0].SourceStart, precision: 9);
        Assert.Equal(8, result[0].SourceEnd, precision: 9);

        Assert.Equal(12, result[1].Start, precision: 9);
        Assert.Equal(8, result[1].Duration, precision: 9);
        Assert.Equal(12, result[1].SourceStart, precision: 9);
        Assert.Equal(20, result[1].SourceEnd, precision: 9);
    }

    [Fact]
    public void Resolve_SplitOnAlreadyTrimmedClip_PreservesSourceOffsets()
    {
        // Existing is already trimmed: on-timeline 10..30 maps to source 5..25.
        // New clip lands at 15..20 (5s in the middle) → front 10..15 (source 5..10),
        // back 20..30 (source 15..25).
        var existing = new TrimmedSegment(Start: 10, Duration: 20, SourceStart: 5, SourceEnd: 25);

        var result = OverwriteEditCalculator.Resolve(insertStart: 15, insertDuration: 5, existing);

        Assert.Equal(2, result.Count);
        Assert.Equal(10, result[0].Start, precision: 9);
        Assert.Equal(5, result[0].Duration, precision: 9);
        Assert.Equal(5, result[0].SourceStart, precision: 9);
        Assert.Equal(10, result[0].SourceEnd, precision: 9);

        Assert.Equal(20, result[1].Start, precision: 9);
        Assert.Equal(10, result[1].Duration, precision: 9);
        Assert.Equal(15, result[1].SourceStart, precision: 9);
        Assert.Equal(25, result[1].SourceEnd, precision: 9);
    }
}
