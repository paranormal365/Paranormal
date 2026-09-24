namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// Gives back the stock and codes of checkouts nobody finished (storefront S4.8), every minute.
/// </summary>
/// <remarks>
/// <para><b>Its own timer, not the five-minute scheduled work.</b> A checkout holds stock for
/// fifteen minutes; on the shared timer a hold could outlive that by up to five more, keeping the
/// last unit from everybody else for a third longer than the page promised.</para>
///
/// <para>Each expired order is cancelled AT STRIPE first (<see cref="StoreOrderPayments.ExpireReservationsAsync"/>):
/// one whose payment is going through is left for the webhook. A pass that throws is logged and the
/// next minute tries again.</para>
/// </remarks>
public sealed class StoreReservationExpiryService(IServiceScopeFactory scopes, ILogger<StoreReservationExpiryService> log) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var released = await scope.ServiceProvider.GetRequiredService<StoreOrderPayments>().ExpireReservationsAsync(stoppingToken);
                if (released > 0) log.LogInformation("Released {Count} expired store checkout(s).", released);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                log.LogError(ex, "Releasing expired store checkouts failed; trying again in a minute.");
            }
        }
    }
}
