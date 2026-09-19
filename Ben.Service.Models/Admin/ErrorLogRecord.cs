namespace Ben.Service.Models.Admin;

/// <summary>One row of Serilog's error table, as the admin page shows it.</summary>
/// <remarks>
/// Deliberately not an EF entity: Serilog creates and owns this table (autoCreateSqlTable), and
/// mapping it would make the application's model responsible for a schema the logger controls.
/// It is read with SQL and projected here.
/// </remarks>
public sealed record ErrorLogRecord(
    long Id,
    DateTime TimeStamp,
    string? Level,
    string? Message,
    string? Exception,
    string? Source,
    string? Application,
    /// <summary>The request path, lifted out of Serilog's Properties XML where present.</summary>
    string? RequestPath);

/// <summary>A page of error rows, with the total so the grid can page properly.</summary>
public sealed record ErrorLogPagedResponse(IReadOnlyList<ErrorLogRecord> Items, int TotalCount);

/// <summary>
/// What the table looks like in aggregate — which messages dominate, and how far back it goes.
/// </summary>
/// <remarks>
/// This exists because of what item 202 found: the table was 96% one repeated message, which made
/// it useless for finding a real fault while looking perfectly healthy row by row. A count and a
/// date range answer "is this log telling me anything?" before anyone reads a single entry.
/// </remarks>
public sealed record ErrorLogSummary(
    int TotalRows,
    DateTime? OldestUtc,
    DateTime? NewestUtc,
    IReadOnlyList<ErrorLogTopMessage> TopMessages);

/// <summary>A recurring message and how much of the table it accounts for.</summary>
public sealed record ErrorLogTopMessage(string Message, int Count);
