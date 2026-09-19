namespace Ben.Video.Core.Services;

/// <summary>
/// What a keystroke means to the editor.
/// </summary>
/// <remarks>
/// Named for the intent rather than the key, because the same intent has more than one chord
/// (Redo is both Ctrl+Shift+Z and Ctrl+Y) and the same key means different things with a
/// modifier held.
/// </remarks>
public enum EditorCommand
{
    /// <summary>Nothing in the editor claims this keystroke.</summary>
    None = 0,

    TogglePlayPause,

    /// <summary>Remove whatever is selected — a clip, a title, a transition, a multi-selection.</summary>
    DeleteSelection,

    /// <summary>
    /// The four arrows. Each means "nudge the selected canvas layer" when something on the canvas
    /// is selected and "step a frame" when nothing is, which is a question about the current
    /// selection rather than about the keystroke, so the caller resolves it.
    /// </summary>
    MoveLeft,
    MoveRight,
    MoveUp,
    MoveDown,

    Rewind,
    SeekToEnd,

    /// <summary>Shift a multi-selection one second earlier / later.</summary>
    NudgeSelectionEarlier,
    NudgeSelectionLater,

    SplitAtPlayhead,
    ClearSelection,
    Duplicate,
    Undo,
    Redo,
    AddMarker,
    ToggleHelp,
}

/// <summary>
/// The editor's whole keyboard decision table, as a function of the keystroke alone.
/// </summary>
/// <remarks>
/// <para>This lived as a fifteen-case switch inside <c>VideoEditor.OnEditorKeyDown</c>, mixed in
/// with the work each shortcut does, so no test could ask what a key means without a browser —
/// and the audit's own list of broken shortcuts (Delete ignoring titles and transitions, arrows
/// never stepping frames, Escape missing three selection kinds) is exactly the class of thing a
/// table like this makes checkable (2026-09-05 audit, phase 11).</para>
///
/// <para>The modifier the browser reports is Ctrl on Windows and Cmd on macOS; the JS layer folds
/// <c>metaKey</c> into <c>ctrl</c> before it gets here, so this table never has to know which
/// machine it is on.</para>
/// </remarks>
public static class EditorKeyMap
{
    /// <summary>
    /// Resolves a keystroke to the command it invokes, or <see cref="EditorCommand.None"/>.
    /// </summary>
    /// <param name="key">The browser's <c>KeyboardEvent.key</c>.</param>
    /// <param name="ctrl">Ctrl on Windows, Cmd on macOS — the JS layer folds the two together.</param>
    /// <param name="shift">Shift held.</param>
    /// <param name="alt">Alt/Option held.</param>
    public static EditorCommand Resolve(string? key, bool ctrl, bool shift, bool alt) => key switch
    {
        // "Spacebar" is the old IE/Edge name, still reported by a few remotes and presenters.
        " " or "Spacebar" => EditorCommand.TogglePlayPause,

        "Delete" or "Backspace" => EditorCommand.DeleteSelection,

        "ArrowLeft"  => EditorCommand.MoveLeft,
        "ArrowRight" => EditorCommand.MoveRight,
        "ArrowUp"    => EditorCommand.MoveUp,
        "ArrowDown"  => EditorCommand.MoveDown,

        // Ctrl+Home/End belong to the browser (and to text fields that have focus elsewhere).
        "Home" when !ctrl => EditorCommand.Rewind,
        "End"  when !ctrl => EditorCommand.SeekToEnd,

        "[" when !ctrl => EditorCommand.NudgeSelectionEarlier,
        "]" when !ctrl => EditorCommand.NudgeSelectionLater,

        // Ctrl+S is Save and belongs to the toolbar, not to Split.
        "s" or "S" when !ctrl => EditorCommand.SplitAtPlayhead,

        "Escape" => EditorCommand.ClearSelection,

        "d" or "D" when ctrl => EditorCommand.Duplicate,

        "z" or "Z" when ctrl && !shift => EditorCommand.Undo,
        "z" or "Z" when ctrl && shift  => EditorCommand.Redo,
        "y" or "Y" when ctrl           => EditorCommand.Redo,

        // A bare M only. Alt+M and the Ctrl combinations are menu accelerators on some platforms,
        // and dropping a marker because somebody reached for one would be hard to explain.
        "m" or "M" when !ctrl && !shift && !alt => EditorCommand.AddMarker,

        "?" => EditorCommand.ToggleHelp,

        _ => EditorCommand.None,
    };
}
