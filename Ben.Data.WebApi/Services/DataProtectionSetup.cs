using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Gives this process a key ring that outlives it.
/// </summary>
/// <remarks>
/// <para><b>The bug this exists to end.</b> Nothing called <c>AddDataProtection()</c>, so ASP.NET
/// generated a key ring in memory and threw it away on shutdown. <c>AddIdentityApiEndpoints</c>
/// protects its bearer tokens with Data Protection, which meant <i>every restart of this API
/// invalidated every access and every refresh token that had ever been issued</i> — a deploy, a
/// rebuild, an app-pool recycle, all the same.</para>
///
/// <para><b>And it failed silently.</b> The Blazor circuit still reported <c>IsAuthenticated</c>
/// from its stored claims, so nothing redirected anybody to <c>/login</c>: people sat in a zombie
/// session where every API call 401'd and the site still said they were signed in. Refresh could
/// not rescue it either — the refresh token died with the same key ring. On 2026-08-27 this
/// presented as "my profile page doesn't seem to be working"; the page was fine, twelve Playwright
/// tests were green, and signing in again fixed it.</para>
///
/// <para><b>Where the keys go.</b> <c>DataProtection:KeyRingPath</c> when set. Otherwise a
/// <c>data-protection-keys</c> folder beside <c>FileStorage:RootPath</c> — deliberately, because
/// that path is already required to survive a deploy and to be backed up, so a key ring there
/// inherits both properties instead of needing somebody to remember them separately. A key ring
/// under the content root would be wiped by the next publish, which is the same bug with more
/// steps.</para>
///
/// <para><b>With neither configured it stays ephemeral — and says so, loudly.</b> Refusing to
/// start would take down a dev run over something that only matters across restarts. A warning
/// naming the exact symptom is the honest middle: the original failure cost an afternoon precisely
/// because nothing anywhere mentioned it.</para>
/// </remarks>
public static class DataProtectionSetup
{
    /// <summary>
    /// The application name stamped into the key ring.
    /// </summary>
    /// <remarks>
    /// Pinned rather than defaulted to the assembly name, so renaming the project — or running the
    /// same key ring from a differently-named host — does not read as a different application and
    /// silently sign everybody out. Two apps sharing one directory stay isolated by this value.
    /// </remarks>
    public const string ApplicationName = "IsHaunted.Api";

    /// <summary>Folder name used when the path is derived from <c>FileStorage:RootPath</c>.</summary>
    public const string KeyRingFolderName = "data-protection-keys";

    /// <summary>
    /// Works out where the key ring belongs, or null when nothing is configured to put it.
    /// </summary>
    /// <remarks>
    /// Separated from the registration so the decision can be tested without standing up a host.
    /// Whitespace counts as unset: <c>"RootPath": ""</c> is the tracked default in appsettings, and
    /// treating it as a real path would put the key ring at the process working directory.
    /// </remarks>
    public static string? ResolveKeyRingPath(IConfiguration configuration)
    {
        var explicitPath = configuration["DataProtection:KeyRingPath"];
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return Path.GetFullPath(explicitPath.Trim());

        var fileRoot = configuration["FileStorage:RootPath"];
        if (!string.IsNullOrWhiteSpace(fileRoot))
            return Path.GetFullPath(Path.Combine(fileRoot.Trim(), KeyRingFolderName));

        return null;
    }

