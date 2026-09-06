namespace Ben.Video.Editor.Services;

/// <summary>What kind of thing went wrong inside the video engine.</summary>
public enum WorkerFailureKind
{
    /// <summary>
    /// The command failed but the engine is still alive — a bad argument, a missing file, an
    /// unsupported codec. Fix the input and try again.
    /// </summary>
    Recoverable,

    /// <summary>
    /// The WebAssembly instance trapped. Nothing else will run on it until it is reloaded.
    /// </summary>
    Crashed,

    /// <summary>
    /// The engine ran out of room. Reloading it will not help with the same file; the work is
    /// simply larger than a browser tab can hold.
    /// </summary>
    OutOfMemory,
}

/// <summary>
/// Reads an ffmpeg.wasm failure and says whether the engine is still usable.
/// </summary>
/// <remarks>
/// <para>Every failure was treated the same: the state went to Error and stayed there until
/// somebody pressed Initialize again. Nothing said that had happened, so after a crash the editor
/// went quiet — the preview stopped refreshing, exports refused to start, and the only clue was a
/// status chip most people never look at (2026-09-05 audit, F7).</para>
///
/// <para>The three cases need three different answers. A bad command is nothing to worry about. A
/// trap means the engine has to be reloaded, which the editor can do by itself. Running out of
/// memory means reloading will not help, and the honest thing is to say the file is too big for
/// the browser and point at the helper that has no such limit.</para>
///
/// <para>Pure, because "what does this error message mean" is exactly the kind of thing that
/// should be checkable against real captured messages without a browser.</para>
/// </remarks>
public static class WorkerFailureClassifier
{
    /// <summary>
    /// Phrases emscripten uses when the heap cannot grow. These are the ones worth telling apart,
    /// because no amount of restarting fixes them.
    /// </summary>
    private static readonly string[] OutOfMemorySignals =
    [
        "cannot enlarge memory",
        "out of memory",
        "oom",
        "allocation failed",
        "memory allocation of",
    ];

    /// <summary>
    /// A trapped WebAssembly instance. The instance is gone; a new one is the only way forward.
    /// </summary>
    private static readonly string[] CrashSignals =
    [
        "runtimeerror",
        "memory access out of bounds",
        "unreachable",
        "null function or function signature mismatch",
        "table index is out of bounds",
        "index out of bounds",
        "abort(",
        "aborted(",
    ];

    /// <summary>Classifies a failure from its message.</summary>
    /// <param name="message">The exception message, or the engine's own log tail.</param>
    public static WorkerFailureKind Classify(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return WorkerFailureKind.Recoverable;

        var text = message.ToLowerInvariant();

        // Out of memory is checked first because its messages often arrive wrapped in a trap:
        // "Aborted(Cannot enlarge memory arrays)" is both, and the memory half is the one that
        // decides what to tell the person.
        foreach (var signal in OutOfMemorySignals)
            if (text.Contains(signal, StringComparison.Ordinal)) return WorkerFailureKind.OutOfMemory;

        foreach (var signal in CrashSignals)
            if (text.Contains(signal, StringComparison.Ordinal)) return WorkerFailureKind.Crashed;

        return WorkerFailureKind.Recoverable;
    }

    /// <summary>Whether the engine has to be reloaded before anything else will run.</summary>
    public static bool NeedsReload(WorkerFailureKind kind) =>
        kind is WorkerFailureKind.Crashed or WorkerFailureKind.OutOfMemory;

    /// <summary>
    /// What to tell the person, in their terms rather than the engine's.
    /// </summary>
    /// <param name="sidecarAvailable">
    /// Whether this host can offer the native helper, which runs outside the browser's memory
    /// limits and is the actual answer to a file that is too big.
    /// </param>
    public static string Explain(WorkerFailureKind kind, bool sidecarAvailable) => kind switch
    {
        WorkerFailureKind.OutOfMemory when sidecarAvailable =>
            "This is more than the browser's video engine can hold in memory. The native helper "
            + "runs outside that limit — pair it from the toolbar and try again.",

        WorkerFailureKind.OutOfMemory =>
            "This is more than the browser's video engine can hold in memory. Try a shorter "
            + "selection, or a smaller export resolution.",

        WorkerFailureKind.Crashed =>
            "The video engine stopped and is being restarted. Your project is untouched.",

        _ => "That step failed. The video engine is still running, so you can change the settings "
           + "and try again.",
    };
}
