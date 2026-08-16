using System.Security.Cryptography;

namespace Ben.Video.Sidecar.Security;

/// <summary>
/// Owns the sidecar's pairing token — the one piece of shared secret between this process and the
/// browser editor, required on every mutating/reachable request except <c>GET /v1/health</c> (item
/// #38 phase E threat T1: an arbitrary web page in another tab must not be able to talk to the
/// sidecar even if it somehow gets the Origin check to pass). Generated once on first run, shown
/// to the user exactly once, then persisted — the user pastes it into the editor's pairing panel
/// a single time; the browser remembers it in localStorage from then on.
/// </summary>
public sealed class PairingTokenStore
{
    private const int TokenBytes = 32;
    private readonly string _tokenFilePath;
    private byte[] _tokenHash = [];

    public PairingTokenStore(string configDir)
    {
        _tokenFilePath = Path.Combine(configDir, "pairing-token");
    }

    /// <summary>True the first time this store loads (no token file existed yet) — the caller
    /// uses this to decide whether to print the plaintext token to the console.</summary>
    public bool WasJustCreated { get; private set; }

    /// <summary>The plaintext token, only ever populated on first creation — never re-read from
    /// disk in plaintext. Null after the process has loaded an existing token on a later run,
    /// by design: the token is for the user to paste once, not for this process to display again.</summary>
    public string? PlaintextOnFirstRun { get; private set; }

    /// <summary>Loads the existing token, or generates and persists a new one if none exists yet.</summary>
    public void LoadOrCreate()
    {
        if (File.Exists(_tokenFilePath))
        {
            var existing = File.ReadAllText(_tokenFilePath).Trim();
            _tokenHash = Hash(existing);
            WasJustCreated = false;
            return;
        }

        Generate();
    }

    /// <summary>Generates a brand-new token, overwriting any existing one — every previously
    /// paired browser will need to re-pair. Used at first run and by <c>--reset-token</c>.</summary>
    public void Generate()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        File.WriteAllText(_tokenFilePath, token);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_tokenFilePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        _tokenHash = Hash(token);
        PlaintextOnFirstRun = token;
        WasJustCreated = true;
    }

    /// <summary>Constant-time comparison against the stored token — never a plain string
    /// equality, which would leak timing information about how many leading characters matched.</summary>
    public bool Matches(string? presented)
    {
        if (string.IsNullOrEmpty(presented)) return false;
        return CryptographicOperations.FixedTimeEquals(Hash(presented), _tokenHash);
    }

    private static byte[] Hash(string value) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
}
