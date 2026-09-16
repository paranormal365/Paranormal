using System.Text.Json;
using Microsoft.JSInterop;

namespace Ben.Canvas.Tests.Support;

/// <summary>
/// A JS runtime whose <c>benImportCanvasModule</c> hands out in-memory stand-ins for the editor's modules.
/// </summary>
/// <remarks>
/// Each stand-in implements the same function names as the real module (InteropExportsTests pins those), so a
/// service under test talks to it exactly as it talks to the browser. Answers are converted through JSON the
/// way the real interop converts them, so a DTO record that would not bind in the browser does not bind here.
/// </remarks>
public sealed class FakeModuleJs : IJSRuntime
{
    public Dictionary<string, FakeModule> Modules { get; } = new(StringComparer.Ordinal);

    public List<string> Imports { get; } = [];

    public FakeModule Module(string path)
    {
        if (!Modules.TryGetValue(path, out var module)) Modules[path] = module = new FakeModule(path);
        return module;
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        if (identifier != "benImportCanvasModule") throw new InvalidOperationException($"Unexpected global call {identifier}.");
        var path = (string)args![0]!;
        Imports.Add(path);
        return ValueTask.FromResult((TValue)(object)Module(path));
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args) => InvokeAsync<TValue>(identifier, args);
}

/// <summary>One module stand-in: a function table and a record of every call.</summary>
public sealed class FakeModule(string path) : IJSObjectReference
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public string Path { get; } = path;

    public Dictionary<string, Func<object?[], object?>> Functions { get; } = new(StringComparer.Ordinal);

    public List<(string Name, object?[] Args)> Calls { get; } = [];

    public int CountOf(string name) => Calls.Count(c => c.Name == name);

    public FakeModule On(string name, Func<object?[], object?> body)
    {
        Functions[name] = body;
        return this;
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        args ??= [];
        Calls.Add((identifier, args));
        if (!Functions.TryGetValue(identifier, out var body))
            throw new JSException($"{Path} has no export named {identifier}.");

        var result = body(args);
        if (result is Task task) throw new InvalidOperationException("Return values, not tasks.");
        if (result is null) return ValueTask.FromResult(default(TValue)!);
        if (result is TValue typed) return ValueTask.FromResult(typed);
        var json = JsonSerializer.Serialize(result, Web);
        return ValueTask.FromResult(JsonSerializer.Deserialize<TValue>(json, Web)!);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>storageInterop.js in memory: localStorage items and IndexedDB board bodies.</summary>
public sealed class FakeStorage
{
    public Dictionary<string, string> Items { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Docs { get; } = new(StringComparer.Ordinal);
    public bool RefuseDocWrites { get; set; }
    public bool Persisted { get; set; } = true;
    public int DocWrites { get; private set; }

    public FakeStorage(FakeModule module)
    {
        module
            .On("getItem", a => Items.GetValueOrDefault((string)a[0]!))
            .On("setItem", a => { Items[(string)a[0]!] = (string)a[1]!; return true; })
            .On("removeItem", a => Items.Remove((string)a[0]!))
            .On("docPut", a =>
            {
                if (RefuseDocWrites) return false;
                DocWrites++;
                Docs[(string)a[0]!] = (string)a[1]!;
                return true;
            })
            .On("docGet", a => Docs.GetValueOrDefault((string)a[0]!))
            .On("docDelete", a => Docs.Remove((string)a[0]!))
            .On("persist", _ => Persisted);
    }
}

/// <summary>opfsInterop.js in memory.</summary>
public sealed class FakeAssets
{
    public Dictionary<string, (long Size, double Modified)> Files { get; } = new(StringComparer.Ordinal);
    public List<string> Deleted { get; } = [];
    public string Backend { get; set; } = "opfs";
    public int UrlReads { get; private set; }

    public FakeAssets(FakeModule module)
    {
        module
            .On("isAvailable", _ => Backend)
            .On("assetUrl", a =>
            {
                UrlReads++;
                var name = (string)a[0]! + (string)a[1]!;
                return Files.ContainsKey(name) ? "blob:fake/" + name : null;
            })
            .On("assetList", _ => Files.Select(f => new { assetId = f.Key[..36], ext = f.Key[36..], size = f.Value.Size, modified = f.Value.Modified }).ToList())
            .On("assetDelete", a =>
            {
                var name = (string)a[0]! + (string)a[1]!;
                Deleted.Add(name);
                return Files.Remove(name);
            })
            .On("assetRevokeAll", _ => null);
    }

    public void Add(Guid id, string ext, DateTime modifiedUtc, long size = 10) =>
        Files[id.ToString("D") + ext] = (size, new DateTimeOffset(modifiedUtc).ToUnixTimeMilliseconds());
}
