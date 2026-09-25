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
