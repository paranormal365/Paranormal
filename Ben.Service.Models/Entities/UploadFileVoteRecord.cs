namespace Ben.Service.Models.Entities;

/// <summary>A single user's vote on an <c>UploadFile</c>.</summary>
public record UploadFileVoteRecord
{
    public Guid      Id           { get; init; }
    public Guid      UploadFileId { get; init; }
    public Guid      AppUserId    { get; init; }
    /// <summary>Typical values: 1 = upvote, -1 = downvote. Integer allows future star-rating schemes.</summary>
    public int       Score        { get; init; }
    public DateTime  DateCreated  { get; init; }
    public DateTime? DateUpdated  { get; init; }
}

// ── Aggregated summary returned by GET ───────────────────────────────────────

/// <summary>Vote totals for an <c>UploadFile</c>, optionally including the calling user's vote.</summary>
public record UploadFileVoteSummary(
    Guid   UploadFileId,
    int    UpvoteCount,
    int    DownvoteCount,
    int    TotalScore,
    int    TotalVotes,
    /// <summary>Null = current user has not voted; otherwise their score.</summary>
    int?   UserScore);

// ── Requests ─────────────────────────────────────────────────────────────────

/// <summary>Request body for <c>PUT /api/upload-files/{id}/my-vote</c>.</summary>
public record UpsertVoteRequest(int Score);
