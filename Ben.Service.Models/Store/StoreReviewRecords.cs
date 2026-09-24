using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Store;

// Favourites and buyer reviews (storefront S6.1).

/// <summary>An approved review, as a shopper reads it.</summary>
/// <param name="IsMine">Written by the person reading.</param>
/// <param name="IVotedHelpful">The person reading has marked it helpful.</param>
public sealed record StoreReviewRecord(
    Guid Id, int Rating, string Title, string Body, string AuthorName, DateTime CreatedUtc, int HelpfulCount,
    string? AdminReply, DateTime? AdminRepliedUtc, bool IsMine, bool IVotedHelpful);

public sealed record StoreReviewPage(IReadOnlyList<StoreReviewRecord> Reviews, int Total, int Page, int PageSize, string Sort);

public sealed record SubmitStoreReviewRequest(int Rating, string? Title, string? Body);

/// <summary>The reader's own review of a product, whatever its state.</summary>
/// <param name="RejectionReason">Why it was not published, when it was refused.</param>
public sealed record MyStoreReviewRecord(
    Guid Id, int Rating, string Title, string Body, StoreReviewStatus Status, string? RejectionReason, DateTime CreatedUtc);

/// <summary>What the product page needs to know about the person looking at it.</summary>
/// <param name="CannotReviewReason">Why not, in words; null when they may.</param>
public sealed record StoreProductViewerState(bool IsFavourite, bool CanReview, string? CannotReviewReason, MyStoreReviewRecord? MyReview);

public sealed record StoreHelpfulVoteResult(int HelpfulCount, bool IVotedHelpful);

public sealed record StoreFavouriteCount(int Count);

/// <summary>The review rules' sentences — the API says them, the page and tests look for them.</summary>
public static class StoreReviewSentences
{
    public const int MaxTitle = 120;
    public const int MaxBody = 3000;
    public const int MinBodyWords = 3;

    public const string PickStars = "Pick between one and five stars.";
    public const string NeedsTitle = "Give your review a title.";
    public static readonly string TitleTooLong = $"A title is {MaxTitle} characters at most.";
    public const string NeedsWords = "Say something about it — a review needs a few words.";
    public const string TooLong = "That's over 3,000 characters — trim it a little.";
    public const string OnlyBuyers = "Only somebody who has bought this can review it.";
    public const string NotOwnVote = "You can't vote for your own review.";
    public const string SignInToReview = "Sign in to review what you've bought.";
    public const string NotOnSale = "That product isn't in the store.";

    /// <summary>The first thing wrong with a review as typed; null when it will do.</summary>
    public static string? Problem(SubmitStoreReviewRequest r)
    {
        if (r.Rating is < 1 or > 5) return PickStars;
        if (string.IsNullOrWhiteSpace(r.Title)) return NeedsTitle;
        if (r.Title.Trim().Length > MaxTitle) return TitleTooLong;
        if (string.IsNullOrWhiteSpace(r.Body) || r.Body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < MinBodyWords) return NeedsWords;
        if (r.Body.Trim().Length > MaxBody) return TooLong;
        return null;
    }
}

/// <summary>The review list's sorts.</summary>
public static class StoreReviewSorts
{
    public const string Popular = "popular";
    public const string Newest = "newest";
    public const string Highest = "highest";
    public const string Lowest = "lowest";
    public const string Helpful = "helpful";

    public static readonly IReadOnlyList<(string Key, string Label)> All =
        [(Popular, "Most popular"), (Newest, "Newest"), (Highest, "Highest rated"), (Lowest, "Lowest rated"), (Helpful, "Most helpful")];

    public static string Normalize(string? sort) => All.Any(s => s.Key == sort) ? sort! : Popular;
}
