namespace Ben.Data.Common.Interfaces;

/// <summary>
/// Abstracts binary file storage so the application can run against the local
/// filesystem in development and swap to Azure Blob Storage / S3 in production
/// without touching any other code.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Writes <paramref name="data"/> to storage at <paramref name="relativePath"/>.
    /// The path is relative to the configured root (e.g. "users/{userId}/file.mp3").
    /// </summary>
    Task WriteAsync(string relativePath, Stream data, CancellationToken ct = default);

    /// <summary>Opens a readable stream for the file at <paramref name="relativePath"/>.</summary>
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default);

    /// <summary>Deletes the file at <paramref name="relativePath"/>. No-op if the file does not exist.</summary>
    Task DeleteAsync(string relativePath, CancellationToken ct = default);

    /// <summary>Returns <c>true</c> if a file exists at <paramref name="relativePath"/>.</summary>
    bool Exists(string relativePath);

    /// <summary>
    /// Builds the canonical relative storage path for a user-owned file.
    /// e.g. "users/{userId}/{storedFileName}"
    /// </summary>
    string UserFilePath(Guid userId, string storedFileName);

    /// <summary>
    /// Builds the canonical relative storage path for an organization-owned file.
    /// e.g. "orgs/{orgId}/{storedFileName}"
    /// </summary>
    string OrgFilePath(Guid orgId, string storedFileName);

    /// <summary>
    /// Builds the canonical relative storage path for a case-scoped file.
    /// e.g. "cases/{caseId}/{storedFileName}"
    /// </summary>
    string CaseFilePath(Guid caseId, string storedFileName);
}
