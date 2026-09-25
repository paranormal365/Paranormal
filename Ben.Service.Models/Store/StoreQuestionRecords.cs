using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Store;

// Store sellers, backlog 251, P12: a product's FAQ and shoppers' questions.

/// <summary>One FAQ entry as a shopper reads it — no names, no dates.</summary>
public sealed record StoreFaqView(Guid Id, string Question, string Answer);

/// <summary>A product's FAQ as the editor holds it: the switch, and the entries in order.</summary>
public sealed record StoreFaqsRecord(bool Enabled, IReadOnlyList<StoreFaqLine> Faqs);

/// <summary>One FAQ entry in the editor. No id is a new one.</summary>
public sealed record StoreFaqLine(Guid? Id, string Question, string Answer);

/// <summary>The FAQ, saved whole: entries not listed are removed; their order is the list's.</summary>
public sealed record SaveStoreFaqsRequest(bool Enabled, IReadOnlyList<StoreFaqLine> Faqs);

public sealed record AskStoreQuestionRequest(string Question);

/// <summary>A question as its asker sees it — their words, and the answer if one came.</summary>
public sealed record StoreAskedQuestionRecord(
    Guid Id, Guid ProductId, string ProductName, string ProductSlug, string Question, string? Answer,
    StoreQuestionStatus Status, DateTime AskedUtc, DateTime? AnsweredUtc);

/// <summary>
/// A question as the answering side sees it. It has nowhere to put who asked — the anonymity is in
/// the shape, the same as the equipment questions.
/// </summary>
/// <param name="SiteStock">The store's own stock: the store answers it.</param>
public sealed record StoreReceivedQuestionRecord(
    Guid Id, Guid ProductId, string ProductName, string Question, string? Answer, StoreQuestionStatus Status,
    DateTime AskedUtc, DateTime? AnsweredUtc, bool Promoted, bool SiteStock, string? SellerName);

/// <summary>An answer — or, with <paramref name="Decline"/>, a no, with an optional note in <paramref name="Answer"/>.</summary>
public sealed record AnswerStoreQuestionRequest(string? Answer, bool Decline);

/// <summary>An answered question copied into the FAQ, reworded for everybody if need be.</summary>
public sealed record PromoteStoreQuestionRequest(string Question, string Answer);

// Store sellers, backlog 251, P13: versions.

/// <summary>Starts a new version of an item: what it's called, and what happens to this one when it goes on sale.</summary>
public sealed record StartStoreVersionRequest(string VersionLabel, StoreSupersededPolicy Policy);

/// <summary>A version's label, and — until it has gone on sale — what happens to the one before.</summary>
public sealed record SaveStoreVersionRequest(string? VersionLabel, StoreSupersededPolicy Policy);

/// <summary>Another version of a product, to link to.</summary>
public sealed record StoreVersionLink(Guid Id, string Name, string Slug, string? VersionLabel);

/// <summary>
/// An item's place among its versions, for its editors. <paramref name="PolicyApplied"/>: this version
/// has gone on sale and its policy has been applied to <paramref name="Previous"/>, so it's fixed now.
/// </summary>
public sealed record StoreVersionInfo(
    string? VersionLabel, StoreSupersededPolicy Policy, bool PolicyApplied, StoreVersionLink? Previous, StoreVersionLink? Next,
    DateTime? SellingOutSinceUtc, DateTime? DiscontinuedUtc);

/// <summary>The new version's id, for its editor.</summary>
public sealed record StoreNewVersionRecord(Guid ProductId);

// Store sellers, backlog 251, P14: page extras.

/// <summary>A product's video, for its page and its editors.</summary>
public sealed record StoreVideoRecord(Guid Id, Guid UploadFileId, string? Title, string ContentType, int SortOrder);

/// <summary>A product's page extras, as its editors hold them.</summary>
public sealed record StoreExtrasRecord(string? ReturnPolicyText, string? WarrantyText, bool ReviewsEnabled, IReadOnlyList<StoreVideoRecord> Videos);

/// <summary>The store's save: the words, and the reviews switch.</summary>
public sealed record SaveStoreExtrasRequest(string? ReturnPolicyText, string? WarrantyText, bool ReviewsEnabled);

/// <summary>A seller's save: the words only — the reviews switch is the store's.</summary>
public sealed record SaveSellerExtrasRequest(string? ReturnPolicyText, string? WarrantyText);
