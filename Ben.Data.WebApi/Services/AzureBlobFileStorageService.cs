using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Stores evidence in an Azure Blob container instead of on a local disk.
/// </summary>
/// <remarks>
/// <para><b>Written and deliberately NOT registered</b> (Ben, 2026-08-31: "create the blob
/// implementation and just sideline it for now… I would like to prove this proof of concept
/// before having to worry about paying monthly myself"). Nothing constructs this class. Turning it
/// on is one line in <c>Program.cs</c> and a connection string, and until somebody writes that
/// line the application behaves exactly as it does today.</para>
///
/// <para><b>Why it is a drop-in at all.</b> The database stores RELATIVE paths
/// (<c>orgs/{guid}/{file}</c>), never absolute ones, and every caller goes through
/// <see cref="IFileStorageService"/>. So the same rows resolve against a disk or a container
/// without a migration — the property worth protecting whenever this interface is edited.</para>
///
/// <para><b>The one honest wart: <see cref="Exists"/> is synchronous.</b> The interface predates
/// this class and blocks on a network call here, which on a local disk was a stat. It is called
/// on housekeeping paths rather than in a request loop, so it is tolerable — but if this is ever
/// switched on for real, the interface wants an async overload before anything hot starts using
/// it.</para>
///
/// <para><b>Cost note, since it is the reason this is sidelined.</b> Storage is the small half:
/// roughly two cents per GB per month. EGRESS is the half that scales with success — every
/// archive photograph a visitor opens is metered leaving Azure, where the same byte served from
/// Ben's own machine costs nothing extra. A site whose pitch is "come and look at everybody's
/// evidence at this location" should price that deliberately, not discover it.</para>
/// </remarks>
public sealed class AzureBlobFileStorageService : IFileStorageService
{
    private readonly BlobContainerClient _container;

    /// <summary>
    /// Reads <c>FileStorage:Blob:ConnectionString</c> and <c>FileStorage:Blob:Container</c>.
    /// </summary>
    /// <remarks>
    /// Fails loudly and immediately when unconfigured, exactly as the local implementation does
    /// for a missing root path. A storage service that constructs successfully and then cannot
    /// store anything turns a deployment mistake into a run-time mystery.
    /// </remarks>
    public AzureBlobFileStorageService(IConfiguration configuration)
    {
        var connection = configuration["FileStorage:Blob:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException(
                "FileStorage:Blob:ConnectionString is not configured. "
              + "Set it, or leave LocalFileStorageService registered.");

        var container = configuration["FileStorage:Blob:Container"];
        if (string.IsNullOrWhiteSpace(container)) container = "evidence";

        _container = new BlobContainerClient(connection, container);

        // PRIVATE, and the default matters: a container created public would expose every piece
        // of client evidence to anybody who guessed a URL, and the whole media pipeline here
        // exists to make sure bytes are only served through a checked endpoint.
        _container.CreateIfNotExists(PublicAccessType.None);
    }

    // ── Paths: identical to the local implementation, on purpose ─────────────
    // These are the strings already written into the database, so they cannot differ between
    // implementations without orphaning every existing row.

    public string UserFilePath(Guid userId, string storedFileName)
        => $"users/{userId}/{storedFileName}";

    public string OrgFilePath(Guid orgId, string storedFileName)
        => $"orgs/{orgId}/{storedFileName}";

    public string CaseFilePath(Guid caseId, string storedFileName)
        => $"cases/{caseId}/{storedFileName}";

    // ── Reading and writing ──────────────────────────────────────────────────

    public async Task WriteAsync(string relativePath, Stream data, CancellationToken ct = default)
        => await _container.GetBlobClient(Normalise(relativePath))
            .UploadAsync(data, overwrite: true, ct);

    /// <summary>Opens the blob's contents.</summary>
    /// <remarks>
    /// Downloads to a seekable MemoryStream rather than handing back the network stream. Several
    /// callers seek — image processing and range requests among them — and a forward-only network
    /// stream fails there in a way that looks like a corrupt file rather than a wrong stream type.
    /// The cost is holding one file in memory; the uploads this serves are already bounded by the
    /// configured upload limits.
    /// </remarks>
    public async Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default)
    {
        var buffer = new MemoryStream();
        await _container.GetBlobClient(Normalise(relativePath)).DownloadToAsync(buffer, ct);
        buffer.Position = 0;
        return buffer;
    }

    /// <summary>Deletes the blob. Absent is success, matching the local implementation.</summary>
    public async Task DeleteAsync(string relativePath, CancellationToken ct = default)
        => await _container.GetBlobClient(Normalise(relativePath))
            .DeleteIfExistsAsync(cancellationToken: ct);

    /// <summary>Whether the blob is there.</summary>
    /// <remarks>
    /// Synchronous because the interface is, which means this blocks on a network round trip.
    /// See the class remarks: acceptable on the housekeeping paths that call it, and the first
    /// thing to revisit if this is ever switched on.
    /// </remarks>
    public async Task DeleteDirectoryAsync(string relativeDirectory, CancellationToken ct = default)
    {
        var prefix = Normalise(relativeDirectory).Trim('/');
        if (prefix.Length == 0)
            throw new ArgumentException("A directory to delete must be named; the container is never it.", nameof(relativeDirectory));

        // Blob storage has no folders, only names with slashes in them: a "directory" is every
        // blob whose name starts with the prefix. The trailing slash keeps "orgs/ab" from
        // matching "orgs/abc…".
        await foreach (var blob in _container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix + "/", ct))
            await _container.GetBlobClient(blob.Name).DeleteIfExistsAsync(cancellationToken: ct);
    }

    public bool Exists(string relativePath)
        => _container.GetBlobClient(Normalise(relativePath)).Exists().Value;

    /// <summary>
    /// The files directly inside a prefix, as paths relative to the container root.
    /// </summary>
    /// <remarks>
    /// <para>A blob container has no directories — only names that happen to contain slashes — so
    /// "directly inside" is expressed with a delimiter, which is what stops this returning an
    /// entire subtree when the caller asked for one level.</para>
    ///
    /// <para>An absent prefix yields an empty list rather than an error, matching the local
    /// implementation: the housekeeping sweep this exists for must not throw because a directory
    /// nobody has written to yet does not exist.</para>
    /// </remarks>
    public IReadOnlyList<string> ListFiles(string relativeDirectory)
    {
        var prefix = Normalise(relativeDirectory);
        if (prefix.Length > 0 && !prefix.EndsWith('/')) prefix += "/";

        var names = new List<string>();
        try
        {
            foreach (var item in _container.GetBlobsByHierarchy(
                         BlobTraits.None, BlobStates.None, delimiter: "/", prefix: prefix))
            {
                // Skip the "folders" the delimiter synthesises — the caller asked for files.
                if (item.IsBlob) names.Add(item.Blob.Name);
            }
        }
        catch (RequestFailedException)
        {
            // Nothing there, or the container has just been created. An empty list is the honest
            // answer and the one the local implementation gives.
        }

        return names;
    }

    /// <summary>
    /// Blob names use forward slashes and never a leading one.
    /// </summary>
    /// <remarks>
    /// Paths reach this class having been built on Windows in places, so a stray backslash is a
    /// real possibility — and it would create a blob whose name contains a literal backslash
    /// rather than the one the database expects, which reads as a missing file forever after.
    /// </remarks>
    private static string Normalise(string relativePath)
        => relativePath.Replace('\\', '/').TrimStart('/');
}