    /// <summary>
    /// Registers Data Protection with a persisted key ring, when one can be located.
    /// </summary>
    public static IServiceCollection AddBenDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        Serilog.ILogger log)
    {
        var keyRingPath = ResolveKeyRingPath(configuration);

        if (keyRingPath is null)
        {
            log.Warning(
                "Data Protection keys are NOT being persisted — neither DataProtection:KeyRingPath nor "
              + "FileStorage:RootPath is set. The key ring is regenerated on every start, so restarting "
              + "this API signs out every user AND invalidates their refresh tokens. The site will still "
              + "show them as signed in while every API call answers 401.");
            return services;
        }

        Directory.CreateDirectory(keyRingPath);

        var builder = services
            .AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));

        var protection = ApplyKeyEncryption(builder, configuration, keyRingPath, log);

        log.Information(
            "Data Protection keys persist to {KeyRingPath} (encryption at rest: {Protection}).",
            keyRingPath, protection);

        return services;
    }

    /// <summary>
    /// Encrypts the key ring at rest, and returns what it settled on.
    /// </summary>
    /// <remarks>
    /// <para><b>Why "auto" is not "certificate".</b> A TLS certificate is the obvious thing to reach
    /// for and the one with a trap in it: certificates rotate. When the renewed certificate replaces
    /// the old one in the store, a key ring encrypted with the old one cannot be read by anything,
    /// ever — which is today's mass sign-out made permanent and unrecoverable. Choosing it has to be
    /// deliberate, and the thumbprint has to be of a certificate somebody has decided to keep.</para>
    ///
    /// <para><b>Unencrypted is a real answer, not a failure.</b> On a developer machine the keys sit
    /// under a path only that account can read, and refusing to run without DPAPI would mean macOS
    /// and Linux could not persist keys at all.</para>
    /// </remarks>
    private static string ApplyKeyEncryption(
        IDataProtectionBuilder builder,
        IConfiguration configuration,
        string keyRingPath,
        Serilog.ILogger log)
    {
        var mode = (configuration["DataProtection:ProtectKeysWith"] ?? "auto").Trim().ToLowerInvariant();

        if (mode == "auto")
            mode = OperatingSystem.IsWindows() ? "dpapi" : "none";

        switch (mode)
        {
            case "dpapi":
                if (!OperatingSystem.IsWindows())
                {
                    log.Warning(
                        "DataProtection:ProtectKeysWith is 'dpapi' but this is not Windows. Keys in "
                      + "{KeyRingPath} will be written unencrypted.", keyRingPath);
                    return "none (dpapi unavailable)";
                }
                builder.ProtectKeysWithDpapi();
                return "dpapi";

            case "certificate":
                var thumbprint = configuration["DataProtection:CertificateThumbprint"];
                if (string.IsNullOrWhiteSpace(thumbprint))
                {
                    // Falling back to unencrypted is the safe half of this failure. Falling back to
                    // an *ephemeral* key ring would not be — that is the outage this class exists
                    // to prevent, and a missing thumbprint should not cause it.
                    log.Warning(
                        "DataProtection:ProtectKeysWith is 'certificate' but no "
                      + "DataProtection:CertificateThumbprint is set. Keys in {KeyRingPath} will be "
                      + "written unencrypted.", keyRingPath);
                    return "none (no thumbprint)";
                }

                var certificate = FindCertificate(thumbprint.Trim());
                if (certificate is null)
                {
                    log.Warning(
                        "No certificate with thumbprint {Thumbprint} was found in the machine or user "
                      + "store. Keys in {KeyRingPath} will be written unencrypted.",
                        thumbprint, keyRingPath);
                    return "none (certificate not found)";
                }

                builder.ProtectKeysWithCertificate(certificate);
                log.Warning(
                    "Data Protection keys are encrypted with certificate {Thumbprint}, valid until "
                  + "{NotAfter:yyyy-MM-dd}. Keep this certificate after it is replaced: a key ring "
                  + "encrypted with a certificate that no longer exists cannot be recovered, and "
                  + "everyone is signed out permanently rather than until they sign in again.",
                    thumbprint, certificate.NotAfter);
                return "certificate";

            case "none":
                return "none";

            default:
                log.Warning(
                    "DataProtection:ProtectKeysWith has an unrecognised value {Mode}. Expected auto, "
                  + "none, dpapi or certificate. Keys will be written unencrypted.", mode);
                return "none (unrecognised mode)";
        }
    }

    /// <summary>
    /// Looks a certificate up by thumbprint, machine store first.
    /// </summary>
    /// <remarks>
    /// Expired certificates are included on purpose. The key ring only needs the private key to
    /// decrypt what it already wrote, and refusing an expired certificate here would turn a routine
    /// renewal into the unrecoverable case the caller is warned about.
    /// </remarks>
    private static X509Certificate2? FindCertificate(string thumbprint)
    {
        foreach (var location in new[] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
        {
            using var store = new X509Store(StoreName.My, location);
            try
            {
                store.Open(OpenFlags.ReadOnly);
            }
            catch (Exception)
            {
                // A store that cannot be opened is not a store that lacks the certificate — try the
                // next one rather than reporting "not found" for a permissions problem.
                continue;
            }

            var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
            if (found.Count > 0 && found[0].HasPrivateKey)
                return found[0];
        }

        return null;
    }
}
