using Serilog.Events;

namespace Ben.Data.WebApi.Logging;

/// <summary>
/// Log entries that are written despite a decision, elsewhere, that they should not be.
/// </summary>
public static class LogNoise
{
    private const string FrameworkHandler = "ExceptionHandlerMiddleware";

    /// <summary>
    /// The framework's Error copy of a missing stored file, which the exception handler has already
    /// decided is a 404 and a Warning.
    /// </summary>
    /// <remarks>
    /// <para>ASP.NET Core's <c>ExceptionHandlerMiddleware</c> logs at Error, with a stack trace,
    /// BEFORE it invokes the registered handler. So the handler's careful downgrade never took
    /// effect — the Error was already written, and the Warning landed underneath it.</para>
    ///
    /// <para>Measured on 2026-08-31: 1,978 of 2,022 rows in <c>Logs</c> — 96% — were this, and
    /// 1,934 of those stood for THREE files. A log in that state cannot show a real fault, which
    /// is the exact outcome the handler set out to avoid.</para>
    ///
    /// <para><b>Every clause is load-bearing</b>, and the tests hold each one: the same exception
    /// from anywhere else still logs at Error, because only this middleware duplicates a decision
    /// already made; a different exception from this middleware still logs, because only the
    /// missing-file case has a handler that downgrades it; and a Warning is never touched, because
    /// the Warning IS the record being kept.</para>
    /// </remarks>
    public static bool IsDuplicateOfAHandledMissingFile(LogEvent e)
        => e.Level == LogEventLevel.Error
        && e.Exception is FileNotFoundException or DirectoryNotFoundException
        && e.Properties.TryGetValue("SourceContext", out var source)
        && source.ToString().Contains(FrameworkHandler, StringComparison.Ordinal);
}
