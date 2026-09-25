using System.Net;
using System.Net.Http.Json;
using Ben.Video.Sidecar.Jobs;
using Ben.Video.Sidecar.Lifetime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// <c>POST /v1/shutdown</c> - the "off" half of the switch in the editor.
/// </summary>
/// <remarks>
/// Only the tests that actually stop a sidecar build their own factory, for the reason you would
/// expect: a shared one would be stopped out from under everything else in the class. The rest
/// share the class fixture, because each factory starts a real host and starting more of them than
/// the tests need makes the whole suite slower and more sensitive to a loaded machine.
/// </remarks>
public sealed class ShutdownEndpointTests(SidecarWebApplicationFactory factory)
    : IClassFixture<SidecarWebApplicationFactory>
{
    [Fact]
    public void Nothing_running_means_stop_and_a_job_running_means_ask_again()
    {
        Assert.Equal(ShutdownDecision.Stop, ShutdownPolicy.Decide(activeJobs: 0, force: false));
        Assert.Equal(ShutdownDecision.RefuseWorkInProgress, ShutdownPolicy.Decide(1, force: false));

        // Having been told, the person can still decide - it is their machine and their export.
        Assert.Equal(ShutdownDecision.Stop, ShutdownPolicy.Decide(3, force: true));
    }

    [Fact]
    public async Task A_page_that_is_not_paired_cannot_stop_it()
    {
        // The whole point of the token: a page that cannot render a segment cannot stop the
        // process either. If this ever regressed, any site the browser visited could kill it.
        var client = factory.CreateAuthenticatedClient(token: null);

        var response = await client.PostAsync("/v1/shutdown", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task It_refuses_while_a_job_is_running_and_says_how_many()
    {
        // Safe on the shared fixture: a refusal is the one answer that leaves it running.
        var client = factory.CreateAuthenticatedClient(token: factory.ReadGeneratedPairingToken());

        // A job in flight, held open for the length of the request - the same registry the runners
        // use, so this is the real condition rather than a stubbed count.
        using var job = factory.Services.GetRequiredService<JobRegistry>().EnterJob();

        var response = await client.PostAsync("/v1/shutdown", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RefusedBody>();
        Assert.Equal(1, body!.ActiveJobs);
    }

    [Fact]
    public async Task Asking_again_meaning_it_stops_even_with_a_job_running()
    {
        using var own = new SidecarWebApplicationFactory();
        var client = own.CreateAuthenticatedClient(token: own.ReadGeneratedPairingToken());
        using var job = own.Services.GetRequiredService<JobRegistry>().EnterJob();

        var response = await client.PostAsync("/v1/shutdown?force=true", content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RefusedBody>();
        Assert.Equal(1, body!.ActiveJobs);   // says what it is throwing away
    }

    [Fact]
    public async Task It_answers_first_and_stops_after()
    {
        using var own = new SidecarWebApplicationFactory();
        var client = own.CreateAuthenticatedClient(token: own.ReadGeneratedPairingToken());
        var lifetime = own.Services.GetRequiredService<IHostApplicationLifetime>();

        var response = await client.PostAsync("/v1/shutdown", content: null);

        // The answer arrives - this is the half that would break if the host were stopped inside
        // the handler, where the caller sees a dropped connection instead of a reply.
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var stopping = new TaskCompletionSource();
        using (lifetime.ApplicationStopping.Register(stopping.SetResult))
        {
            if (lifetime.ApplicationStopping.IsCancellationRequested) stopping.TrySetResult();
            var finished = await Task.WhenAny(stopping.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.True(finished == stopping.Task, "The sidecar accepted the shutdown but never stopped.");
        }
    }

    private sealed record RefusedBody(int ActiveJobs, string Message);
}
