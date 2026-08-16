using System.Runtime.InteropServices;

namespace Ben.Video.Sidecar.Security;

/// <summary>
/// A stable identifier for this installation, and the platform it was built for.
/// </summary>
/// <remarks>
/// <para>Generated once and kept in the config directory beside the pairing token, so it survives
/// restarts and upgrades-in-place. It exists so the site can tell "one machine paired five times"
/// apart from "five machines paired once", which is the whole question when deciding whether an
/// old build is still in use.</para>
///
/// <para>It identifies an <i>installation</i>, not a person: it is generated locally, is not
/// derived from anything about the machine or its owner, and carries no meaning off this box until
/// a signed-in browser pairs and the server records the two together.</para>
/// </remarks>
public sealed class InstallIdentity
{
    private readonly string _filePath;

    public InstallIdentity(string configDir)
    {
        _filePath = Path.Combine(configDir, "install-id");
        Value = LoadOrCreate();
    }

    /// <summary>The stable id for this installation.</summary>
    public Guid Value { get; }

    /// <summary>Runtime identifier this build targets, e.g. "osx-arm64".</summary>
    public static string Platform => RuntimeInformation.RuntimeIdentifier;

    private Guid LoadOrCreate()
    {
        try
        {
            if (File.Exists(_filePath) &&
                Guid.TryParse(File.ReadAllText(_filePath).Trim(), out var existing))
            {
                return existing;
            }

            var created = Guid.NewGuid();
            File.WriteAllText(_filePath, created.ToString());
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(_filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return created;
        }
        catch
        {
            // An unwritable config directory must not stop the sidecar serving. A per-run id still
            // identifies this process to anything asking; it just won't correlate across restarts,
            // which is a reporting gap rather than a failure.
            return Guid.NewGuid();
        }
    }
}
