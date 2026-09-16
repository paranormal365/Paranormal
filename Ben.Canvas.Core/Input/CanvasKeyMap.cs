namespace Ben.Canvas.Core.Input;

/// <summary>What a key press asks the board to do, named for intent rather than for the key.</summary>
public enum CanvasCommand
{
    None,
    DeleteSelection,
    MoveLeft,
    MoveRight,
    MoveUp,
    MoveDown,
    ClearSelection,
    SelectAll,
    Duplicate,
    Copy,
    Cut,
    Undo,
    Redo,
    ZoomIn,
    ZoomOut,
    ZoomReset,
    FitToContent,
    Group,
    Ungroup,
    ToggleLock,
    BringToFront,
    SendToBack,
    Save,
    ToggleHelp,
    EditSelected,
    Rename,
    ResizeMode,
    ConnectMode,
    AddText,
    AddCard,
    AddMessage,
    AddImage,
    AddLink,
    NextConnector,
    PreviousConnector,

    // Ben, 2026-09-16: "Can we make it like Miro?" — the next block, already joined to this one.
    GrowLeft,
    GrowRight,
    GrowUp,
    GrowDown,

    /// <summary>Walk the board a card at a time, for showing it to somebody.</summary>
    Present,
}

/// <summary>
/// The board's keyboard shortcuts.
/// </summary>
/// <remarks>
/// <para>Copied in shape from the video editor's EditorKeyMap. The browser script folds Cmd into Ctrl before
/// calling this, so one table serves Windows and Mac.</para>
///
/// <para>What is deliberately left to the browser: Ctrl+V (paste arrives as the document paste event, which
/// carries the clipboard; a key handler does not), Ctrl+1..8 and Ctrl+L (Chrome and Edge keep them for tabs
/// and the address bar, and a page cannot take them back), Ctrl+E (there is no export chord), Space (panning
/// lives in the gesture module), Tab (native focus order) and anything with Alt.</para>
///
/// <para>Bare letters act only while the board itself has focus, so typing an R into a card never resizes it.
/// </para>
/// </remarks>
public static class CanvasKeyMap
{
    public static CanvasCommand Resolve(string? key, bool ctrl, bool shift, bool alt, bool boardFocused)
    {
        if (string.IsNullOrEmpty(key) || alt) return CanvasCommand.None;

        if (ctrl)
        {
            // Ctrl+Shift+Arrow, not Alt+Arrow: Alt+Left and Alt+Right are Back and Forward in Chrome
            // on Windows, and the site's own browsers are Windows ones.
            if (shift)
            {
                switch (key)
                {
                    case "ArrowLeft": return CanvasCommand.GrowLeft;
                    case "ArrowRight": return CanvasCommand.GrowRight;
                    case "ArrowUp": return CanvasCommand.GrowUp;
                    case "ArrowDown": return CanvasCommand.GrowDown;
                }
            }

            return Lower(key) switch
            {
                "a" when !shift => CanvasCommand.SelectAll,
                "c" when !shift => CanvasCommand.Copy,
                "x" when !shift => CanvasCommand.Cut,
                "d" when !shift => CanvasCommand.Duplicate,
                "s" when !shift => CanvasCommand.Save,
                "z" when !shift => CanvasCommand.Undo,
                "z" => CanvasCommand.Redo,
                "y" when !shift => CanvasCommand.Redo,
                "=" or "+" => CanvasCommand.ZoomIn,
                "-" or "_" => CanvasCommand.ZoomOut,
                "0" when !shift => CanvasCommand.ZoomReset,
                "g" when !shift => CanvasCommand.Group,
                "g" => CanvasCommand.Ungroup,
                "l" when shift => CanvasCommand.ToggleLock,
                "]" or "}" => CanvasCommand.BringToFront,
                "[" or "{" => CanvasCommand.SendToBack,
                _ => CanvasCommand.None,
            };
        }

        switch (key)
        {
            case "Delete" or "Backspace": return CanvasCommand.DeleteSelection;
            case "ArrowLeft": return CanvasCommand.MoveLeft;
            case "ArrowRight": return CanvasCommand.MoveRight;
            case "ArrowUp": return CanvasCommand.MoveUp;
            case "ArrowDown": return CanvasCommand.MoveDown;
            case "Escape": return CanvasCommand.ClearSelection;
            case "Enter": return CanvasCommand.EditSelected;
            case "F2": return CanvasCommand.Rename;
            case "Home": return CanvasCommand.FitToContent;
            case "1" or "!" when shift: return CanvasCommand.FitToContent;
        }

        if (!boardFocused) return CanvasCommand.None;

        return key switch
        {
            "?" => CanvasCommand.ToggleHelp,
            "]" => CanvasCommand.NextConnector,
            "[" => CanvasCommand.PreviousConnector,
            _ => Lower(key) switch
            {
                "r" => CanvasCommand.ResizeMode,
                "c" => CanvasCommand.ConnectMode,
                "t" => CanvasCommand.AddText,
                "n" => CanvasCommand.AddCard,
                "m" => CanvasCommand.AddMessage,
                "i" => CanvasCommand.AddImage,
                "l" => CanvasCommand.AddLink,
                "p" => CanvasCommand.Present,
                _ => CanvasCommand.None,
            },
        };
    }

    private static string Lower(string key) => key.Length == 1 ? key.ToLowerInvariant() : key;
}
