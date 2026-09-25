namespace Ben.Data.Common.Enums;

/// <summary>What part of a store item a history line is about (store sellers, backlog 251, P2).</summary>
public enum StoreProductChangeArea
{
    /// <summary>The item was made, or copied from another.</summary>
    Created = 0,

    /// <summary>Name, words, specifications, category, web address and the like.</summary>
    Details = 1,

    /// <summary>Put on sale, or taken off.</summary>
    Sale = 2,

    /// <summary>Who sells it.</summary>
    Seller = 3,

    /// <summary>The options (Colour, Size) and their values.</summary>
    Options = 4,

    /// <summary>Variants added, removed, switched on or off, or given a new SKU.</summary>
    Variants = 5,

    /// <summary>A variant's price or old price. The store's alone: a seller's history leaves these out.</summary>
    Price = 6,

    /// <summary>Stock counted in or written off by hand. Sales are in the stock log, not here.</summary>
    Stock = 7,

    /// <summary>Pictures added, removed, reordered or described.</summary>
    Pictures = 8,

    /// <summary>The parts list and the "other" cost line — what a unit costs to make (P4).</summary>
    Parts = 9,

    /// <summary>Files: manuals, firmware, documents — added, changed, removed (P11).</summary>
    Files = 10,

    /// <summary>The FAQ: entries written, changed, removed, promoted from a question; switched on or off (P12).</summary>
    Faq = 11,
}
