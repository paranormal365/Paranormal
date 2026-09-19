namespace Ben.Service.Models.Entities;

/// <summary>
/// Opens a chunked upload: the file's facts up front, so the server can refuse an oversize or
/// wrong-extension file before a single byte of it is sent.
/// </summary>
public sealed record StartChunkedUploadRequest(
    string FileName,
    string? ContentType,
    long TotalBytes,
    Guid UploadFileTypeId,
    string? Description,
    bool IsPublic);

/// <summary>
/// One chunked upload in progress. <see cref="ChunkMaxBytes"/> is the size the client must cut
/// chunks to — told by the server so the ceiling lives in one place (a site setting) rather than
/// being compiled into every client.
/// </summary>
public sealed record ChunkedUploadSessionRecord(
    Guid Id,
    long TotalBytes,
    long BytesReceived,
    long ChunkMaxBytes,
    long MaxFileBytes,
    IReadOnlyList<int> ReceivedChunks);
