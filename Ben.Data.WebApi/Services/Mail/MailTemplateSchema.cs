using Ben.Data.Common.Mail;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Mail;

/// <summary>
/// The tables and columns a template's author may pick from (item 246).
/// </summary>
/// <remarks>
/// <para><b>Two gates, and both are needed.</b> The kind decides which TABLES — that is Ben's
/// "only what the letter has" (2026-09-20), and it is what stops a token naming a row the letter
/// never loaded. This decides which COLUMNS, and it is needed because a table being legitimate
/// does not make every column in it legitimate: <c>AppUser</c> derives from ASP.NET Identity's
/// <c>IdentityUser</c>, so it carries <c>PasswordHash</c>, <c>SecurityStamp</c> and
/// <c>ConcurrencyStamp</c> without any of them appearing in the entity's own source file. A table
/// allowlist alone would have offered all three in a dropdown.</para>
///
/// <para><b>Deny by shape, not by list.</b> Naming the columns to hide would be a list that goes
/// stale the first time a package adds one — which is exactly how the Identity columns got here.
/// Matching on what a name MEANS catches the next one too.</para>
///
/// <para><b>Read from the EF model</b> rather than a hand-written map, so a column that is renamed
/// or dropped stops being offered without anybody remembering to come here.</para>
/// </remarks>
public static class MailTemplateSchema
{
    /// <summary>A column somebody may put in a letter.</summary>
    public sealed record Column(string Name, string Type);

    /// <summary>A table, with what may be read from it.</summary>
    public sealed record Table(string Name, IReadOnlyList<Column> Columns);

    /// <summary>
    /// Fragments that make a column secret whatever table it is in.
    /// </summary>
    /// <remarks>
    /// Substring matching, case-insensitively: <c>PasswordHash</c>, <c>SecurityStamp</c>,
    /// <c>ConcurrencyStamp</c>, <c>TwoFactorEnabled</c> and anything anybody later adds whose name
    /// admits what it is. A column that has to be read to be understood does not belong in a
    /// letter.
    /// </remarks>
    private static readonly string[] NeverOffered =
    [
        "password", "hash", "stamp", "token", "secret", "salt", "apikey", "key",
        "twofactor", "lockout", "accessfailed", "normalized", "recovery", "otp",
    ];

    /// <summary>
    /// Types worth putting in a letter.
    /// </summary>
    /// <remarks>
    /// A Guid is offered because a reference number sometimes IS one, but nothing structural:
    /// byte arrays, collections and owned types have no reading a person would want.
    /// </remarks>
    internal static bool Readable(Type t)
    {
        var bare = Nullable.GetUnderlyingType(t) ?? t;
        return bare == typeof(string) || bare == typeof(bool) || bare == typeof(Guid)
            || bare == typeof(DateTime) || bare == typeof(DateTimeOffset) || bare == typeof(decimal)
            || bare == typeof(int) || bare == typeof(long) || bare == typeof(short)
            || bare == typeof(double) || bare == typeof(float) || bare.IsEnum;
    }

    internal static bool Secret(string column)
        => NeverOffered.Any(bad => column.Contains(bad, StringComparison.OrdinalIgnoreCase));

    /// <summary>What an author of this kind of letter may pick from.</summary>
    public static IReadOnlyList<Table> For(MailKindInfo kind, BenDataContext db)
    {
        var model = db.Model.GetEntityTypes().ToList();
        var tables = new List<Table>();

        foreach (var wanted in kind.Context)
        {
            var entity = model.FirstOrDefault(e =>
                string.Equals(e.GetTableName(), wanted, StringComparison.OrdinalIgnoreCase));
            if (entity is null) continue;

            var columns = entity.GetProperties()
                .Where(p => !p.IsShadowProperty())
                .Select(p => new { p.Name, Type = p.ClrType })
                .Where(p => Readable(p.Type) && !Secret(p.Name))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(p => new Column(p.Name, Friendly(p.Type)))
                .ToList();

            if (columns.Count > 0) tables.Add(new Table(wanted, columns));
        }

        return tables;
    }

    private static string Friendly(Type t)
    {
        var bare = Nullable.GetUnderlyingType(t) ?? t;
        if (bare.IsEnum) return "choice";
        if (bare == typeof(string)) return "text";
        if (bare == typeof(bool)) return "yes or no";
        if (bare == typeof(DateTime) || bare == typeof(DateTimeOffset)) return "date and time";
        if (bare == typeof(decimal) || bare == typeof(double) || bare == typeof(float)) return "amount";
        if (bare == typeof(Guid)) return "reference";
        return "number";
    }
}
