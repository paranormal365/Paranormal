using Ben.Service.Models.Store;


namespace Ben.Web.Website.Library.Store.Checkout;

/// <summary>Where the checkout page is: typing the order, or paying for the order it placed.</summary>
public enum CheckoutPhase { PlaceOrder, Payment }

/// <summary>
/// What the checkout page holds (storefront S4.10): the email, the addresses, the terms box, and —
/// once "Continue to payment" has placed the order — what the server answered.
/// </summary>
/// <remarks>
/// <para><b>Locked while paying.</b> The order the server placed was priced from these exact fields
/// (its tax from this address); a card confirmed after a field changed would pay for an order the
/// buyer no longer sees. So once <see cref="Accept"/> runs, every setter refuses until
/// <see cref="Unlock"/> — the page's "Edit" — throws the placed order away and a new "Continue"
/// prices again. The page also swaps the fields for a read-only summary; the lock is what holds
/// even if something still writes to a field.</para>
/// </remarks>
public sealed class CheckoutForm
{
    private string _email = "";
    private string? _billCompany;
    private string? _buyerNotes;
    private bool _billingSameAsShipping = true;
    private bool _agreedToTerms;

    public CheckoutForm()
    {
        Shipping = new CheckoutAddress(this);
        Billing = new CheckoutAddress(this);
    }

    public CheckoutPhase Phase { get; private set; } = CheckoutPhase.PlaceOrder;

    public bool IsLocked => Phase == CheckoutPhase.Payment;

    /// <summary>The placed order waiting for payment; null before "Continue" and after "Edit".</summary>
    public StoreCheckoutPrepared? Prepared { get; private set; }

    public string Email { get => _email; set => Set(ref _email, value ?? ""); }
    public CheckoutAddress Shipping { get; }
    public CheckoutAddress Billing { get; }
    public bool BillingSameAsShipping { get => _billingSameAsShipping; set => Set(ref _billingSameAsShipping, value); }
    public string? BillCompany { get => _billCompany; set => Set(ref _billCompany, value); }
    public bool AgreedToTerms { get => _agreedToTerms; set => Set(ref _agreedToTerms, value); }
    public string? BuyerNotes { get => _buyerNotes; set => Set(ref _buyerNotes, value); }

    public StoreCheckoutRequest ToRequest() => new(
        Email.Trim(), Shipping.ToInput(), BillingSameAsShipping ? null : Billing.ToInput(),
        BillingSameAsShipping || string.IsNullOrWhiteSpace(BillCompany) ? null : BillCompany.Trim(),
        AgreedToTerms, string.IsNullOrWhiteSpace(BuyerNotes) ? null : BuyerNotes.Trim());

    /// <summary>The first thing to fix before "Continue", in the server's own words; null when it may be sent.</summary>
    public string? Problem() => StoreCheckoutRules.Problem(ToRequest());

    /// <summary>The server placed the order: the fields lock and the payment step begins.</summary>
    public void Accept(StoreCheckoutPrepared prepared)
    {
        Prepared = prepared;
        Phase = CheckoutPhase.Payment;
    }

    /// <summary>"Edit": back to typing. The placed order is forgotten; the next "Continue" prices afresh.</summary>
    public void Unlock()
    {
        Prepared = null;
        Phase = CheckoutPhase.PlaceOrder;
    }

    /// <summary>Starts the email from the signed-in account, unless something is typed already.</summary>
    public void Prefill(string? email, string? fullName)
    {
        if (IsLocked) return;
        if (string.IsNullOrWhiteSpace(_email) && !string.IsNullOrWhiteSpace(email)) _email = email.Trim();
        if (string.IsNullOrWhiteSpace(Shipping.FullName) && !string.IsNullOrWhiteSpace(fullName)) Shipping.FullName = fullName.Trim();
    }

    internal void Set<T>(ref T field, T value)
    {
        if (IsLocked) return;
        field = value;
    }
}

/// <summary>One US address on the checkout form; it refuses changes while its form is locked.</summary>
public sealed class CheckoutAddress
{
    private readonly CheckoutForm _form;
    private string _fullName = "", _phone = "", _street1 = "", _city = "", _state = "", _zip = "";
    private string? _street2;

    internal CheckoutAddress(CheckoutForm form) => _form = form;

    public string FullName { get => _fullName; set => _form.Set(ref _fullName, value ?? ""); }
    public string Phone { get => _phone; set => _form.Set(ref _phone, value ?? ""); }
    public string Street1 { get => _street1; set => _form.Set(ref _street1, value ?? ""); }
    public string? Street2 { get => _street2; set => _form.Set(ref _street2, value); }
    public string City { get => _city; set => _form.Set(ref _city, value ?? ""); }
    public string State { get => _state; set => _form.Set(ref _state, value ?? ""); }
    public string Zip { get => _zip; set => _form.Set(ref _zip, value ?? ""); }

    public StoreAddressInput ToInput() => new(
        FullName.Trim(), Phone.Trim(), Street1.Trim(), string.IsNullOrWhiteSpace(Street2) ? null : Street2.Trim(),
        City.Trim(), State.Trim(), Zip.Trim());

    /// <summary>The address as it would be printed on a label, one line per row.</summary>
    public IEnumerable<string> Lines()
    {
        yield return FullName.Trim();
        yield return Street1.Trim();
        if (!string.IsNullOrWhiteSpace(Street2)) yield return Street2.Trim();
        yield return $"{City.Trim()}, {UsStates.Normalize(State) ?? State} {Zip.Trim()}";
        yield return Phone.Trim();
    }
}
