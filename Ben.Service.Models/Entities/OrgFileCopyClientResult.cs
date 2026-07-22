namespace Ben.Service.Models.Entities;

/// <summary>
/// Client-side result of a copy-from-user operation.
/// Mirrors the API's OrgFileCopyResult but lives in the shared models layer.
/// </summary>
public record OrgFileCopyClientResult(
    OrganizationFileRecord File,
    bool CanPublishImmediately,
    bool PublishedImmediately);
