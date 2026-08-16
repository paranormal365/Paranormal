namespace Ben.Video.Editor.Services;

/// <summary>
/// Scoped service that accumulates error messages from ffmpeg operations and JS interop.
/// When <see cref="Ben.Video.Editor.Models.VideoEditorOptions.ErrorLog"/> is <c>true</c>,
/// the File menu exposes an "Export Error Log" item that downloads the log as plain text.
/// </summary>
public sealed class ErrorLogService
{
    private readonly List<ErrorLogEntry> _entries = [];

    /// <summary>All entries accumulated in this session, newest last.</summary>
    public IReadOnlyList<ErrorLogEntry> Entries => _entries;

    /// <summary>Whether any entries have been logged.</summary>
    public bool HasEntries => _entries.Count > 0;

    // ── Logging ───────────────────────────────────────────────────────────────

    /// <summary>Append an entry to the log.</summary>
    public void Log(string source, string message, string? detail = null)
        => _entries.Add(new ErrorLogEntry(DateTime.Now, source, message, detail));

    /// <summary>Convenience overload for exceptions.</summary>
    public void Log(string source, Exception ex)
        => Log(source, ex.Message, ex.ToString());

    /// <summary>Remove all entries.</summary>
    public void Clear() => _entries.Clear();

    // ── Export ────────────────────────────────────────────────────────────────

    /// <summary>Formats all entries as a plain-text log string.</summary>
    public string ExportText()
    {
        if (_entries.Count == 0) return "No errors logged in this session.";
        return string.Join("\n\n", _entries.Select(e =>
        {
            var line = $"[{e.Timestamp:yyyy-MM-dd HH:mm:ss}] [{e.Source}]\n{e.Message}";
            return e.Detail is not null ? $"{line}\n{e.Detail}" : line;
        }));
    }
}

/// <summary>A single error log entry.</summary>
public sealed record ErrorLogEntry(
    DateTime Timestamp,
    string   Source,
    string   Message,
    string?  Detail);
