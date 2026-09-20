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
