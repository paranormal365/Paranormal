using Microsoft.Data.SqlClient;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// On a development machine, an API pointed at a database of its own writes its files beside that
/// database too — never into the shared <c>.uploads</c> folder (crawl W14, 2026-09-22).
/// </summary>
/// <remarks>
/// <para><b>What went wrong.</b> An upload row holds a path; the bytes live under
/// <c>FileStorage:RootPath</c>. <c>run-e2e.sh</c> keeps each test database with a folder named after
/// it (<c>.uploads-&lt;database&gt;</c>), so the two can never drift apart. But an API started by
/// hand with only its connection pointed elsewhere kept the development default — the shared
/// <c>.uploads</c> — and wrote a scratch database's photos there. The next run against that database,
/// correctly paired, found rows with no bytes, and the sweep photographed every one of them as a
/// broken image on a real screen: a confident finding about the product that was about the
/// machine. The filed cause ("a directory per run") was wrong; the files were found in
/// <c>.uploads</c> on 2026-09-23.</para>
///
/// <para><b>What this does.</b> Development only: when the database is not the default one and the
/// root is still the shared <c>.uploads</c>, the root becomes <c>.uploads-&lt;database&gt;</c> beside
/// it — the same folder the harness would have given it. A root set to anything else is somebody's
/// choice and is left alone.</para>
/// </remarks>
public static class DevUploadsPairing
{
    /// <summary>The database the shared development folder belongs to.</summary>
    public const string DefaultDatabase = "IsHauntedDb";

    /// <summary>
    /// The folder this database's files belong in, or null when the configured one is right.
    /// </summary>
    public static string? PairedRoot(string? connectionString, string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(rootPath)) return null;

        string database;
        try { database = new SqlConnectionStringBuilder(connectionString).InitialCatalog; }
        catch (ArgumentException) { return null; }

        if (string.IsNullOrWhiteSpace(database)
            || database.Equals(DefaultDatabase, StringComparison.OrdinalIgnoreCase)) return null;

        var trimmed = rootPath.TrimEnd('/', '\\');
        if (!Path.GetFileName(trimmed).Equals(".uploads", StringComparison.Ordinal)) return null;

        return Path.Combine(Path.GetDirectoryName(trimmed) ?? "", ".uploads-" + database);
    }

    /// <summary>Applies <see cref="PairedRoot"/> to this host's configuration, and says so.</summary>
    public static void Apply(IConfiguration configuration, TextWriter log)
    {
        var paired = PairedRoot(configuration.GetConnectionString("BenDbConnectionString"),
                                configuration["FileStorage:RootPath"]);
        if (paired is null) return;

        configuration["FileStorage:RootPath"] = paired;
        log.WriteLine($"[DevUploadsPairing] This database keeps its files in {paired}, not the shared .uploads (W14).");
    }
}
