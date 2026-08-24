using Ben.Data.Source.Context;
using Ben.Data.WebApi.Controllers.Public;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Redaction;

/// <summary>One person to redact: the name tokens that identify them, and what to say instead.</summary>
public sealed record RosterEntry(IReadOnlyList<string> Tokens, string Replacement);

/// <summary>Everyone a private case's public prose must not name.</summary>
public sealed record RedactionRoster(IReadOnlyList<RosterEntry> Entries)
{
    public static readonly RedactionRoster Empty = new([]);
}

/// <summary>
/// Builds the substitution roster for a case (item 184): the client, from the case's originating
/// request, and every <c>CaseRelatedPerson</c>. Returns null for a case that is NOT a private
/// engagement — Ben's scope rule: replacements run only on cases designated private, and a
/// public-place case renders exactly as written.
/// </summary>
/// <remarks>
/// <para><b>Replacement ladder</b> (noun phrases, never pronouns — a noun phrase is valid
/// anywhere a proper name sits; pronouns need subject/object case agreement and a gender guess,
/// neither of which prose substitution can do honestly):</para>
/// <para>Client: their chosen alias → the org's pseudonym → "the family" when two or more
/// related people live at the property → "the client".</para>
/// <para>Related person: their <c>PublicLabel</c> → a label derived from Relationship → then
/// "a resident" when they live at the property, else "a witness".</para>
/// </remarks>
public static class CaseRedactionRoster
{
    public static async Task<RedactionRoster?> ForCaseAsync(
        BenDataContext db, Guid caseId, CancellationToken ct)
    {
        var rosters = await ForCasesAsync(db, [caseId], ct);
        return rosters.TryGetValue(caseId, out var roster) ? roster : null;
    }

    /// <summary>
    /// Rosters for a page of cases in two queries, keyed by case id. Cases that are not private
    /// engagements are simply absent — the caller renders those verbatim.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, RedactionRoster>> ForCasesAsync(
        BenDataContext db, IReadOnlyCollection<Guid> caseIds, CancellationToken ct)
    {
        if (caseIds.Count == 0) return new Dictionary<Guid, RedactionRoster>();

        var cases = await db.Cases.AsNoTracking()
            .Where(c => caseIds.Contains(c.Id) && c.IsPrivateEngagement)
            .Select(c => new
            {
                c.Id,
                c.ClientDisplayAlias,
                c.PublicPseudonym,
                Client = c.ClientRequest == null ? null : new
                {
                    c.ClientRequest.AppUser.FirstName,
                    c.ClientRequest.AppUser.LastName,
                    c.ClientRequest.AppUser.DisplayName,
                },
            })
            .ToListAsync(ct);
        if (cases.Count == 0) return new Dictionary<Guid, RedactionRoster>();

        var privateIds = cases.Select(c => c.Id).ToList();
        var people = await db.CaseRelatedPeople.AsNoTracking()
            .Where(p => privateIds.Contains(p.CaseId))
            .Select(p => new { p.CaseId, p.Name, p.Relationship, p.LivesAtProperty, p.PublicLabel })
            .ToListAsync(ct);
        var peopleByCase = people.ToLookup(p => p.CaseId);

        var result = new Dictionary<Guid, RedactionRoster>(cases.Count);
        foreach (var c in cases)
        {
            var entries = new List<RosterEntry>();
            var casePeople = peopleByCase[c.Id].ToList();

            if (c.Client is not null)
            {
                var tokens = Tokens(c.Client.FirstName, c.Client.LastName, c.Client.DisplayName);
                if (tokens.Count > 0)
                {
                    var residents = casePeople.Count(p => p.LivesAtProperty);
                    var replacement = PublicClientName.Clean(c.ClientDisplayAlias)
                        ?? PublicClientName.Clean(c.PublicPseudonym)
                        ?? (residents >= 2 ? "the family" : "the client");
                    entries.Add(new RosterEntry(tokens, replacement));
                }
            }

            foreach (var p in casePeople)
            {
                var tokens = Tokens(p.Name);
                if (tokens.Count == 0) continue;
                entries.Add(new RosterEntry(tokens, LabelFor(p.PublicLabel, p.Relationship, p.LivesAtProperty)));
            }

            result[c.Id] = new RedactionRoster(entries);
        }
        return result;
    }

    /// <summary>
    /// Name tokens worth matching: split on spaces, at least three characters (the item-176
    /// rule — initials would flag half the alphabet), deduplicated case-insensitively.
    /// </summary>
    private static List<string> Tokens(params string?[] names)
        => [.. names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .SelectMany(n => n!.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(t => t.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static string LabelFor(string? publicLabel, string? relationship, bool livesAtProperty)
    {
        if (!string.IsNullOrWhiteSpace(publicLabel)) return publicLabel.Trim();

        var r = relationship?.Trim().ToLowerInvariant() ?? "";
        if (r.Length > 0)
        {
            if (r.Contains("owner") || r.Contains("landlord")) return "the homeowner";
            if (r.Contains("neighbor") || r.Contains("neighbour")) return "a neighbor";
            if (r.Contains("tenant") || r.Contains("roommate") || r.Contains("lodger")) return "a resident";
            string[] family = ["mother", "father", "mom", "dad", "son", "daughter", "child", "spouse",
                              "husband", "wife", "partner", "sibling", "brother", "sister",
                              "grand", "aunt", "uncle", "cousin", "niece", "nephew", "in-law"];
            if (family.Any(r.Contains)) return "a family member";
            if (r.Contains("friend") || r.Contains("guest") || r.Contains("visitor")) return "a visitor";
        }

        return livesAtProperty ? "a resident" : "a witness";
    }
}
