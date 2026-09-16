using Ben.Canvas.Core.Input;

namespace Ben.Canvas.Tests.Input;

/// <summary>
/// The keyboard map claims the chords the board uses and leaves everything else - including the chords
/// browsers keep for themselves - to the browser.
/// </summary>
public sealed class CanvasKeyMapTests
{
    private static CanvasCommand Key(string key, bool ctrl = false, bool shift = false, bool alt = false, bool board = true) =>
        CanvasKeyMap.Resolve(key, ctrl, shift, alt, board);

    [Theory]
    [InlineData("Delete", false, false, CanvasCommand.DeleteSelection)]
    [InlineData("Backspace", false, false, CanvasCommand.DeleteSelection)]
    [InlineData("ArrowLeft", false, false, CanvasCommand.MoveLeft)]
    [InlineData("ArrowRight", false, true, CanvasCommand.MoveRight)]
    [InlineData("ArrowUp", false, false, CanvasCommand.MoveUp)]
    [InlineData("ArrowDown", false, false, CanvasCommand.MoveDown)]
    [InlineData("Escape", false, false, CanvasCommand.ClearSelection)]
    [InlineData("Enter", false, false, CanvasCommand.EditSelected)]
    [InlineData("F2", false, false, CanvasCommand.Rename)]
    [InlineData("Home", false, false, CanvasCommand.FitToContent)]
    [InlineData("a", true, false, CanvasCommand.SelectAll)]
    [InlineData("c", true, false, CanvasCommand.Copy)]
    [InlineData("x", true, false, CanvasCommand.Cut)]
    [InlineData("d", true, false, CanvasCommand.Duplicate)]
    [InlineData("s", true, false, CanvasCommand.Save)]
    [InlineData("z", true, false, CanvasCommand.Undo)]
    [InlineData("=", true, false, CanvasCommand.ZoomIn)]
    [InlineData("+", true, true, CanvasCommand.ZoomIn)]
    [InlineData("-", true, false, CanvasCommand.ZoomOut)]
    [InlineData("0", true, false, CanvasCommand.ZoomReset)]
    [InlineData("g", true, false, CanvasCommand.Group)]
    [InlineData("G", true, true, CanvasCommand.Ungroup)]
    public void Each_chord_resolves_to_its_command(string key, bool ctrl, bool shift, CanvasCommand expected) =>
        Assert.Equal(expected, Key(key, ctrl, shift));

    [Fact]
    public void Ctrl_Shift_Z_is_redo() => Assert.Equal(CanvasCommand.Redo, Key("Z", ctrl: true, shift: true));

    [Fact]
    public void Ctrl_Y_is_redo() => Assert.Equal(CanvasCommand.Redo, Key("y", ctrl: true));

    [Fact]
    public void Ctrl_S_is_save_not_the_browser_dialog() => Assert.Equal(CanvasCommand.Save, Key("S", ctrl: true));

    [Theory]
    [InlineData("1")]
    [InlineData("!")]
    public void Shift_1_fits_with_either_key_value(string key) => Assert.Equal(CanvasCommand.FitToContent, Key(key, shift: true, board: false));

    [Fact]
    public void Ctrl_Shift_L_locks() => Assert.Equal(CanvasCommand.ToggleLock, Key("L", ctrl: true, shift: true));

    [Fact]
    public void Ctrl_close_bracket_brings_to_front() => Assert.Equal(CanvasCommand.BringToFront, Key("]", ctrl: true));

    [Fact]
    public void Ctrl_open_bracket_sends_to_back() => Assert.Equal(CanvasCommand.SendToBack, Key("[", ctrl: true));

    [Theory]
    [InlineData("r", CanvasCommand.ResizeMode)]
    [InlineData("c", CanvasCommand.ConnectMode)]
    [InlineData("t", CanvasCommand.AddText)]
    [InlineData("n", CanvasCommand.AddCard)]
    [InlineData("m", CanvasCommand.AddMessage)]
    [InlineData("i", CanvasCommand.AddImage)]
    [InlineData("l", CanvasCommand.AddLink)]
    [InlineData("]", CanvasCommand.NextConnector)]
    [InlineData("[", CanvasCommand.PreviousConnector)]
    [InlineData("?", CanvasCommand.ToggleHelp)]
    public void Bare_keys_act_when_the_board_is_focused(string key, CanvasCommand expected) => Assert.Equal(expected, Key(key));

