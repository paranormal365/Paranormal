namespace Ben.Data.WebApi.Services;

/// <summary>
/// Writes accumulated rate-limit refusal counts to the tracking table on a timer.
/// </summary>
/// <remarks>
/// <para>The counting itself happens in memory on the refusal path, which is what keeps fifty
/// thousand refusals from costing fifty thousand inserts. Something has to move those numbers to
/// where a SuperAdmin can read them, and a timer is the whole of it.</para>
///
/// <para>The final flush on shutdown matters more than it looks: without it, the last interval's
/// refusals are lost on every deploy, and a limit that only ever bites during a busy evening
/// could show a total that never moves.</para>
/// </remarks>
public sealed class RateLimitFlushService : BackgroundService
{
    private readonly RateLimitAlerting _alerting;
    private readonly ILogger<RateLimitFlushService> _logger;

    public RateLimitFlushService(RateLimitAlerting alerting, ILogger<RateLimitFlushService> logger)
    {
        _alerting = alerting;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RateLimitAlerting.FlushInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await _alerting.FlushAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down — fall through to the final flush below.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The rate-limit flush loop stopped unexpectedly.");
        }

        // Not stoppingToken: it is already cancelled, and this write is the reason we are here.
        await _alerting.FlushAsync(CancellationToken.None);
    }
}
