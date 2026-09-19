using System.Data;
using System.Text.RegularExpressions;
using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// Reads Serilog's error table, so the log can be looked at without a SQL client.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Serilog writes errors to <c>Logs</c> and nothing has ever read
/// them back. Every diagnosis so far has meant opening a query window on the server, which means
/// in practice the log is consulted only by whoever can do that, only when they already suspect
/// something. The mail failure of 2026-08-31 is the cost of that: it left no trace anyone looked
/// at for hours.</para>
///
/// <para><b>Why raw SQL rather than EF.</b> Serilog creates and owns this table
/// (<c>autoCreateSqlTable</c>). Mapping it as an entity would put the application's model in
/// charge of a schema the logger changes, and a migration would then fight the sink. The same
/// reasoning <see cref="Services.Scheduling.LogRetentionJob"/> follows for its deletes.</para>
///
/// <para><b>The table name is configuration, so it is validated the same way.</b> It reaches a
/// command as an identifier and cannot be parameterised, so it is checked against a plain
/// identifier pattern first — the identical guard the retention job uses before its DELETE.
/// Everything a caller supplies is a parameter.</para>
///
/// <para><b>Read-only, deliberately.</b> Deleting is the retention job's business, on a window
/// with a floor. A button that empties the log is one click between a busy administrator and the
/// evidence they were about to need.</para>
/// </remarks>
[ApiController]
[Route("api/admin/error-logs")]
// Admin and SuperAdmin both: diagnosing a fault is the job this log exists for, and making it
// SuperAdmin-only would mean the person on call cannot see why the site is failing. Policy, never
// Roles - a bare Roles attribute pins no scheme and answers 401 to an Entra caller.
[Authorize(Policy = AuthPolicyNames.AppAdministrator)]
public sealed class AdminErrorLogController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IConfiguration _configuration;

    public AdminErrorLogController(
        IDbContextFactory<BenDataContext> dbContextFactory, IConfiguration configuration)
    {
        _dbContextFactory = dbContextFactory;
        _configuration = configuration;
    }

    /// <summary>A bare SQL identifier — the table name is the only unparameterised part.</summary>
    private static readonly Regex PlainIdentifierPattern =
        new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>Whether a configured table name may be interpolated into a command.</summary>
    /// <remarks>
    /// Public for the same reason <see cref="Services.Scheduling.LogRetentionJob.IsPlainIdentifier"/>
    /// is: it is the guard standing between a configuration file and a SQL statement, and a guard
    /// nothing can test is a guard nobody should trust. Every other value in these queries is a
    /// parameter; this one cannot be, because an identifier is not parameterisable.
    /// </remarks>
    public static bool IsPlainIdentifier(string? name)
        => !string.IsNullOrWhiteSpace(name) && PlainIdentifierPattern.IsMatch(name);

    /// <summary>
    /// The configured table name, or null when it is missing or not a plain identifier.
    /// </summary>
    private string? ResolveTable()
    {
        var table = _configuration["Logging:Retention:TableName"] ?? "Logs";
        return IsPlainIdentifier(table) ? table : null;
    }

    // ── GET /api/admin/error-logs ─────────────────────────────────────────────

    /// <summary>One page of error rows, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<ErrorLogPagedResponse>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null,
        [FromQuery] string? source = null,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        CancellationToken ct = default)
    {
        var table = ResolveTable();
        if (table is null)
            return Problem("Logging:Retention:TableName is not a plain identifier, so the log cannot be read.");

        var take = Math.Clamp(pageSize, 1, 200);
        var skip = (Math.Max(page, 1) - 1) * take;

        // Built by concatenating only fixed fragments; every value is a parameter.
        var where = new List<string>();
        var parameters = new List<SqlParameter>();

        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(Message LIKE @search OR Exception LIKE @search)");
            parameters.Add(new SqlParameter("@search", $"%{search}%"));
        }
        if (!string.IsNullOrWhiteSpace(source))
        {
            where.Add("Source = @source");
            parameters.Add(new SqlParameter("@source", source));
        }
        if (dateFrom.HasValue)
        {
            where.Add("[TimeStamp] >= @dateFrom");
            parameters.Add(new SqlParameter("@dateFrom", dateFrom.Value));
        }
        if (dateTo.HasValue)
        {
            where.Add("[TimeStamp] <= @dateTo");
            parameters.Add(new SqlParameter("@dateTo", dateTo.Value));
        }

        var whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty;

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);

        var total = 0;
        await using (var countCommand = connection.CreateCommand())
        {
            countCommand.CommandText = $"SELECT COUNT(*) FROM [{table}] {whereSql}";
            foreach (var p in parameters) countCommand.Parameters.Add(Clone(p));
            total = Convert.ToInt32(await countCommand.ExecuteScalarAsync(ct));
        }

        var items = new List<ErrorLogRecord>();
        await using (var command = connection.CreateCommand())
        {
            // RequestPath is pulled out of Serilog's Properties XML rather than stored as its own
            // column: it is the single most useful field for placing an error, and adding a column
            // would mean reconfiguring the sink and leaving every existing row blank.
            command.CommandText = $"""
                SELECT Id, [TimeStamp], Level, Message, Exception, Source, Application,
                       TRY_CAST(Properties AS xml).value(
                           '(/properties/property[@key="RequestPath"])[1]', 'nvarchar(400)') AS RequestPath
                FROM [{table}]
                {whereSql}
                ORDER BY [TimeStamp] DESC
                OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
                """;
            foreach (var p in parameters) command.Parameters.Add(Clone(p));
            command.Parameters.Add(new SqlParameter("@skip", skip));
            command.Parameters.Add(new SqlParameter("@take", take));

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                items.Add(new ErrorLogRecord(
                    // Serilog's autoCreateSqlTable makes Id an INT identity, and SqlClient's
                    // GetInt64 on an int column throws InvalidCastException rather than widening.
                    // Found 2026-09-03 by the Playwright suite on a fresh database - and the live
                    // table is int too, so the grid had never loaded. Convert widens either way.
                    Id:          Convert.ToInt64(reader.GetValue(0)),
                    TimeStamp:   reader.GetDateTime(1),
                    Level:       reader.IsDBNull(2) ? null : reader.GetString(2),
                    Message:     reader.IsDBNull(3) ? null : reader.GetString(3),
                    Exception:   reader.IsDBNull(4) ? null : reader.GetString(4),
                    Source:      reader.IsDBNull(5) ? null : reader.GetString(5),
                    Application: reader.IsDBNull(6) ? null : reader.GetString(6),
                    RequestPath: reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
        }

        return Ok(new ErrorLogPagedResponse(items, total));
    }

    // ── GET /api/admin/error-logs/summary ─────────────────────────────────────

    /// <summary>
    /// The shape of the table: how many rows, how far back, and which messages dominate.
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ErrorLogSummary>> GetSummary(CancellationToken ct)
    {
        var table = ResolveTable();
        if (table is null)
            return Problem("Logging:Retention:TableName is not a plain identifier, so the log cannot be read.");

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);

        int total; DateTime? oldest = null, newest = null;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT COUNT(*), MIN([TimeStamp]), MAX([TimeStamp]) FROM [{table}]";
            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            total  = reader.GetInt32(0);
            oldest = reader.IsDBNull(1) ? null : reader.GetDateTime(1);
            newest = reader.IsDBNull(2) ? null : reader.GetDateTime(2);
        }

        var top = new List<ErrorLogTopMessage>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                SELECT TOP 10 Message, COUNT(*) AS Occurrences
                FROM [{table}]
                GROUP BY Message
                ORDER BY COUNT(*) DESC
                """;
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                top.Add(new ErrorLogTopMessage(
                    reader.IsDBNull(0) ? "(no message)" : reader.GetString(0),
                    reader.GetInt32(1)));
        }

        return Ok(new ErrorLogSummary(total, oldest, newest, top));
    }

    // ── GET /api/admin/error-logs/sources ─────────────────────────────────────

    /// <summary>Distinct Source values, for the filter.</summary>
    [HttpGet("sources")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetSources(CancellationToken ct)
    {
        var table = ResolveTable();
        if (table is null) return Ok(Array.Empty<string>());

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);

        var sources = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT DISTINCT Source FROM [{table}] WHERE Source IS NOT NULL ORDER BY Source";
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) sources.Add(reader.GetString(0));
        return Ok(sources);
    }

    /// <summary>A parameter cannot be attached to two commands, so each gets its own copy.</summary>
    private static SqlParameter Clone(SqlParameter source)
        => new(source.ParameterName, source.Value);
}