    [Theory]
    [InlineData("r")]
    [InlineData("c")]
    [InlineData("t")]
    [InlineData("n")]
    [InlineData("m")]
    [InlineData("i")]
    [InlineData("l")]
    [InlineData("]")]
    [InlineData("[")]
    [InlineData("?")]
    public void Bare_letters_do_nothing_when_the_board_is_not_focused(string key) => Assert.Equal(CanvasCommand.None, Key(key, board: false));

    [Fact]
    public void Ctrl_V_is_not_a_command_here() => Assert.Equal(CanvasCommand.None, Key("v", ctrl: true));

    [Theory]
    [InlineData("1")]
    [InlineData("8")]
    [InlineData("l")]
    public void Ctrl_1_and_Ctrl_L_are_left_to_the_browser(string key) => Assert.Equal(CanvasCommand.None, Key(key, ctrl: true));

    [Fact]
    public void Ctrl_E_is_not_export() => Assert.Equal(CanvasCommand.None, Key("e", ctrl: true));

    [Fact]
    public void Space_is_not_a_command_here() => Assert.Equal(CanvasCommand.None, Key(" "));

    [Fact]
    public void Alt_combinations_are_left_to_the_system() => Assert.Equal(CanvasCommand.None, Key("z", ctrl: true, alt: true));

    [Theory]
    [InlineData("Tab")]
    [InlineData("q")]
    [InlineData("PageDown")]
    [InlineData("")]
    public void Everything_else_is_left_to_the_browser(string key) => Assert.Equal(CanvasCommand.None, Key(key));

    // ── The script's side of the contract ───────────────────────────────

    private static string Script() =>
        Support.RepoFiles.ReadWithoutComments(Path.Combine(Support.RepoFiles.EditorWwwroot(), "js", "keyboardInterop.js"));

    [Fact]
    public void The_listener_folds_Cmd_into_the_modifier_it_forwards() =>
        Assert.Contains("e.ctrlKey || e.metaKey", Script());

    /// <summary>A key the script stops the browser from handling must do something here, or it is simply swallowed.</summary>
    [Fact]
    public void Every_key_the_listener_suppresses_is_one_the_editor_handles()
    {
        var script = Script();
        var bare = ListLiterals(script, "BOARD_KEYS");
        var ctrl = ListLiterals(script, "CTRL_KEYS");

        var dead = bare.Where(k => Key(k) == CanvasCommand.None).Select(k => "bare " + k)
            .Concat(ctrl.Where(k => Key(k, ctrl: true) == CanvasCommand.None && Key(k, ctrl: true, shift: true) == CanvasCommand.None).Select(k => "ctrl " + k))
            .ToList();

        Assert.True(dead.Count == 0, "The script claims keys the editor ignores: " + string.Join(", ", dead));
    }

    [Fact]
    public void Bare_keys_are_claimed_only_with_the_board_focused() =>
        Assert.Matches(@"onBoard\s*&&\s*!ctrl\s*&&\s*!e\.altKey\s*&&\s*BOARD_KEYS\.includes", Script());

    [Fact]
    public void Space_is_left_to_the_gesture_module() =>
        Assert.Matches(@"e\.key === ' ' \|\| e\.key === 'Spacebar'\) return", Script());

    [Fact]
    public void Shift_1_is_normalised_to_1() => Assert.Contains("Digit1", Script());

    [Fact]
    public void Ctrl_v_is_never_claimed()
    {
        var ctrl = ListLiterals(Script(), "CTRL_KEYS");
        Assert.DoesNotContain("v", ctrl);
        Assert.DoesNotContain("V", ctrl);
    }

    private static List<string> ListLiterals(string script, string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(script, name + @"\s*=\s*\[([^\]]*)\]");
        Assert.True(m.Success, $"{name} was not found in keyboardInterop.js.");
        return System.Text.RegularExpressions.Regex.Matches(m.Groups[1].Value, @"'([^']*)'").Select(x => x.Groups[1].Value).ToList();
    }
}
