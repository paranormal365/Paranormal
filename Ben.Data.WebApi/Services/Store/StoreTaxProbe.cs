using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using StripeTax = Stripe.Tax;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>What the Stripe dashboard says about sales tax, reduced to what the settings page shows.</summary>
/// <param name="Active">Stripe Tax is switched on in the dashboard.</param>
/// <param name="RegisteredStates">US states Stripe collects tax in; anywhere else is taxed at $0.</param>
/// <param name="Problem">Why Stripe could not be asked, in words; null when it answered.</param>
public sealed record StoreTaxReadiness(bool Active, bool HasHeadOffice, IReadOnlyList<string> RegisteredStates, string? Problem);

/// <summary>
/// Asks Stripe whether it is ready to work out sales tax (storefront S1.7a). The settings page's
/// ready-to-sell checklist is its only reader in S1; S4's tax service extends it.
/// </summary>
public interface IStoreTaxProbe
{
    Task<StoreTaxReadiness> ProbeAsync(CancellationToken ct = default);
}

/// <summary>The real probe: Stripe's tax settings and its active US registrations.</summary>
/// <remarks>
/// Bounded at ten seconds and never throws: the settings page must load when Stripe is slow or
/// down, and say so, rather than hang or show an error page.
/// </remarks>
public class StripeTaxProbe(IOptions<StripeOptions> options, ILogger<StripeTaxProbe> log) : IStoreTaxProbe
{
    public const string NotSetUp = "Online payment isn't set up.";
    public const string NoAnswer = "Stripe didn't answer — try again in a minute.";

    protected StripeClient? Client { get; } =
        options.Value.IsConfigured ? new StripeClient(options.Value.SecretKey) : null;

    public async Task<StoreTaxReadiness> ProbeAsync(CancellationToken ct = default)
    {
        if (Client is null) return new(false, false, [], NotSetUp);

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var settings = await new StripeTax.SettingsService(Client).GetAsync(cancellationToken: bounded.Token);
            var states = new List<string>();
            await foreach (var registration in new StripeTax.RegistrationService(Client)
                               .ListAutoPagingAsync(new StripeTax.RegistrationListOptions { Status = "active" }, cancellationToken: bounded.Token))
            {
                if (registration.Country == "US" && registration.CountryOptions?.Us?.State is { Length: > 0 } state)
                    states.Add(state.ToUpperInvariant());
            }
            return new(settings.Status == "active", settings.HeadOffice is not null, states.Distinct().Order().ToList(), null);
        }
        catch (Exception ex) when (ex is StripeException or OperationCanceledException or HttpRequestException && !ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "Stripe Tax readiness could not be read");
            return new(false, false, [], NoAnswer);
        }
    }
}

/// <summary>Development without Stripe: tax is on and Tennessee is registered.</summary>
public sealed class FakeStoreTaxProbe : IStoreTaxProbe
{
    public Task<StoreTaxReadiness> ProbeAsync(CancellationToken ct = default)
        => Task.FromResult(new StoreTaxReadiness(true, true, ["TN"], null));
}

/// <summary>
/// When the store talks to pretend Stripe instead of the real one (storefront): only in
/// Development, only with no secret key, and only when <c>Stripe:AllowFakeCheckout</c> says so.
/// </summary>
/// <remarks>
/// Three conditions so no one of them can switch it on by accident — a Production host with a
/// forgotten key must fail loudly ("Online payment isn't set up."), never take pretend payments.
/// </remarks>
public static class StoreStripeMode
{
    public const string AllowFakeKey = "Stripe:AllowFakeCheckout";

    public static bool UseFakes(IHostEnvironment env, IConfiguration config)
        => env.IsDevelopment()
        && string.IsNullOrWhiteSpace(config["Stripe:SecretKey"])
        && config.GetValue<bool>(AllowFakeKey);
}

/// <summary>
/// Whether the store can take a card, as far as configuration goes (storefront S1.7). Read by the
/// ready-to-sell checklist; the keys themselves never leave the server.
/// </summary>
/// <param name="Fake">Pretend Stripe is in use (<see cref="StoreStripeMode"/>) — Development only.</param>
public sealed record StorePaymentSetup(bool Fake, bool HasSecretKey, bool HasPublishableKey)
{
    public static StorePaymentSetup From(IHostEnvironment env, IConfiguration config)
        => new(StoreStripeMode.UseFakes(env, config),
               !string.IsNullOrWhiteSpace(config["Stripe:SecretKey"]),
               config["Stripe:PublishableKey"]?.Trim().StartsWith("pk_", StringComparison.Ordinal) == true);
}
