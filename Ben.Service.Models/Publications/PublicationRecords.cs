namespace Ben.Service.Models.Publications;

/// <summary>A publication, as its owning group sees it.</summary>
/// <remarks>
/// Carries draft counts, which the public record deliberately does not — a visitor has no business
/// knowing how much unpublished work a group is sitting on.
/// </remarks>
public sealed record PublicationRecord(
    Guid Id,
    Guid OrganizationId,
    string Title,
    string UrlName,
    string? Description,
    bool IsPublic,
    int PublishedPostCount,
    int DraftPostCount,
    int SubscriberCount,
    DateTime DateCreated);

/// <summary>A publication as a visitor sees it.</summary>
public sealed record PublicPublicationRecord(
    string UrlName,
    string Title,
    string? Description,
    string OrganizationName,
    string? OrganizationUrlName,
    int PostCount,
    DateTime? LatestPostUtc);

/// <summary>A post, as its author sees it — including drafts.</summary>
public sealed record PublicationPostRecord(
    Guid Id,
    Guid PublicationId,
    string Title,
    string UrlName,
    string? Excerpt,
    string BodyHtml,
    DateTime? PublishedUtc,
    int? RequiredTier,
    DateTime DateCreated,
    DateTime? DateUpdated);

/// <summary>
/// A post as a visitor sees it.
/// </summary>
/// <param name="BodyHtml">
/// Null when the reader does not hold the post's tier. The server withholds it rather than sending
/// it for the page to hide — markup delivered to a browser has been delivered, whatever the page
/// then does with it.
/// </param>
/// <param name="RequiresSubscription">
/// True when the body was withheld. Lets the reader say why there is an excerpt and no article,
/// without the page having to infer it from a null.
/// </param>
public sealed record PublicPublicationPostRecord(
    string UrlName,
    string Title,
    string? Excerpt,
    string? BodyHtml,
    DateTime PublishedUtc,
    bool RequiresSubscription,
    string PublicationTitle,
    string PublicationUrlName);

/// <summary>Creating or renaming a publication. The URL name is derived once, on create.</summary>
public sealed record SavePublicationRequest(string Title, string? Description, bool IsPublic);

/// <summary>Writing a post. Publishing is a separate act.</summary>
public sealed record SavePublicationPostRequest(string Title, string? Excerpt, string BodyHtml);

/// <summary>One of the caller's own subscriptions.</summary>
public sealed record MySubscriptionRecord(
    string PublicationUrlName,
    string PublicationTitle,
    string OrganizationName,
    DateTime SubscribedUtc,
    DateTime? LatestPostUtc);
