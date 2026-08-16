using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Recording that a sidecar was installed, and that someone paired with one.
/// </summary>
public class SidecarTelemetryControllerTests
{
    private static (SidecarTelemetryController Controller, IDbContextFactory<Ben.Data.Source.Context.BenDataContext> Factory)
        Build(Guid? userId = null, string ip = "203.0.113.7")
    {
        var factory = TestDbFactory.Create();
        var controller = new SidecarTelemetryController(factory, NullLogger<SidecarTelemetryController>.Instance);

        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        if (userId is { } id)
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, id.ToString())], "test"));
        }
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return (controller, factory);
    }

    private static async Task<List<SidecarInstallLog>> RowsAsync(
        IDbContextFactory<Ben.Data.Source.Context.BenDataContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.SidecarInstallLogs.ToListAsync();
    }

    [Fact]
    public async Task Install_is_recorded_without_an_account()
    {
        // The installer runs before anyone signs in — that is the whole reason this endpoint is
        // anonymous, so a null user must be a recorded row rather than a rejection.
        var (controller, factory) = Build();
        var installId = Guid.NewGuid();

        var result = await controller.RecordInstall(
            new SidecarInstallRequest(installId, "1.2.3.4", "osx-arm64"), default);

        Assert.IsType<NoContentResult>(result);
        var row = Assert.Single(await RowsAsync(factory));
        Assert.Equal(SidecarTelemetryEventTypes.Install, row.EventType);
        Assert.Equal(installId, row.InstallId);
        Assert.Equal("1.2.3.4", row.Version);
        Assert.Equal("osx-arm64", row.Platform);
        Assert.Null(row.AppUserId);
    }

    [Fact]
    public async Task Pairing_is_attributed_to_the_signed_in_user()
    {
        var userId = Guid.NewGuid();
        var (controller, factory) = Build(userId);

        await controller.RecordPairing(
            new SidecarInstallRequest(Guid.NewGuid(), "1.2.3.4", "win-x64"), default);

        var row = Assert.Single(await RowsAsync(factory));
        Assert.Equal(SidecarTelemetryEventTypes.Pair, row.EventType);
        Assert.Equal(userId, row.AppUserId);
    }

    [Fact]
    public async Task The_address_comes_from_the_connection_not_the_caller()
    {
        // A client-supplied address would be worth nothing, so the body has no field for one and
        // the value must track the connection.
        var (controller, factory) = Build(ip: "198.51.100.42");

        await controller.RecordInstall(new SidecarInstallRequest(Guid.NewGuid(), "1.0", "linux-x64"), default);

        Assert.Equal("198.51.100.42", Assert.Single(await RowsAsync(factory)).IpAddress);
    }

    [Fact]
    public async Task An_install_and_its_later_pairing_share_one_InstallId()
    {
        // This is what makes "one machine paired five times" distinguishable from "five machines".
        var userId = Guid.NewGuid();
        var installId = Guid.NewGuid();
        var (controller, factory) = Build(userId);

        await controller.RecordInstall(new SidecarInstallRequest(installId, "1.0", "osx-arm64"), default);
        await controller.RecordPairing(new SidecarInstallRequest(installId, "1.0", "osx-arm64"), default);

        var rows = await RowsAsync(factory);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(installId, r.InstallId));
        Assert.Single(rows, r => r.AppUserId == userId);
    }

    [Fact]
    public async Task A_missing_install_id_is_rejected()
    {
        var (controller, factory) = Build();

        var result = await controller.RecordInstall(
            new SidecarInstallRequest(Guid.Empty, "1.0", "osx-arm64"), default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await RowsAsync(factory));
    }

    [Theory]
    [InlineData(60, 50)]    // version column is 50
    [InlineData(80, 50)]    // platform column is 50
    public async Task Over_long_client_values_are_clamped_to_the_column(int sent, int stored)
    {
        // The values arrive from a program on someone else's machine. Trusting their length would
        // turn a hostile or buggy sidecar into a failed insert.
        var (controller, factory) = Build();

        await controller.RecordInstall(
            new SidecarInstallRequest(Guid.NewGuid(), new string('v', sent), new string('p', sent)), default);

        var row = Assert.Single(await RowsAsync(factory));
        Assert.Equal(stored, row.Version!.Length);
        Assert.Equal(stored, row.Platform!.Length);
    }

    [Fact]
    public async Task Telemetry_failure_never_fails_the_caller()
    {
        // The install or pairing has already happened by the time this is called; an error here
        // would report a failure that did not occur.
        var factory = TestDbFactory.Create();
        var controller = new SidecarTelemetryController(factory, NullLogger<SidecarTelemetryController>.Instance);
        // No HttpContext at all — reading the remote address will throw inside the handler.
        controller.ControllerContext = new ControllerContext();

        var result = await controller.RecordInstall(
            new SidecarInstallRequest(Guid.NewGuid(), "1.0", "osx-arm64"), default);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Summary_counts_installations_not_events()
    {
        // Three events from two machines, one of them paired. The counts are per INSTALLATION —
        // counting rows would say three installs exist when two do.
        var userId = Guid.NewGuid();
        var machineA = Guid.NewGuid();
        var machineB = Guid.NewGuid();
        var (controller, _) = Build(userId);

        await controller.RecordInstall(new SidecarInstallRequest(machineA, "1.0.0.0", "osx-arm64"), default);
        await controller.RecordPairing(new SidecarInstallRequest(machineA, "1.0.0.0", "osx-arm64"), default);
        await controller.RecordInstall(new SidecarInstallRequest(machineB, "2.0.0.0", "win-x64"), default);

        var summary = Assert.IsType<OkObjectResult>((await controller.GetSummary(default)).Result).Value;
        var s = Assert.IsType<SidecarTelemetrySummary>(summary);

        Assert.Equal(2, s.DistinctInstalls);
        Assert.Equal(1, s.InstallsPairedToAnAccount);
        Assert.Equal(1, s.DistinctPeople);
        Assert.Equal(2, s.ByVersion.Count);
        Assert.All(s.ByVersion, v => Assert.Equal(1, v.Installs));
    }

    [Fact]
    public async Task Events_come_back_newest_first()
    {
        var (controller, _) = Build();
        await controller.RecordInstall(new SidecarInstallRequest(Guid.NewGuid(), "1.0", "osx-arm64"), default);
        await Task.Delay(10);
        await controller.RecordInstall(new SidecarInstallRequest(Guid.NewGuid(), "2.0", "win-x64"), default);

        var rows = Assert.IsType<OkObjectResult>((await controller.GetAll()).Result).Value
                   as IReadOnlyList<SidecarInstallLogRecord>;

        Assert.Equal(2, rows!.Count);
        Assert.True(rows[0].DateCreated >= rows[1].DateCreated);
    }
}
