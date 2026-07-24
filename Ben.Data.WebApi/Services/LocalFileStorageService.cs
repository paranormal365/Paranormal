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
               .Replace('\\', '/');   // always use forward slashes in stored paths

    public async Task WriteAsync(string relativePath, Stream data, CancellationToken ct = default)
    {
        var fullPath = FullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write,
                                            FileShare.None, bufferSize: 81920, useAsync: true);
        await data.CopyToAsync(fs, ct);
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = FullPath(relativePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Stored file not found: {relativePath}", fullPath);

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
                                       FileShare.Read, bufferSize: 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = FullPath(relativePath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public bool Exists(string relativePath) => File.Exists(FullPath(relativePath));

    private string FullPath(string relativePath)
        => Path.Combine(_rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
