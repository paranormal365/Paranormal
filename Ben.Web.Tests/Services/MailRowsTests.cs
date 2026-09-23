using Ben.Data.Common.Mail;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Mail;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The rows a letter hands its template (item 246, found broken 2026-09-23).
/// </summary>
/// <remarks>
/// Nineteen letters handed a template nothing, so every table token rendered blank — and two
/// published templates on production would have gone out that way. These pin the helper every
/// letter now uses: the right table for each entity, the columns the editor offers and no others,
/// and nothing the letter's kind does not declare.
/// </remarks>
public sealed class MailRowsTests
{
    private static readonly MailKindInfo Removed = MailKinds.GuestRemoved;

    [Fact]
    public void Each_entity_becomes_the_table_its_kind_names()
    {
        var payload = MailRows.For(Removed,
            new AppUser { DisplayName = "Grace Guest", Email = "grace@example.test" },
            new HostedEvent { Name = "Halloween Lock-In", CancelledReason = "The hotel flooded." },
            new Organization { Name = "The Thomas House" });

        Assert.Equal("Grace Guest", payload.Tables!["AppUsers"]["DisplayName"]);
        Assert.Equal("The hotel flooded.", payload.Tables["HostedEvents"]["CancelledReason"]);
        Assert.Equal("The Thomas House", payload.Tables["Organizations"]["Name"]);
    }

    [Fact]
    public void A_secret_column_is_never_put_in_a_row()
    {
        var payload = MailRows.For(Removed,
            new AppUser { DisplayName = "Grace", PasswordHash = "AQAAAA-not-for-a-letter", SecurityStamp = "stamp" });

        var person = payload.Tables!["AppUsers"];
        Assert.DoesNotContain(person.Keys, k => k.Contains("Password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(person.Keys, k => k.Contains("Stamp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(person.Values, v => v is string s && s.Contains("not-for-a-letter"));
    }

    /// <summary>The kind's context is the security boundary; passing more changes nothing.</summary>
    [Fact]
    public void A_table_the_kind_does_not_declare_is_dropped()
    {
        var payload = MailRows.For(Removed,
            new HostedEvent { Name = "Halloween Lock-In" },
            new Case { Title = "Somebody's house" });

        Assert.True(payload.Tables!.ContainsKey("HostedEvents"));
        Assert.False(payload.Tables.ContainsKey("Cases"));
    }

    [Fact]
    public void A_person_with_no_account_is_a_row_all_the_same()
    {
        var payload = MailRows.For(Removed, MailRows.Person("walkup@example.test", null, firstName: "Ada"));

        Assert.Equal("walkup@example.test", payload.Tables!["AppUsers"]["Email"]);
        Assert.Equal("Ada", payload.Tables["AppUsers"]["DisplayName"]);
    }

    /// <summary>What a published template actually prints, once the rows are there.</summary>
    [Fact]
    public void A_template_reads_the_rows_it_is_handed()
    {
        var payload = MailRows.For(Removed,
            new AppUser { DisplayName = "Grace" },
            new HostedEvent { Name = "Halloween Lock-In", CancelledReason = "The hotel flooded." });

        var tables = payload.Tables!;
        var rendered = MailTokens.Render(
            "<p>Hello {AppUsers.DisplayName}. {HostedEvents.Name} was removed: {HostedEvents.CancelledReason}</p>",
            new MailTokens.Context(tables, TimeZoneInfo.Utc, DateTime.UtcNow, "IsHaunted", "https://test.local/", null));

        Assert.Equal("<p>Hello Grace. Halloween Lock-In was removed: The hotel flooded.</p>", rendered);
    }
}
