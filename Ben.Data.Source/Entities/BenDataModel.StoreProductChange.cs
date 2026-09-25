using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One line of a store item's history: who changed what, in a plain sentence (store sellers,
    /// backlog 251, P2).
    /// </summary>
    /// <remarks>
    /// <para>Ben, 09/24/2026: edits to a live item show at once, "and history records who changed
    /// what". Append-only, and added in the same <c>SaveChanges</c> as the edit it describes, so a
    /// change that did not happen has no line and one that did cannot lose its line.</para>
    ///
    /// <para>The summary is written at the time, in words, rather than stored as a before/after
    /// diff to be described later: the category, variant and seller it names may be renamed or
    /// gone by the time anybody reads it.</para>
    /// </remarks>
    public class StoreProductChange
    {
        /// <summary>Room for a sentence or two; a longer one is cut with an ellipsis.</summary>
        public const int MaxSummaryLength = 500;

        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public StoreProductChangeArea Area { get; set; }
        public string Summary { get; set; } = string.Empty;
        public Guid? ActorAppUserId { get; set; }
        public StoreChangeActor ActorRole { get; set; }
        public DateTime OccurredUtc { get; set; }

        public virtual StoreProduct Product { get; set; } = null!;
        public virtual AppUser? ActorAppUser { get; set; }
    }
}
