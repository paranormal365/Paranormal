namespace Ben.Service.Models.Entities;

/// <summary>
/// A request parked for an account holder to claim — what the adopt page shows before they say
/// whether it is theirs (site evaluation 2026-09-06, phase 1).
/// </summary>
/// <remarks>
/// Only what the holder needs to recognise it: the name given, the address, and the groups it
/// was meant for. The description is not shown until adoption — a stranger's story is not the
/// holder's to read if it turns out not to be theirs.
/// </remarks>
public sealed record PendingClientRequestRecord(
    Guid Id,
    string DisplayName,
    string StreetAddress1,
    string? StreetAddress2,
    string City,
    string State,
    string ZipCode,
    IReadOnlyList<string> OrganizationNames,
    DateTime DateCreated);
