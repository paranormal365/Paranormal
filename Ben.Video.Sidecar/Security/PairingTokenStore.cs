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

    // ── Short-code pairing ────────────────────────────────────────────────────
    // The long token above is the credential; nobody should have to type 43 characters of it.
    // Pairing instead shows the user a single 6-digit code (e.g. 483920) on a sidecar-served page,
    // and the editor exchanges that code for the long token via POST /v1/pair. The code is NOT the
    // credential: one million combinations only survives because a code is short-lived
    // (10 minutes), single-use, and exchange attempts ride the same failure throttle as bad
    // tokens. The exchange can hand out the long token because the token file already stores it in
    // plaintext — "hash-only in memory" was always about not re-DISPLAYING it, and handing it to a
    // correctly-paired browser is precisely its purpose. No rotation involved, so browsers that
    // paired earlier stay paired — pairing a second browser no longer breaks the first.

    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    private readonly object _codeLock = new();
    private byte[]? _codeHash;
    private DateTimeOffset _codeExpiresUtc;

    /// <summary>A 6-digit pairing code is currently valid and unused.</summary>
    public bool HasActiveCode
    {
        get { lock (_codeLock) return _codeHash is not null && DateTimeOffset.UtcNow < _codeExpiresUtc; }
    }

    /// <summary>When the active code stops working — the /pair page shows this to the user.</summary>
    public DateTimeOffset CodeExpiresUtc
    {
        get { lock (_codeLock) return _codeExpiresUtc; }
    }

    /// <summary>
    /// Starts (or restarts) a pairing window: mints a fresh 6-digit code and returns it for
    /// display. Replaces any previous code — there is at most one active code at a time.
    /// </summary>
    public string BeginPairing()
    {
        // GetInt32 is the crypto RNG — no modulo bias, uniform over the full 000000–999999 range.
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        lock (_codeLock)
        {
            _codeHash = Hash(code);
            _codeExpiresUtc = DateTimeOffset.UtcNow + CodeLifetime;
        }
        return code;
    }

    /// <summary>
    /// Exchanges a presented 6-digit code for the long pairing token. Returns null when the code
    /// is wrong, expired, or already used. Success consumes the code — a second exchange needs a
    /// fresh one, so a code observed over someone's shoulder after use is worthless.
    /// </summary>
    public string? TryExchangeCode(string? presented)
    {
        if (string.IsNullOrEmpty(presented)) return null;

        lock (_codeLock)
        {
            if (_codeHash is null || DateTimeOffset.UtcNow >= _codeExpiresUtc) return null;
            if (!CryptographicOperations.FixedTimeEquals(Hash(presented), _codeHash)) return null;

            _codeHash = null; // single-use
        }

        // The token file is the plaintext source of truth (mode 600); reading it here instead of
        // caching plaintext in this object keeps the long-standing in-memory posture unchanged.
        return File.Exists(_tokenFilePath) ? File.ReadAllText(_tokenFilePath).Trim() : null;
    }

    private static byte[] Hash(string value) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
}
