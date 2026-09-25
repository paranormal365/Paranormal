using Ben.Data.Common.Interfaces;
using Ben.Data.Common.Mail;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Mail;

/// <summary>
/// The rows a letter hands its template, built from the entities the sender already holds.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> A kind declares which tables its template may read, and the editor
/// offers every readable column of them — but until 2026-09-23 nineteen of the site's letters handed
/// a template NOTHING. A table token with no row renders as an empty string, so a template the editor
/// happily accepted went out with blanks: two were published on production, an organizer's
/// "bookings have arrived" greeting nobody about nothing and a guest's "your event was removed"
/// with no reason in it. No such letter had yet been sent.</para>
///
/// <para><b>The same columns the editor offers, by the same rule.</b> The values are read through
/// the EF model with <see cref="MailTemplateSchema"/>'s own filters, so a column the editor offers is
/// a column a letter fills, and a secret the editor hides is never put in a row either. A table the
/// kind does not declare is dropped here too — the kind's context is the security boundary, and a
/// sender passing more than it should changes nothing.</para>
///
/// <para><b>A person without an account</b> — a guest who picked seats, somebody signed up by a
/// guide — has no <c>AppUser</c> entity, so <see cref="Person"/> builds that row from the address
/// and name they gave.</para>
/// </remarks>
public static class MailRows
{
    /// <summary>A row written by hand, for a table whose entity the sender does not have.</summary>
    public sealed record Manual(string Table, IReadOnlyDictionary<string, object?> Values);

    /// <summary>The person a letter is written to, when there is no account behind them.</summary>
    public static Manual Person(string? email, string? displayName, string? firstName = null)
        => new("AppUsers", new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Email"] = email,
            ["DisplayName"] = displayName ?? firstName,
            ["FirstName"] = firstName,
        });

    /// <summary>The payload for a letter of this kind, from what the sender has.</summary>
    /// <param name="entities">
    /// Entities (an <c>AppUser</c>, a <c>HostedEvent</c>, an <c>Organization</c>…) or
    /// <see cref="Manual"/> rows. Nulls are skipped, and so is anything the kind does not declare.
    /// </param>
    public static MailPayload For(MailKindInfo kind, params object?[] entities)
        => For(kind, supplied: null, entities);

    /// <summary>The same, with the values a kind declares as supplied tokens.</summary>
    public static MailPayload For(
        MailKindInfo kind,
        IReadOnlyDictionary<string, MailSuppliedValue>? supplied,
        params object?[] entities)
    {
        var tables = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities)
        {
            if (entity is null) continue;

            var (table, row) = entity is Manual manual
                ? (manual.Table, manual.Values)
                : (TableOf(entity), RowOf(entity));

            // Only what this letter's kind declares, and the first row given for a table wins —
            // a letter is about one event, one venue, one person.
            if (table is null || row is null) continue;
            if (!kind.Context.Contains(table, StringComparer.OrdinalIgnoreCase)) continue;
            tables.TryAdd(table, row);
        }

        return new MailPayload(Tables: tables, Supplied: supplied);
    }

    /// <summary>
    /// The EF model, read once from a context that never connects.
    /// </summary>
    /// <remarks>
    /// Several senders hold no context at the moment they write — the guest-removed letter among
    /// them — and threading one through only to read the model would change a dozen signatures. The
    /// model is the same for every <see cref="BenDataContext"/>; building it opens no connection.
    /// </remarks>
    private static readonly Lazy<Microsoft.EntityFrameworkCore.Metadata.IModel> Model = new(() =>
    {
        using var modelOnly = new BenDataContext(new DbContextOptionsBuilder<BenDataContext>()
            .UseSqlServer("Server=model-only;Database=model-only").Options);
        return modelOnly.Model;
    });

    private static string? TableOf(object entity)
        => Model.Value.FindEntityType(entity.GetType())?.GetTableName();

    private static IReadOnlyDictionary<string, object?>? RowOf(object entity)
    {
        var type = Model.Value.FindEntityType(entity.GetType());
        if (type is null) return null;

        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in type.GetProperties())
        {
            if (p.IsShadowProperty() || p.PropertyInfo is null) continue;
            if (!MailTemplateSchema.Readable(p.ClrType) || MailTemplateSchema.Secret(p.Name)) continue;
            row[p.Name] = p.PropertyInfo.GetValue(entity);
        }
        return row;
    }
}
