using System.Net;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Composes the HTML bodies of platform notifications, encoding anything a person typed.
/// </summary>
/// <remarks>
/// <para><see cref="UserMessage.MessageBody"/> is rendered as markup — deliberately, because the
/// notices the platform writes use <c>&lt;strong&gt;</c> to pick out the name of a group or a piece
/// of equipment, and readers see formatting rather than tags.</para>
///
/// <para>That makes any user-supplied fragment interpolated into one an injection vector. Display
/// names, equipment names, decline reasons and condition notes are all typed by somebody, and all
/// of them reach these bodies. Encoding at composition — rather than at render, which would show
/// literal tags for every legitimately-formatted notice — keeps both properties.</para>
/// </remarks>
internal static class NotificationText
{
    /// <summary>
    /// Encodes a fragment somebody typed, for safe interpolation into a notification body.
    /// </summary>
    /// <remarks>
    /// Use for every value that originates from a user: display names, item names, free-text notes.
    /// Values the server itself produced — dates, statuses, fixed sentences — do not need it, and
    /// wrapping them would be noise.
    /// </remarks>
    internal static string Safe(string? userSuppliedText) => WebUtility.HtmlEncode(userSuppliedText ?? string.Empty);
}
