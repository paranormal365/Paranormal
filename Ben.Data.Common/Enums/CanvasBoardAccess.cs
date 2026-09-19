namespace Ben.Data.Common.Enums;

/// <summary>
/// What somebody may do with a case's canvas board.
/// </summary>
/// <remarks>
/// Ben, 2026-09-16: a reader gets "a read only version of the board"; somebody who may edit the case "can edit the
/// board additively"; and "only the author or organization admin or site admin or super admin can edit the board and
/// change existing pieces". Creating a board of their own is a separate answer, because it is about the case rather
/// than about this board.
/// </remarks>
public enum CanvasBoardAccess
{
    /// <summary>Read it, change nothing.</summary>
    Read = 0,

    /// <summary>Add pieces, and rework or remove the pieces they added. Everybody else's stay as they are.</summary>
    Append = 1,

    /// <summary>Change anything on the board: the person who wrote it, a group administrator, or a site administrator.</summary>
    Full = 2,
}
