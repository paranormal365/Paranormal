namespace Ben.Data.Common.Constants;

/// <summary>
/// Identifies which application tier produced a log or audit entry.
/// </summary>
/// <remarks>
/// Written to the <c>Source</c> column on <c>AuditLog</c> rows and enriched
/// as a Serilog property so that every error in the <c>Logs</c> table is
/// tagged with its origin application.
/// <para>
/// Passed explicitly by calling code rather than resolved from configuration
/// so that the value is always unambiguous at the call site.
/// </para>
/// </remarks>
public static class AppSources
{
    /// <summary>Operations originating from the <c>Ben.Data.WebApi</c> ASP.NET Core Web API.</summary>
    public const string WebApi = "WebApi";

    /// <summary>Operations originating from the <c>Ben.Web.WebApp</c> Blazor Server application.</summary>
    public const string WebApp = "WebApp";
}
