using System.Reflection;
using Ben.Data.Common.Constants;
using Ben.Data.WebApi.Controllers.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The error-log reader: who may open it, and what may reach a SQL statement.
/// </summary>
/// <remarks>
/// <para>The rows themselves are not exercised here. Serilog owns the <c>Logs</c> table and
/// creates it outside EF, so the controller reads it with raw SQL over the live connection —
/// which the in-memory provider cannot answer. Asserting against a fake would test the fake.
/// What IS testable is everything that decides whether the query is safe to run at all, and
/// that is where the risk actually lives.</para>
/// </remarks>
public sealed class AdminErrorLogControllerTests
{
    // ── Who may read it ───────────────────────────────────────────────────────

    [Fact]
    public void The_log_is_open_to_Admin_and_SuperAdmin_together()
    {
        var authorize = typeof(AdminErrorLogController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .ToList();

        var attribute = Assert.Single(authorize);

        // AppAdministrator is the policy that accepts both roles. SuperAdmin alone would mean the
        // person on call cannot see why the site is failing, which is the one moment this page
        // exists for.
        Assert.Equal(AuthPolicyNames.AppAdministrator, attribute.Policy);
    }

    [Fact]
    public void It_never_authorizes_by_role_attribute()
    {
        // The repo-wide guard in AdminAuthorizationIsAPolicyTests covers this too. Stated again
        // here because the failure is invisible from the outside: [Authorize(Roles = ...)] pins no
        // scheme, so an Entra caller is re-authenticated with the default handler and answered 401
        // — the role check never runs, and the page simply looks broken for that sign-in.
        var attribute = typeof(AdminErrorLogController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Single();

        Assert.True(string.IsNullOrEmpty(attribute.Roles),
            "Gate on the policy, never on Roles — a bare Roles attribute answers 401 to a valid Entra caller.");
    }

    [Fact]
    public void Anonymous_access_is_not_allowed_anywhere_on_it()
    {
        // A log carries request paths, user ids and stack traces. One [AllowAnonymous] on one
        // action would publish all of it.
        var offenders = typeof(AdminErrorLogController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .Select(m => m.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"These actions allow anonymous callers: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void It_is_read_only()
    {
        // Deleting belongs to LogRetentionJob, which has a minimum window and batches its work. A
        // button that empties the log is one click between a busy administrator and the evidence
        // they were about to need.
        var writeVerbs = typeof(AdminErrorLogController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<HttpPostAttribute>()   is not null
                     || m.GetCustomAttribute<HttpPutAttribute>()    is not null
                     || m.GetCustomAttribute<HttpDeleteAttribute>() is not null
                     || m.GetCustomAttribute<HttpPatchAttribute>()  is not null)
            .Select(m => m.Name)
            .ToList();

        Assert.True(writeVerbs.Count == 0,
            $"The error log reader must not write. Found: {string.Join(", ", writeVerbs)}");
    }

    [Fact]
    public void It_answers_on_the_admin_route()
    {
        var route = typeof(AdminErrorLogController).GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(route);
        Assert.Equal("api/admin/error-logs", route!.Template);
    }

    // ── What may reach a SQL statement ────────────────────────────────────────

    [Theory]
    [InlineData("Logs")]
    [InlineData("ErrorLogs")]
    [InlineData("_staging_logs")]
    [InlineData("Logs2026")]
    public void A_plain_identifier_is_accepted(string name)
        => Assert.True(AdminErrorLogController.IsPlainIdentifier(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Logs; DROP TABLE AppUsers--")]   // the statement this guard exists to stop
    [InlineData("Logs WHERE 1=1")]
    [InlineData("[Logs]")]                        // already-bracketed would nest brackets
    [InlineData("dbo.Logs")]                      // schema-qualified is not a bare identifier
    [InlineData("Logs'")]
    [InlineData("2026Logs")]                      // may not begin with a digit
    [InlineData("Logs Table")]
    public void Anything_that_is_not_a_bare_identifier_is_refused(string? name)
        => Assert.False(AdminErrorLogController.IsPlainIdentifier(name));

    [Fact]
    public void The_guard_matches_the_retention_jobs_rule()
    {
        // The two touch the same configured table name — one to read it, one to delete from it.
        // If the rules ever diverge, one of them is wrong, and the looser one is the hole.
        foreach (var candidate in new[] { "Logs", "dbo.Logs", "Logs; DROP TABLE AppUsers--", "", "  ", "[Logs]" })
        {
            Assert.Equal(
                Ben.Data.WebApi.Services.Scheduling.LogRetentionJob.IsPlainIdentifier(candidate),
                AdminErrorLogController.IsPlainIdentifier(candidate));
        }
    }

    // ── What the reader assumes about the table ───────────────────────────────

    [Fact]
    public void The_reader_never_assumes_a_column_width_Serilog_does_not_create()
    {
        // Serilog's autoCreateSqlTable makes Id an INT identity, and SqlClient's typed getters do
        // not widen: GetInt64 on an int column throws InvalidCastException. The live table is int,
        // so the grid had never loaded — found 2026-09-03 by the browser suite on a fresh database,
        // where the same exception surfaced as "Could not load the error log." Nothing here can
        // open SQL Server, so this reads the source instead: the reader must go through
        // Convert/GetValue for Id, never a width-specific getter.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var source = File.ReadAllText(Path.Combine(dir!.FullName,
            "Ben.Data.WebApi", "Controllers", "Admin", "AdminErrorLogController.cs"));

        Assert.DoesNotContain("GetInt64(", source);
        Assert.Contains("Convert.ToInt64(reader.GetValue(0))", source);
    }
}
