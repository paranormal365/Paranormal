using System.Text.Json;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The API's Serilog sinks are declared in a fixed order, because configuration merges them by it.
/// </summary>
/// <remarks>
/// <para><b>W-S7 of the 2026-09-06 evaluation.</b> The same "No confirmation message" error
/// printed twice per sign-up and was read as two failed sends. It was one send and two console
/// sinks, and the second was an accident: <c>Serilog:WriteTo</c> is a JSON array, .NET
/// configuration merges arrays by INDEX, and it merges the object at each index KEY BY KEY rather
/// than replacing it.</para>
///
/// <para>So while <c>appsettings.json</c> held <c>[MSSqlServer]</c> and
/// <c>appsettings.Development.json</c> held <c>[Console, MSSqlServer]</c>, index 0 resolved to a
/// hybrid — <c>Name</c> of Console, <c>Args</c> of the SQL sink, including its
/// <c>restrictedToMinimumLevel: Error</c>. A console that printed errors and nothing else, on top
/// of the one the host added in code. Errors doubled; warnings did not. Nothing was misconfigured
/// in either file on its own.</para>
///
/// <para>The two files now agree on order, and this holds it. <c>appsettings.Development.json</c>
/// is gitignored, so it cannot be asserted here — the base file is the half that can be, and the
/// comment beside it says the other half out loud.</para>
///
/// <para>This is also why the first attempt at fixing W-S7 was wrong. It skipped the code sink
/// whenever configuration mentioned Console, which on this machine left only the Error-restricted
/// hybrid: the console fell silent below Error, and the run that proved it was a host started by
/// hand with no logs at all. Reading the merged configuration rather than either file is what
/// found the real shape.</para>
/// </remarks>
public sealed class SerilogSinkOrderTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    private static JsonElement Sinks()
    {
        var path = Path.Combine(RepoRoot().FullName, "Ben.Data.WebApi", "appsettings.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("Serilog").GetProperty("WriteTo").Clone();
    }

    [Fact]
    public void The_console_is_first_and_the_database_second()
    {
        var sinks = Sinks();
        Assert.Equal(2, sinks.GetArrayLength());

        Assert.Equal("Console", sinks[0].GetProperty("Name").GetString());
        Assert.Equal("MSSqlServer", sinks[1].GetProperty("Name").GetString());
    }

    /// <summary>
    /// The console sink carries no level restriction of its own.
    /// </summary>
    /// <remarks>
    /// This is the property that actually broke: the hybrid inherited <c>Error</c> from the SQL
    /// sink's args and stopped printing anything below it. A console restricted to errors is a
    /// console that hides the warning carrying the sign-up link.
    /// </remarks>
    [Fact]
    public void The_console_prints_more_than_errors()
    {
        var console = Sinks()[0];
        Assert.True(console.TryGetProperty("Args", out var args), "The console sink has no Args.");
        Assert.False(args.TryGetProperty("restrictedToMinimumLevel", out _),
            "The console sink is level-restricted, so anything below that level is invisible on "
            + "the terminal — including the warning that carries a local sign-up's confirmation "
            + "link.");
    }

    /// <summary>The database sink keeps its restriction — that one is deliberate.</summary>
    [Fact]
    public void The_database_still_only_keeps_errors()
    {
        var sql = Sinks()[1];
        Assert.Equal("Error",
            sql.GetProperty("Args").GetProperty("restrictedToMinimumLevel").GetString());
    }
}
