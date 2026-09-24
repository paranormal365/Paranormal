using Ben.Data.Common;
using Ben.Data.Common.Mail;

namespace Ben.Data.WebApi.Services.Mail;

/// <summary>
/// Made-up rows, so a letter can be looked at without sending one (item 246).
/// </summary>
/// <remarks>
/// <para><b>Invented, never a real row.</b> The obvious implementation is to preview against the
/// most recent actual record, and it is the wrong one twice over: opening an editor would show
/// somebody's real name and address to whoever happened to open it, and it would make the preview
/// a read of another person's business that nothing recorded. Made-up values are also better at
/// the job — "Marguerite Ashdown" is instantly recognisable as not-real, so nobody mistakes a
/// preview for a sent letter.</para>
///
/// <para><b>Values are chosen to be long enough to be awkward.</b> A preview full of "test" tells
/// an author nothing about whether their layout survives a real name, and the first thing that
/// breaks an email layout is a longer word than the designer had in mind.</para>
/// </remarks>
public static class MailSampleRows
{
    /// <summary>The fixed moment a preview is written at: 20 September 2026, 9:05 in Nashville.</summary>
    /// <remarks>
    /// Fixed rather than "now" so a preview is the same picture every time it is opened, and so a
    /// screenshot of one stays true.
    /// </remarks>
    public static readonly DateTime WrittenAtUtc = new(2026, 9, 20, 14, 5, 0, DateTimeKind.Utc);

    /// <summary>Sample values for every column this kind of letter could read.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> For(
        IReadOnlyList<MailTemplateSchema.Table> tables)
    {
        var rows = new Dictionary<string, IReadOnlyDictionary<string, object?>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var table in tables)
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in table.Columns) row[column.Name] = Value(table.Name, column);
            rows[table.Name] = row;
        }

        return rows;
    }

    /// <summary>
    /// Stand-ins for the values only a mailer can work out.
    /// </summary>
    /// <remarks>
    /// Without these a preview shows a hole exactly where the important part of the letter goes —
    /// a confirmation with no button, a pass with no code — and an author would reasonably
    /// conclude their template was broken.
    /// </remarks>
    public static IReadOnlyDictionary<string, (string Value, bool IsHtml)> SuppliedFor(
        MailKindInfo kind, SiteIdentity site)
    {
        var sample = new Dictionary<string, (string, bool)>(StringComparer.OrdinalIgnoreCase);
        var url = site.AbsoluteUrl("/confirm?code=THIS-IS-A-PREVIEW");

        foreach (var token in kind.Supplied)
        {
            sample[token.Name] = token.Name switch
            {
                "ConfirmButton" or "ResetButton" =>
                    (MailBlocks.Button(token.Name == "ConfirmButton" ? "Confirm my email" : "Reset password", url), true),

                // A grey square standing in for the real code: the shape and size an author is
                // laying out around, without minting a pass that would actually admit somebody.
                "PassImage" => ("""<img src="https://placehold.co/180x180?text=QR" width="180" height="180" """
                              + """alt="Your entry pass" style="display:block;border:0;" />""", true),

                "ResetCode" => ("A1B2-C3D4", false),

                "AppealButton" => (MailBlocks.Button("Appeal this decision", url), true),
                "CreditNote" => ("The event credit spent on it has been returned, and can be used for another event.", false),

                // The store's letters (S4.4): the shapes an author lays out around.
                "ItemsTable" => (MailBlocks.LineItems(), true),
                "SummaryTable" => (MailBlocks.LineItems(), true),
                "ShipTo" => ("Marguerite Ashdown<br>1201 Del Rio Pike<br>Franklin, TN 37064", true),
                _ => (url, false),
            };
        }

        return sample;
    }

    private static object? Value(string table, MailTemplateSchema.Column column)
    {
        // Names first: a column called DisplayName should read like a display name, whatever
        // table it is in, because that is what an author is looking at when they judge a layout.
        var n = column.Name;

        if (Is(n, "DisplayName", "FullName", "Name", "Title"))
            return table switch
            {
                "AppUsers"      => "Marguerite Ashdown",
                "Organizations" => "Paranormal 365",
                "HostedEvents"  => "Halloween Lock-In at the Thomas House",
                "Tours"         => "The Old Franklin Ghost Walk",
                "Cases"         => "The house on Del Rio Pike",
                "Places"        => "The Thomas House Hotel",
                _               => "Halloween Lock-In",
            };

        if (Is(n, "Email")) return "marguerite.ashdown@example.com";
        if (Is(n, "UserName")) return "marguerite";
        if (Is(n, "FirstName")) return "Marguerite";
        if (Is(n, "LastName", "Surname")) return "Ashdown";
        if (Is(n, "PhoneNumber", "Phone")) return "(615) 555-0148";
        if (Is(n, "City")) return "Franklin";
        if (Is(n, "State")) return "TN";
        if (Is(n, "PostalCode", "Zip")) return "37064";
        if (Is(n, "StreetAddress", "Address", "Address1")) return "1201 Del Rio Pike";
        if (Is(n, "TimeZoneId")) return "America/Chicago";
        if (Is(n, "Reference")) return "2026-042";

        return column.Type switch
        {
            "text"          => "Something somebody typed",
            "yes or no"     => true,
            "date and time" => WrittenAtUtc,
            "amount"        => 99.00m,
            "reference"     => Guid.Parse("a1b2c3d4-0000-4000-8000-000000000042"),
            "choice"        => "Active",
            _               => 42,
        };
    }

    private static bool Is(string column, params string[] any)
        => any.Any(a => column.Equals(a, StringComparison.OrdinalIgnoreCase));
}
