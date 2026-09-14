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
            ReplaceWith(partial, fullPath);
        }
        catch
        {
            File.Delete(partial);
            throw;
        }
    }

    /// <summary>Puts a finished partial file in place of the target, in one step, even while the target is being read.</summary>
    /// <remarks>
    /// <para><b>Not <c>File.Move(overwrite: true)</c>, which fails on Windows and Windows is production.</b> It maps to
    /// <c>MoveFileEx</c> with <c>REPLACE_EXISTING</c>, and that refuses to replace a file any handle has open - even one
    /// opened with <c>FileShare.Delete</c>, which <see cref="OpenReadAsync"/> grants precisely so that a replace could
    /// happen. Measured on the production box, 2026-09-14, against a reader holding exactly that sharing:
    /// <c>File.Move</c> overwrite threw <c>UnauthorizedAccessException</c>; <c>File.Replace</c> succeeded and the open
    /// reader went on reading the old bytes; delete-then-move also succeeded but leaves a moment where the file is
    /// not there at all, which is the half-state this method exists to prevent. macOS allows the move, so a
    /// Mac-only run of <c>A_file_open_for_reading_can_still_be_replaced</c> never saw it.</para>
    ///
    /// <para><c>File.Replace</c> needs a target to replace, so a first write moves instead. If another writer creates
    /// the target in between, the move finds it there and the replace is the right answer after all.</para>
    /// </remarks>
    private static void ReplaceWith(string partial, string fullPath)
    {
        if (File.Exists(fullPath))
        {
            File.Replace(partial, fullPath, destinationBackupFileName: null);
            return;
        }

        try
        {
            File.Move(partial, fullPath);
        }
        catch (IOException) when (File.Exists(fullPath))
        {
            File.Replace(partial, fullPath, destinationBackupFileName: null);
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
