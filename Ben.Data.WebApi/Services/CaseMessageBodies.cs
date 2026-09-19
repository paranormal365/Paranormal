using Ben.Data.Common.Text;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// The two forms a message between a group and its client is kept in.
/// </summary>
/// <remarks>
/// <para>
/// Beta feedback, 2026-09-14: the message box on a case became a small formatting editor. The iPhone app in App
/// Review draws <c>Body</c> as plain text in a bubble, and cannot be changed until its next build — the same reason
/// <c>CaseMessageBodiesArePlainTextTests</c> exists. So nothing about <c>Body</c> changes: it stays plain text,
/// always. The formatted copy is added beside it as <c>BodyHtml</c>, sanitized, and only the website reads it.
/// </para>
/// <para>
/// A message sent with only <c>Body</c> — the app, an older website, anything that predates this — is stored exactly as
/// before, with no <c>BodyHtml</c>. A message sent with <c>BodyHtml</c> has its <c>Body</c> written from it, so the app
/// shows the same words, laid out as plain text.
/// </para>
/// </remarks>
public static class CaseMessageBodies
{
    /// <summary>
    /// The stored pair for a posted message, or null when neither form holds any words.
    /// </summary>
    public static (string Body, string? BodyHtml)? Normalise(string? body, string? bodyHtml, ICmsMarkupSanitizer sanitizer)
    {
        if (PlainTextHtml.HasText(bodyHtml))
        {
            var clean = sanitizer.SanitizeHtml(bodyHtml).Trim();
            var plain = PlainTextHtml.ToText(clean);
            if (plain.Length > 0) return (plain, clean);
        }

        return string.IsNullOrWhiteSpace(body) ? null : (body.Trim(), null);
    }
}
