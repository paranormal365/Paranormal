using Ben.Data.Common.Mail;
using Ben.Data.WebApi.Services.Mail;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// What an author may put in a letter, read from the real model (item 246).
/// </summary>
public sealed class MailTemplateSchemaTests
{
    /// <summary>
    /// The one that matters.
    /// </summary>
    /// <remarks>
    /// <c>AppUser</c> derives from ASP.NET Identity's <c>IdentityUser</c>, so it carries
    /// <c>PasswordHash</c>, <c>SecurityStamp</c> and <c>ConcurrencyStamp</c> without one of them
    /// appearing in the entity's own file. Anybody reading that file would conclude a table
    /// allowlist was enough. It is not, and this asserts against the model rather than the source.
    /// </remarks>
    [Fact]
    public async Task The_identity_columns_are_never_offered()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();

        var users = MailTemplateSchema.For(MailKinds.ResetYourPassword, db)
            .Single(t => t.Name == "AppUsers");

        var names = users.Columns.Select(c => c.Name).ToList();

        Assert.DoesNotContain("PasswordHash", names);
        Assert.DoesNotContain("SecurityStamp", names);
        Assert.DoesNotContain("ConcurrencyStamp", names);
        Assert.DoesNotContain("TwoFactorEnabled", names);
        Assert.DoesNotContain("NormalizedEmail", names);

        // And it still offers the things a letter is actually written with.
        Assert.Contains("DisplayName", names);
        Assert.Contains("Email", names);
    }

    [Fact]
    public async Task Only_the_tables_this_letter_carries_are_offered()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();

        var names = MailTemplateSchema.For(MailKinds.ResetYourPassword, db)
            .Select(t => t.Name).ToList();

        Assert.Equal(["AppUsers"], names);

        var forACase = MailTemplateSchema.For(MailKinds.CaseStatusChanged, db)
            .Select(t => t.Name).ToList();

        Assert.Contains("Cases", forACase);
        Assert.Contains("Organizations", forACase);
    }

    [Fact]
    public async Task Every_offered_column_is_something_a_person_could_read()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();

        foreach (var kind in MailKinds.All)
            foreach (var table in MailTemplateSchema.For(kind, db))
                Assert.All(table.Columns, c =>
                {
                    Assert.False(string.IsNullOrWhiteSpace(c.Name));
                    Assert.Contains(c.Type, new[]
                        { "text", "yes or no", "date and time", "amount", "reference", "number", "choice" });
                });
    }

    /// <summary>
    /// Every kind names tables that actually exist.
    /// </summary>
    /// <remarks>
    /// A kind naming a table that was since renamed would silently offer an author fewer tables
    /// than the letter carries, and nothing else would notice.
    /// </remarks>
    [Fact]
    public async Task Every_declared_table_is_a_real_one()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();

        var real = db.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .Where(n => n is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

        foreach (var kind in MailKinds.All)
            foreach (var table in kind.Context)
                Assert.True(real.Contains(table),
                    $"{kind.Key} names the table \"{table}\", which the model does not have.");
    }
}
