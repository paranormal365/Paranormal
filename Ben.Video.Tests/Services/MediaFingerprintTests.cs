using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Deciding whether a re-fetched file is the file the project was saved against.
/// </summary>
/// <remarks>
/// The wrong answer here silently edits somebody's project against different footage, which is
/// worse than saying the media is missing (2026-09-05 audit, F14).
/// </remarks>
public sealed class MediaFingerprintTests
{
    [Fact]
    public void The_same_file_matches()
    {
        var verdict = MediaFingerprint.Compare(1024, "abc", 1024, "abc");

        Assert.Equal(MediaFingerprint.Verdict.Matches, verdict);
    }

    [Fact]
    public void A_different_size_is_a_different_file()
    {
        var verdict = MediaFingerprint.Compare(1024, "abc", 2048, "abc");

        Assert.Equal(MediaFingerprint.Verdict.Differs, verdict);
    }

    [Fact]
    public void A_file_of_the_same_size_with_different_contents_is_caught_by_the_hash()
    {
        var verdict = MediaFingerprint.Compare(1024, "abc", 1024, "def");

        Assert.Equal(MediaFingerprint.Verdict.Differs, verdict);
    }

    [Fact]
    public void Hashes_are_compared_regardless_of_case()
    {
        var verdict = MediaFingerprint.Compare(1024, "ABC", 1024, "abc");

        Assert.Equal(MediaFingerprint.Verdict.Matches, verdict);
    }

    /// <summary>
    /// The distinction the whole type exists for: a hash that was never taken is not a mismatch.
    /// </summary>
    /// <remarks>
    /// Large footage is never hashed, and a project saved before hashing existed has no hash at
    /// all. Reading either as "did not match" would refuse media that is perfectly good.
    /// </remarks>
    [Theory]
    [InlineData(null, "abc")]
    [InlineData("abc", null)]
    [InlineData(null, null)]
    public void A_missing_hash_does_not_fail_a_file_whose_size_is_right(
        string? expected, string? actual)
    {
        var verdict = MediaFingerprint.Compare(1024, expected, 1024, actual);

        Assert.Equal(MediaFingerprint.Verdict.Matches, verdict);
    }

    /// <summary>
    /// Nothing recorded is its own answer, kept apart from "matches" so the caller decides.
    /// </summary>
    [Fact]
    public void With_nothing_recorded_the_answer_is_unknown_not_yes()
    {
        var verdict = MediaFingerprint.Compare(null, null, 1024, "abc");

        Assert.Equal(MediaFingerprint.Verdict.Unknown, verdict);
    }

    [Fact]
    public void An_ordinary_clip_is_worth_hashing()
        => Assert.True(MediaFingerprint.ShouldHash(48_900_846));

    /// <summary>
    /// A session recording is not. The browser's digest has no streaming form, so hashing one
    /// means a second full copy in memory on the thread the whole editor runs on.
    /// </summary>
    [Fact]
    public void A_session_recording_is_not()
        => Assert.False(MediaFingerprint.ShouldHash(538L * 1024 * 1024));

    [Fact]
    public void An_empty_file_is_not_worth_hashing_either()
        => Assert.False(MediaFingerprint.ShouldHash(0));
}
