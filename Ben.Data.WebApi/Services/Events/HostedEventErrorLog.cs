using System.Data;
using System.Text.RegularExpressions;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Service.Models.Admin;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// The errors the server logged on hosted-event addresses, for the Event health tab.
/// </summary>
/// <remarks>
/// <para><b>Read from the server's own log table</b>, the one the error log page reads, with the same guard on its
/// configured name. That table belongs to the logging sink, exists only on SQL Server, and keeps its time in the
/// server's LOCAL clock, so the days here are the server's days — and anywhere it cannot be read the tab says why
/// rather than drawing an empty chart that looks like a clean week.</para>
///
/// <para><b>Addresses, not requests.</b> Ids and tokens are replaced so one broken screen reads as one row, however many
/// events it broke on, and no guest's pass token reaches a web page.</para>
/// </remarks>
public static partial class HostedEventErrorLog
{
    /// <summary>The panels' data, or why there is none.</summary>
    public sealed record Result(IReadOnlyList<StatPoint>? PerDay, IReadOnlyList<StatSlice>? ByAddress, string? Unavailable)
    {
        public static Result Without(string why) => new(null, null, why);
    }

    /// <summary>At most this many error rows are read for one window; a bad week is still counted, just not beyond this.</summary>
    private const int MaximumRows = 5000;

    [GeneratedRegex(@"/(api/admin/hosted-events|api/organizations/[^/]+/events|api/public/hosted-events|api/public/events|hosted-events|events|my-events|attending|helping|event-picks|event-passes|event-photo|event-files|event-room|event-keep)(/|$|\?)",
        RegexOptions.IgnoreCase)]
    private static partial Regex EventAddress();

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex Guid_();

    [GeneratedRegex(@"/[0-9a-fA-F]{24,}|/[A-Za-z0-9_-]{32,}")]
    private static partial Regex Token();

    /// <summary>Whether an address belongs to hosted events.</summary>
    public static bool IsEventAddress(string? path) => path is { Length: > 0 } && EventAddress().IsMatch(path);

    /// <summary>An address with its ids and tokens taken out, so one screen is one row.</summary>
    public static string Normalise(string path)
        => Token().Replace(Guid_().Replace(path.Split('?')[0], "{id}"), "/{token}");

    public static async Task<Result> ReadAsync(
        BenDataContext db, IConfiguration configuration, int days, int topN, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer())
            return Result.Without("The server's error log is only kept on SQL Server.");

        var table = configuration["Logging:Retention:TableName"] ?? "Logs";
        if (!AdminErrorLogController.IsPlainIdentifier(table))
            return Result.Without("The log table's configured name is not a plain identifier, so it is not read.");

        var sinceLocal = DateTime.Now.Date.AddDays(-(days - 1));
        var rows = new List<(DateTime Day, string Path)>();
        try
        {
            var connection = (SqlConnection)db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT TOP (@take) [TimeStamp],
                       TRY_CAST(Properties AS xml).value('(/properties/property[@key="RequestPath"])[1]', 'nvarchar(400)')
                FROM [{table}]
                WHERE Level IN ('Error', 'Fatal') AND [TimeStamp] >= @since
                ORDER BY [TimeStamp] DESC
                """;
            command.Parameters.Add(new SqlParameter("@take", MaximumRows));
            command.Parameters.Add(new SqlParameter("@since", sinceLocal));

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var path = reader.IsDBNull(1) ? null : reader.GetString(1);
                if (IsEventAddress(path)) rows.Add((reader.GetDateTime(0).Date, Normalise(path!)));
            }
        }
        catch (SqlException ex)
        {
            return Result.Without($"The server's error log could not be read: {ex.Message.Split('\n')[0]}");
        }

        return new Result(
            AdminStatsController.FillDays(rows.GroupBy(r => r.Day).Select(g => (g.Key, g.Count())), sinceLocal, days),
            [.. rows.GroupBy(r => r.Path).OrderByDescending(g => g.Count()).Take(topN).Select(g => new StatSlice(g.Key, g.Count()))],
            null);
    }
}
