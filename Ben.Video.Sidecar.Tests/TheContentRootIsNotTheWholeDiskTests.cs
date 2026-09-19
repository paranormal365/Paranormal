using Xunit;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// The sidecar states where its content root is instead of inheriting the working directory.
/// </summary>
/// <remarks>
/// <para><b>What happened.</b> The installed service pinned a CPU core for as long as it was up and
/// grew to 2.9 GB. Measured 2026-09-19 after three hours fifty-nine minutes: 97% of a core, one
/// thread — the .NET File Watcher — taking 1,601 of 3,665 samples inside stat() and lstat(), the
/// heap busy enough that a /v1/health request answering a small object took 340ms. That is the call
/// the editor polls to decide whether native acceleration is available.</para>
///
/// <para><b>Why.</b> <c>WebApplication.CreateBuilder(args)</c> takes the content root from the
/// CURRENT DIRECTORY when it is not told otherwise, and the default configuration then watches
/// appsettings.json inside it with reloadOnChange — a recursive PhysicalFileProvider watch on that
/// directory. Installed, this runs from a LaunchAgent, and launchd starts a process with its
/// working directory set to "/". So the watch covered the entire filesystem: every change anywhere
/// on the machine arrived as an FSEvent and was stat()ed. Nothing in this repository ever wrote
/// the words FileSystemWatcher.</para>
///
/// <para><b>Why a source scan.</b> The fault is in how the host is CONSTRUCTED, before any seam a
/// test can reach — by the time a WebApplicationFactory has built the app it has supplied its own
/// content root and the bug is invisible. What can be checked is that the production entry point
/// still says where its content root is, and that the installer still tells launchd where to start
/// it, which between them are the whole of the fix.</para>
/// </remarks>
public sealed class TheContentRootIsNotTheWholeDiskTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }

    [Fact]
    public void Program_states_its_content_root_rather_than_inheriting_the_working_directory()
    {
        var program = File.ReadAllText(RepoFile("Ben.Video.Sidecar", "Program.cs"));

        Assert.Contains("ContentRootPath", program);
        Assert.Contains("AppContext.BaseDirectory", program);

        // The bare overload is the one that inherits the current directory. Using it again would
        // put the watch back over the whole disk.
        Assert.DoesNotContain("WebApplication.CreateBuilder(args)", program);
    }

    [Fact]
    public void The_installer_tells_launchd_where_to_start_it()
    {
        var install = File.ReadAllText(RepoFile("Ben.Video.Sidecar", "installer", "macos", "install.sh"));

        Assert.Contains("WorkingDirectory", install);
        // Not "/" — which is what launchd uses when the key is absent, and the whole of the fault.
        Assert.DoesNotContain("<key>WorkingDirectory</key>   <string>/</string>", install);
    }
}
