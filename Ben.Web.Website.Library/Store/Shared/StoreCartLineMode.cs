namespace Ben.Web.Website.Library.Store.Shared;

/// <summary>Which of Smarty's four item shapes a cart line takes (storefront S3.5).</summary>
public enum StoreCartLineMode
{
    /// <summary>The cart page's card: picture, name, unit price, quantity, stock, totals and savings.</summary>
    Page,

    /// <summary>The drawer's row: picture, name, quantity, line total and "$x / item".</summary>
    Drawer,

    /// <summary>The header menu's row: thumbnail, "2 × name", line total. A link to the product.</summary>
    Dropdown,

    /// <summary>The checkout's "Your items": read-only, the quantity on the picture.</summary>
    ReadOnly,
}
