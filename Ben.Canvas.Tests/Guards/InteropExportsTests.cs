using System.Text.RegularExpressions;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Guards;

/// <summary>
/// Every JS function C# calls on a canvas module exists in that module.
/// </summary>
/// <remarks>
/// A renamed export is invisible to the compiler: the call fails at runtime with a JSException that
/// names the function, usually inside a render, where it blanks the editor. Later milestones add their
/// modules to <see cref="Exports"/> rather than creating new guard files.
/// </remarks>
public sealed class InteropExportsTests
{
    /// <summary>Module path (as passed to CanvasModules.ImportAsync) to the functions it exports.</summary>
    public static readonly Dictionary<string, string[]> Exports = new(StringComparer.Ordinal)
    {
        ["js/modalInterop.js"] = ["open", "close"],
        ["js/boardGestures.js"] = ["attach", "detach", "setViewport", "getViewport", "cancel", "attachSheet", "detachSheet", "setReadOnly"],
        ["js/keyboardInterop.js"] = ["register", "unregister"],
        ["js/storageInterop.js"] = ["getItem", "setItem", "removeItem", "docPut", "docGet", "docDelete", "persist"],
        ["js/opfsInterop.js"] = ["isAvailable", "assetWrite", "assetWriteBytes", "assetExists", "assetUrl", "assetRevoke", "assetRevokeAll", "assetList", "assetDelete", "estimate", "readBlob"],
        ["js/pasteInterop.js"] = ["attach", "detach"],
        ["js/domInterop.js"] = ["setUnloadGuard", "flushOnPageHide", "stopFlushOnPageHide", "isCoarsePointer", "downloadUrl", "revokeUrlLater", "clickElement", "watchMedia", "unwatchMedia"],
        ["js/packageInterop.js"] = ["exportPackage", "importPackage"],
        ["js/blobInterop.js"] = ["fetchAsObjectUrl", "uploadFromUrl", "revokeObjectUrl"],
        ["js/snapshotInterop.js"] = ["drawBoard"],
        ["js/mapInterop.js"] = ["mount", "unmount"],
    };

    /// <summary>Scripts that call back into .NET, and the service whose [JSInvokable] methods they may name.</summary>
    private static readonly Dictionary<string, Type[]> Callbacks = new(StringComparer.Ordinal)
    {
        ["js/boardGestures.js"] = [typeof(Ben.Canvas.Editor.Services.BoardGestureBridge), typeof(Ben.Canvas.Editor.Services.CanvasLayoutState)],
        ["js/keyboardInterop.js"] = [typeof(Ben.Canvas.Editor.Services.KeyboardShortcutService)],
        ["js/pasteInterop.js"] = [typeof(Ben.Canvas.Editor.Services.PasteService)],
        ["js/domInterop.js"] = [typeof(Ben.Canvas.Editor.Services.UnloadGuardService), typeof(Ben.Canvas.Editor.Services.CanvasLayoutState)],
    };

    /// <summary>
    /// A callback name the script invokes but no [JSInvokable] method carries fails only when that gesture
    /// ends in a browser - the drag appears to work and then nothing is saved.
    /// </summary>
    [Fact]
    public void Every_invokeMethodAsync_name_matches_a_JSInvokable()
    {
        var missing = new List<string>();

        foreach (var (module, services) in Callbacks)
        {
            var invokable = services.SelectMany(s => s.GetMethods())
                .Where(m => m.GetCustomAttributes(typeof(Microsoft.JSInterop.JSInvokableAttribute), false).Length > 0)
                .Select(m => m.Name)
                .ToHashSet(StringComparer.Ordinal);

            var text = RepoFiles.ReadWithoutComments(Path.Combine(RepoFiles.EditorWwwroot(), module.Replace('/', Path.DirectorySeparatorChar)));
            var names = Regex.Matches(text, @"invokeMethod(?:Async)?\(\s*'(\w+)'").Select(m => m.Groups[1].Value)
                .Concat(Regex.Matches(text, @"invoke(?:Result)?\(\s*state\s*,\s*'(\w+)'").Select(m => m.Groups[1].Value))
                .Distinct();

            foreach (var name in names)
                if (!invokable.Contains(name))
                    missing.Add($"{module}: {name}");
        }

        Assert.True(missing.Count == 0, "These callbacks have no [JSInvokable] method:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void Every_listed_module_exports_its_functions()
    {
        var missing = new List<string>();

        foreach (var (module, functions) in Exports)
        {
            var path = Path.Combine(RepoFiles.EditorWwwroot(), module.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) { missing.Add($"{module}: file missing"); continue; }

            var text = RepoFiles.ReadWithoutComments(path);
            foreach (var fn in functions)
                if (!Regex.IsMatch(text, $@"export\s+(async\s+)?function\s+{Regex.Escape(fn)}\s*\("))
                    missing.Add($"{module}: export {fn}");
        }

        Assert.True(missing.Count == 0, "These exports are listed but not present:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void Every_imported_module_is_listed()
    {
        var unlisted = new List<string>();

        foreach (var file in RepoFiles.UiFiles("*.cs", "*.razor"))
            foreach (Match m in Regex.Matches(RepoFiles.ReadWithoutComments(file), @"CanvasModules\.ImportAsync\([^,]+,\s*""([^""]+)""\)"))
                if (!Exports.ContainsKey(m.Groups[1].Value))
                    unlisted.Add($"{RepoFiles.Relative(file)}: {m.Groups[1].Value}");

        Assert.True(unlisted.Count == 0,
            "These modules are imported but not listed in InteropExportsTests.Exports, so their calls are "
            + "unchecked:\n  " + string.Join("\n  ", unlisted));
    }

    [Fact]
    public void Call_sites_only_invoke_listed_functions()
    {
        var offenders = new List<string>();

        foreach (var file in RepoFiles.UiFiles("*.cs", "*.razor"))
        {
            var text = RepoFiles.ReadWithoutComments(file);
            var modules = Regex.Matches(text, @"CanvasModules\.ImportAsync\([^,]+,\s*""([^""]+)""\)")
                .Select(m => m.Groups[1].Value)
                .Where(Exports.ContainsKey)
                .ToList();
            if (modules.Count == 0) continue;

            var allowed = modules.SelectMany(m => Exports[m]).ToHashSet(StringComparer.Ordinal);

            foreach (Match call in Regex.Matches(text, @"_module[?!]?\.Invoke(Void)?Async(<.*?>)?\(\s*""([^""]+)"""))
                if (!allowed.Contains(call.Groups[3].Value))
                    offenders.Add($"{RepoFiles.Relative(file)}: {call.Groups[3].Value}");
        }

        Assert.True(offenders.Count == 0, "These calls name functions their module does not export:\n  " + string.Join("\n  ", offenders));
    }
}
