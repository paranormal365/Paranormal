namespace Ben.Data.Common.Enums;

/// <summary>
/// What one labelled example says about a post's chosen experience type (item 186 F6).
/// </summary>
/// <remarks>Append-only, like every enum here: the numbers are in the database.</remarks>
public enum FeedLabel
{
    /// <summary>Somebody with standing agreed the content is what its type says.</summary>
    Confirmed = 1,

    /// <summary>Somebody with standing said the type does not fit the content.</summary>
    Mismatch = 2,
}

/// <summary>Who produced a labelled example — provenance the re-fit can weigh.</summary>
/// <remarks>Append-only.</remarks>
public enum FeedLabelSource
{
    /// <summary>A site moderator, deciding in the review queue.</summary>
    Moderator = 1,

    /// <summary>The owning group claiming the post as its own (item 186 F7's verification).</summary>
    GroupClaim = 2,

    /// <summary>The author accepting a mismatch nudge and recategorizing.</summary>
    PosterCorrection = 3,
}
