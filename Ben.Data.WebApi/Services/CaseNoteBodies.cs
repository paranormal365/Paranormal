using Ben.Data.Common.Text;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// What a case note's body is: sanitized HTML.
/// </summary>
/// <remarks>
/// <para>
/// Case notes were plain text in a textarea, drawn with <c>white-space: pre-wrap</c>. Beta feedback (2026-09-14)
/// gave them a formatting editor, so the website now draws a note as markup. One rule for what a stored body is
/// keeps every reader simple: it is HTML, and it has been through the sanitizer.
/// </para>
/// <para>
/// A body that was written as plain text — every note before the change, and anything an API caller sends without
/// tags — becomes paragraphs and line breaks that read the same; a body with tags is sanitized. The same function is
/// used when a note is saved, when notes are read (so a row the one-time conversion has not reached yet is still
/// drawn safely), and by <see cref="CaseNoteBodyHtmlBackfillService"/> to convert the stored rows once.
/// </para>
/// </remarks>
public static class CaseNoteBodies
{
    public static string ToHtml(string? body, ICmsMarkupSanitizer sanitizer)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        return PlainTextHtml.LooksLikeHtml(body)
            ? sanitizer.SanitizeHtml(body).Trim()
            : PlainTextHtml.FromPlainText(body);
    }
}
