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

    /// <summary>
    /// Remove a whole directory and everything under it — a group's <c>orgs/{id}</c> or a case's
    /// <c>cases/{id}</c> once the rows that pointed into it are gone. Deleting file by file from the
    /// rows leaves behind whatever the rows never knew about, and an empty folder named for a
    /// group that no longer exists. Removing nothing is not an error; removing the root is refused.
    /// </summary>
    Task DeleteDirectoryAsync(string relativeDirectory, CancellationToken ct = default);

    /// <summary>Returns <c>true</c> if a file exists at <paramref name="relativePath"/>.</summary>
    bool Exists(string relativePath);

    /// <summary>
    /// Lists the files directly inside <paramref name="relativeDirectory"/>, as paths relative to
    /// the storage root. An absent directory is an empty list, not an error. Added for
    /// chunked-upload housekeeping — sweeping sessions that were started and then abandoned —
    /// which is impossible to do through an abstraction that can only address files it already
    /// knows the names of.
    /// </summary>
    IReadOnlyList<string> ListFiles(string relativeDirectory);

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

    /// <summary>
    /// Builds the canonical relative storage path for a file a place keeps — a room's LiDAR scan,
    /// which belongs to the building rather than to whoever made it.
    /// e.g. "places/{placeId}/{storedFileName}"
    /// </summary>
    /// <remarks>
    /// Has a body so the test doubles that stand in for storage need not each learn it; both real
    /// backends spell it out beside the other three.
    /// </remarks>
    string PlaceFilePath(Guid placeId, string storedFileName) => $"places/{placeId}/{storedFileName}";
}
