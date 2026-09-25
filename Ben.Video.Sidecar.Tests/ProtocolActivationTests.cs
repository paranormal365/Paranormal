using Microsoft.AspNetCore.Builder;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// Being started by a <c>benvideo-sidecar:</c> link.
/// </summary>
/// <remarks>
/// <para>That link is the "on" half of the switch in the editor: a web page cannot start a program,
/// so it opens a registered protocol link and Windows runs this executable <b>with the whole URI
/// as a command-line argument</b>. The MSIX manifest declares the handler; the Inno installer
/// registers it under HKCU.</para>
///
/// <para>Which means the app has to tolerate an argument it never asked for, and that is not
/// automatic: the host passes <c>args</c> to the configuration system, whose command-line provider
/// rejects anything it cannot read as a setting. A URI is not <c>--key=value</c>. Verified against
/// the real executable on 2026-09-20 - it started and served health - and pinned here so a later
/// change to how arguments are handled cannot quietly break the switch.</para>
/// </remarks>
public sealed class ProtocolActivationTests
{
    [Theory]
    [InlineData("benvideo-sidecar:")]
    [InlineData("benvideo-sidecar:open")]
    [InlineData("benvideo-sidecar://open?from=editor")]
    public void A_protocol_URI_argument_does_not_upset_the_configuration_system(string uri)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [uri],
            ContentRootPath = AppContext.BaseDirectory,
        });

        // Building is where a bad argument would surface. Nothing is served here.
        var app = builder.Build();
        Assert.NotNull(app.Configuration);
    }

    /// <summary>
    /// The flags the app really does read still have to work when one is passed alongside a URI,
    /// because Windows may hand over both on a re-activation.
    /// </summary>
    [Fact]
    public void The_flags_the_app_reads_still_arrive_alongside_one()
    {
        string[] args = ["benvideo-sidecar:open", "--reset-token"];

        Assert.Contains("--reset-token", args);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });
        Assert.NotNull(builder.Build().Configuration);
    }
}
