using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One entry in a product's FAQ (store sellers, backlog 251, P12): a question and its answer, in the
    /// words of whoever looks after the product — its seller, or the store for its own stock.
    /// </summary>
    /// <remarks>
    /// Written in the editor, or promoted from a shopper's answered question — as a <b>copy</b>, so
    /// editing the FAQ never rewrites what one person was actually told, and nobody is named.
    /// </remarks>
    public class StoreProductFaq
    {
        public const int MaxQuestionLength = 300;
        public const int MaxAnswerLength = 4000;

        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public int SortOrder { get; set; }

        public virtual StoreProduct Product { get; set; } = null!;

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
    }

    /// <summary>
    /// A shopper's question about a product (store sellers, backlog 251, P12), to its seller — or, for
    /// the store's own stock, to the store.
    /// </summary>
    /// <remarks>
    /// <para><b>Private to the asker.</b> The answer is for them alone unless whoever answered promotes
    /// it to the FAQ, which copies the words without the name. The answering side is never shown who
    /// asked: its records have nowhere to put an asker, and the bell comes from the store.</para>
    ///
    /// <para><b>About the asker</b>, so it goes with their account: closing or purging it removes their
    /// questions (an FAQ promoted from one stays — it names nobody).</para>
    /// </remarks>
    public class StoreProductQuestion
    {
        public const int MinQuestionLength = 5;
        public const int MaxQuestionLength = 1000;
        public const int MaxAnswerLength = 4000;

        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public Guid AskerAppUserId { get; set; }
        public string Question { get; set; } = string.Empty;
        public StoreQuestionStatus Status { get; set; }

        /// <summary>The answer — or, for a declined question, the note saying why (optional).</summary>
        public string? Answer { get; set; }

        /// <summary>Who answered — kept for the store's records; never shown to the asker.</summary>
        public Guid? AnsweredByAppUserId { get; set; }
        public DateTime? AnsweredUtc { get; set; }

        /// <summary>The FAQ entry this was copied into, if it was — stops it being promoted twice.</summary>
        public Guid? PromotedFaqId { get; set; }

        public virtual StoreProduct Product { get; set; } = null!;
        public virtual AppUser AskerAppUser { get; set; } = null!;

        public DateTime DateCreated { get; set; }
    }

    /// <summary>
    /// A video on a product's page (store sellers, backlog 251, P14): an uploaded file — mp4, webm or
    /// mov — at most three to a product, shown in the gallery after the pictures. Its metadata is
    /// stripped on the way in where the host can, and it's served only while a product holds it.
    /// </summary>
    public class StoreProductVideo
    {
        public const int MaxPerProduct = 3;
        public const long MaxBytes = 95L * 1024 * 1024;
        public const int MaxTitleLength = 200;

        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public Guid UploadFileId { get; set; }
        public string? Title { get; set; }
        public int SortOrder { get; set; }

        public virtual StoreProduct Product { get; set; } = null!;
        public virtual UploadFile UploadFile { get; set; } = null!;

        public DateTime DateCreated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
    }
}
