namespace Ben.Service.Models.Entities;

/// <summary>
/// The photo editor's saved work on one file - its marks, its adjustments and how it was turned -
/// as <c>GET /api/upload-files/{id}/edit-state</c> returns it. Null when nothing has been saved.
/// </summary>
public sealed record ImageEditStateRecord(string? EditStateJson);
