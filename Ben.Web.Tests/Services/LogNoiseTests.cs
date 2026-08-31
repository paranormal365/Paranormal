using System;
using System.Collections.Generic;
using System.IO;
using Ben.Data.WebApi.Logging;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The filter that stops one missing file writing 1,695 error rows.
/// </summary>
/// <remarks>
/// Each clause of the predicate gets a test that fails if that clause is removed, because a filter
/// is the kind of code that looks right while quietly swallowing more than it was meant to — and a
/// log filter that swallows too much destroys the evidence you would need to notice.
/// </remarks>
public class LogNoiseTests
{
    private static LogEvent Event(LogEventLevel level, Exception? ex, string sourceContext)
        => new(DateTimeOffset.UtcNow, level, ex,
               new MessageTemplate(Array.Empty<MessageTemplateToken>()),
               new List<LogEventProperty>
               {
                   new("SourceContext", new ScalarValue(sourceContext)),
               });

    private const string Middleware =
        "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware";

    [Fact]
    public void The_frameworks_duplicate_of_a_missing_file_is_excluded()
    {
        var e = Event(LogEventLevel.Error, new FileNotFoundException("gone"), Middleware);
        Assert.True(LogNoise.IsDuplicateOfAHandledMissingFile(e));
    }

    [Fact]
    public void A_missing_directory_counts_too()
    {
        var e = Event(LogEventLevel.Error, new DirectoryNotFoundException("gone"), Middleware);
        Assert.True(LogNoise.IsDuplicateOfAHandledMissingFile(e));
    }

    [Fact]
    public void The_same_exception_from_anywhere_else_still_logs()
    {
        // Only this middleware duplicates a decision already made. A FileNotFoundException raised
        // by, say, a background job is nobody's duplicate and must survive.
        var e = Event(LogEventLevel.Error, new FileNotFoundException("gone"),
                      "Ben.Data.WebApi.Services.Media.MediaIngestService");
        Assert.False(LogNoise.IsDuplicateOfAHandledMissingFile(e));
    }

    [Fact]
    public void A_different_exception_from_the_same_middleware_still_logs()
    {
        // The middleware's Error is only redundant where a handler downgrades it, and the handler
        // downgrades exactly one family. Everything else it reports is a real fault.
        var e = Event(LogEventLevel.Error, new InvalidOperationException("broken"), Middleware);
        Assert.False(LogNoise.IsDuplicateOfAHandledMissingFile(e));
    }

    [Fact]
    public void The_handlers_own_warning_survives()
    {
        // This is the record being KEPT. The database and the disk disagreeing is worth knowing,
        // and if this were filtered the fix would have hidden the condition instead of the noise.
        var e = Event(LogEventLevel.Warning, new FileNotFoundException("gone"), Middleware);
        Assert.False(LogNoise.IsDuplicateOfAHandledMissingFile(e));
    }

    [Fact]
    public void An_error_with_no_exception_at_all_still_logs()
    {
        var e = Event(LogEventLevel.Error, null, Middleware);
        Assert.False(LogNoise.IsDuplicateOfAHandledMissingFile(e));
    }
}
