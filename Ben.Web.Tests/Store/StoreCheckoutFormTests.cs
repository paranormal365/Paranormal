using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Ben.Web.Website.Library.Store.Checkout;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>The checkout page's form (storefront S4.10): what it sends, and that it holds still while being paid for.</summary>
public sealed class StoreCheckoutFormTests
{
    private static CheckoutForm Filled()
    {
        var form = new CheckoutForm { Email = " buyer@example.com ", AgreedToTerms = true };
        form.Shipping.FullName = "Ada Buyer";
        form.Shipping.Phone = "615-555-0100";
        form.Shipping.Street1 = "1 Elm St";
        form.Shipping.City = "Nashville";
        form.Shipping.State = "tn";
        form.Shipping.Zip = "37203";
        return form;
    }

    private static StoreCheckoutPrepared Prepared() => new(
        Guid.NewGuid(), 100042, "pi_fake_secret_fake", "pk_test_fake", new StoreCheckoutTotals(10, 0, 5, 1.2m, 16.2m),
        false, "/store/checkout/complete", DateTime.UtcNow.AddMinutes(15), FakeCheckout: true);

    [Fact]
    public void Editing_after_prepare_requires_a_new_prepare()
    {
        var form = Filled();
        form.Accept(Prepared());

        form.Shipping.Zip = "90210";
        form.Email = "someone@else.com";
        form.AgreedToTerms = false;

        Assert.True(form.IsLocked);
        Assert.Equal(("37203", "buyer@example.com", true), (form.Shipping.Zip, form.Email.Trim(), form.AgreedToTerms));

        form.Unlock();
        form.Shipping.Zip = "90210";

        Assert.Equal((CheckoutPhase.PlaceOrder, (StoreCheckoutPrepared?)null, "90210"), (form.Phase, form.Prepared, form.Shipping.Zip));
    }

    [Fact]
    public void Accepting_moves_to_the_payment_step()
    {
        var form = Filled();
        var prepared = Prepared();
        Assert.Equal(CheckoutPhase.PlaceOrder, form.Phase);

        form.Accept(prepared);

        Assert.Equal((CheckoutPhase.Payment, prepared), (form.Phase, form.Prepared));
    }

    [Fact]
    public void Billing_is_sent_only_when_it_differs()
    {
        var form = Filled();
        form.BillCompany = "Spook Co";
        Assert.Null(form.ToRequest().Billing);
        Assert.Null(form.ToRequest().BillCompany);

        form.BillingSameAsShipping = false;
        form.Billing.FullName = "Bill Payer";

        var request = form.ToRequest();
        Assert.Equal("Bill Payer", request.Billing!.FullName);
        Assert.Equal("Spook Co", request.BillCompany);
        Assert.Equal("buyer@example.com", request.Email);
    }

    /// <summary>The page checks with the server's own rules, so a sentence shown before sending is the one the server would say.</summary>
    [Fact]
    public void The_page_and_the_server_refuse_the_same_things_in_the_same_words()
    {
        var cases = new List<(Action<CheckoutForm> Break, string Sentence)>
        {
            (f => f.Email = "not-an-email", StoreCheckoutSentences.EmailInvalid),
            (f => f.Shipping.Phone = " ", StoreCheckoutSentences.Required("Phone")),
            (f => f.Shipping.State = "PR", StoreCheckoutSentences.ChooseAState),
            (f => f.Shipping.Zip = "3720", StoreCheckoutSentences.ZipInvalid),
            (f => { f.BillingSameAsShipping = false; }, StoreCheckoutSentences.Required("Full name")),
            (f => f.AgreedToTerms = false, StoreCheckoutSentences.AgreeToTerms),
        };
        Assert.Null(Filled().Problem());
        foreach (var (breakIt, sentence) in cases)
        {
            var form = Filled();
            breakIt(form);
            Assert.Equal(sentence, form.Problem());
            Assert.Equal(sentence, StoreCheckoutService.Problem(form.ToRequest()));
        }
    }

    [Fact]
    public void Prefill_never_overwrites_what_was_typed()
    {
        var form = new CheckoutForm { Email = "typed@example.com" };
        form.Prefill("account@example.com", "Account Name");
        Assert.Equal(("typed@example.com", "Account Name"), (form.Email, form.Shipping.FullName));
    }

    [Fact]
    public void The_address_prints_as_a_label()
        => Assert.Equal(["Ada Buyer", "1 Elm St", "Nashville, TN 37203", "615-555-0100"], Filled().Shipping.Lines());
}
