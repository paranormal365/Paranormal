using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

/// <summary>
/// The <c>benvideo-sidecar:</c> scheme is spelled the same in all three places that matter.
/// </summary>
/// <remarks>
/// <para>The "on" half of the switch works only if three separate files agree on one string: the
/// editor builds the link (<see cref="SidecarSwitch.LaunchUri"/>), the Windows installer registers
/// the handler under HKCU, and the Store package declares it in its manifest. Two of those are not
/// C# and neither is checked by the compiler.</para>
///
/// <para>The failure is silent and lands on the user, not on us: the button is there, they press
/// it, the browser finds no handler, and nothing happens at all. Nothing else would catch it -
/// neither installer is built during a test run, and the packaged one cannot even be installed on
/// a developer's machine without an administrator.</para>
/// </remarks>
public sealed class ProtocolSchemeContractTests
{
    /// <summary>The scheme, without the colon, as the registry and the manifest both spell it.</summary>
    private static string Scheme => SidecarSwitch.LaunchUri.Split(':')[0];

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }

    [Fact]
    public void The_Store_package_declares_the_scheme_the_editor_opens()
    {
        var manifest = File.ReadAllText(RepoFile(
            "Ben.Video.Sidecar", "installer", "windows", "msix", "AppxManifest.template.xml"));

        Assert.Contains($"<uap:Protocol Name=\"{Scheme}\">", manifest);
    }

    [Fact]
    public void The_Windows_installer_registers_the_scheme_the_editor_opens()
    {
        var iss = File.ReadAllText(RepoFile(
            "Ben.Video.Sidecar", "installer", "windows", "BenVideoSidecar.iss"));

        // The key itself...
        Assert.Contains($@"Software\Classes\{Scheme}""", iss);
        // ...the marker without which Windows ignores the key entirely...
        Assert.Contains("\"URL Protocol\"", iss);
        // ...and something to actually run, with the URI handed to it.
        Assert.Contains($@"Software\Classes\{Scheme}\shell\open\command""", iss);
    }

    /// <summary>
    /// A scheme has to be a scheme: letters, digits, <c>+</c>, <c>-</c> and <c>.</c>, starting with
    /// a letter. An underscore would be quietly ignored by Windows and rejected by the browser.
    /// </summary>
    [Fact]
    public void The_scheme_is_one_a_browser_will_accept()
    {
        Assert.Matches("^[a-zA-Z][a-zA-Z0-9+.-]*$", Scheme);
        Assert.Contains(':', SidecarSwitch.LaunchUri);
    }
}
