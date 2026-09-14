using Ben.Data.Common.Interfaces;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Stores files on the local filesystem under a configured root path.
/// Register as a singleton — the root path is read from configuration once.
/// </summary>
public sealed class LocalFileStorageService : IFileStorageService
{
    private readonly string _rootPath;

    public LocalFileStorageService(IConfiguration configuration)
    {
        var rootPath = configuration["FileStorage:RootPath"];
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new InvalidOperationException(
                "FileStorage:RootPath is not configured. " +
                "Add it to appsettings.json or appsettings.Development.json.");

        _rootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(_rootPath);
    }

    public string UserFilePath(Guid userId, string storedFileName)
        => Path.Combine("users", userId.ToString(), storedFileName)
               .Replace('\\', '/');

    public string OrgFilePath(Guid orgId, string storedFileName)
        => Path.Combine("orgs", orgId.ToString(), storedFileName)
               .Replace('\\', '/');

    public string CaseFilePath(Guid caseId, string storedFileName)
        => Path.Combine("cases", caseId.ToString(), storedFileName)
               .Replace('\\', '/');

    /// <summary>Writes a file, so that it is either all there or not there at all.</summary>
    /// <remarks>
    /// <para>The bytes go to a partial file beside the target, which is renamed onto it only once the copy has finished.
    /// Writing straight to the target used to open it with <c>FileMode.Create</c>, which empties it before a byte is
    /// copied: a request cancelled mid-write left a zero-byte file, and a reader arriving during the write read one.
    /// The thumbnail cache treats an existing file as a finished thumbnail, so two pictures in the media library were
    /// served as empty images for good after a person moved to another page while they were being made
    /// (found walking the site, 2026-09-13).</para>
    ///
    /// <para>A rename within one directory replaces the target in a single step, so a reader sees the old file or
    /// the new one and never half of either. A failed or cancelled write removes its partial file.</para>
    /// </remarks>
    public async Task WriteAsync(string relativePath, Stream data, CancellationToken ct = default)
    {
        var fullPath = FullPath(relativePath);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        var partial = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}{PartialSuffix}");
        try
        {
            await using (var fs = new FileStream(partial, FileMode.CreateNew, FileAccess.Write,
                                                 FileShare.None, bufferSize: 81920, useAsync: true))
            {
                await data.CopyToAsync(fs, ct);
            }
            File.Move(partial, fullPath, overwrite: true);
        }
        catch
        {
            File.Delete(partial);
            throw;
        }
    }

    /// <summary>The ending of a write still in progress; never listed, and never a stored file's name.</summary>
    private const string PartialSuffix = ".partial";

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = FullPath(relativePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Stored file not found: {relativePath}", fullPath);

        // Delete sharing as well as read: a newer copy being renamed onto this file while it is open is how a write
        // replaces a file, and on Windows the rename is refused unless every reader allows it.
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
                                       FileShare.Read | FileShare.Delete, bufferSize: 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = FullPath(relativePath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public Task DeleteDirectoryAsync(string relativeDirectory, CancellationToken ct = default)
    {
        var trimmed = relativeDirectory.Trim().Trim('/', '\\');
        if (trimmed.Length == 0)
            throw new ArgumentException("A directory to delete must be named; the root is never it.", nameof(relativeDirectory));

        var fullDir = Path.GetFullPath(FullPath(trimmed));
        // The full path must sit strictly inside the root: "orgs/../.." resolves outside it.
        if (!fullDir.StartsWith(_rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("The directory is outside the storage root.", nameof(relativeDirectory));

        if (Directory.Exists(fullDir)) Directory.Delete(fullDir, recursive: true);
        return Task.CompletedTask;
    }

    public bool Exists(string relativePath) => File.Exists(FullPath(relativePath));

    public IReadOnlyList<string> ListFiles(string relativeDirectory)
    {
        var fullDir = FullPath(relativeDirectory);
        if (!Directory.Exists(fullDir)) return [];

        var trimmed = relativeDirectory.TrimEnd('/');
        return Directory.EnumerateFiles(fullDir)
            .Where(f => !Path.GetFileName(f).EndsWith(PartialSuffix, StringComparison.Ordinal))
            .Select(f => $"{trimmed}/{Path.GetFileName(f)}")
            .ToList();
    }

    private string FullPath(string relativePath)
        => Path.Combine(_rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
