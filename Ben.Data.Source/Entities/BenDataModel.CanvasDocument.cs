using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A saved case canvas board: cards, notes, maps, pictures and links laid out on one surface,
    /// as the canvas editor's own JSON. On a case it is the case's document; with no case it is
    /// its author's.
    /// </summary>
    /// <remarks>
    /// <para><b>The JSON is stored whole and never shredded into tables.</b> The server's jobs are
    /// to keep it, to say who may reach it and to refuse a stale save. Everything the board means
    /// is the editor's business, and a schema mirroring its node types would need a migration
    /// every time the editor gained a block.</para>
    ///
    /// <para><b>Why <see cref="Revision"/> exists.</b> A video project is its author's; a board on a
    /// case may be saved by anybody holding Cases Update in the case's group. Two investigators
    /// working the same board would otherwise overwrite each other silently, the second save
    /// winning without either of them knowing. So a save names the revision it loaded, a stale one
    /// is answered 409 with the server's copy, and the column is an EF concurrency token so the
    /// check happens inside the UPDATE rather than in a read-then-write window two saves can both
    /// slip through.</para>
    ///
    /// <para>An <c>int</c> rather than a SQL <c>rowversion</c> because the browser has to carry it
    /// back as text in <c>If-Match</c> and put it in a sentence; eight opaque bytes serve neither.</para>
    /// </remarks>
    public class CanvasDocument : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>The case the board belongs to, or null for a personal board.</summary>
        public Guid? CaseId { get; set; }

        /// <summary>The board's title, from the document's own <c>title</c>; at most 256 characters.</summary>
        public string Name { get; set; } = null!;

        /// <summary>The canvas editor's document JSON, stored as nvarchar(max). Message HTML inside
        /// it has already been through the server's markup sanitiser.</summary>
        public string DocumentJson { get; set; } = null!;

        /// <summary>Starts at 1; every accepted save adds one. The optimistic-concurrency token.</summary>
        public int Revision { get; set; }

        /// <summary>
        /// The board as the group last saw it, or null while it has never been published.
        /// </summary>
        /// <remarks>
        /// <para>Ben, 2026-09-16: research is written on the writer's own machine and kept to themselves until it is
        /// ready — "they can keep the drafts which are not displayed to the members until it is published". So
        /// <see cref="DocumentJson"/> is the working draft and this is what everybody else reads. A board with nothing
        /// here is not in the case's list at all: an unfinished thought is not evidence, and a list full of them is
        /// worse than an empty one.</para>
        /// <para>Publishing copies the draft here and records the revision it came from, so "published, and written on
        /// since" is a fact the list can state rather than a guess.</para>
        /// </remarks>
        public string? PublishedJson { get; set; }

        /// <summary><see cref="Revision"/> as it stood when <see cref="PublishedJson"/> was taken.</summary>
        public int? PublishedRevision { get; set; }

        /// <summary>The PNG snapshot last published to the case, if any.</summary>
        public Guid? PublishedUploadFileId { get; set; }

        /// <summary>When the snapshot was published. Publishing is an act with a time and a person,
        /// not a property the file happens to have.</summary>
        public DateTime? PublishedAtUtc { get; set; }

        /// <summary>Who published it.</summary>
        public Guid? PublishedByAppUserId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Case? Case { get; set; }
        public virtual UploadFile? PublishedUploadFile { get; set; }
        public virtual AppUser? PublishedByAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
