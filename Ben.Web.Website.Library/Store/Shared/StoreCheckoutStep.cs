namespace Ben.Web.Website.Library.Store.Shared;

/// <summary>Where a buyer is on the way to an order — Smarty's four steps (storefront).</summary>
public enum StoreCheckoutStep
{
    Cart = 0,
    PlaceOrder = 1,
    Payment = 2,
    Complete = 3,
}
