using System.Globalization;
using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Commands;
using Ben.Canvas.Core.Model;

namespace Ben.Canvas.Tests.Support;

/// <summary>Small boards and blocks for the engine tests.</summary>
public static class TestBoards
{
    public static CanvasNode Node(CanvasNodeType type = CanvasNodeType.Card, double x = 0, double y = 0, double? width = null, double? height = null)
    {
        var descriptor = BlockRegistry.Get(type);
        return new CanvasNode
        {
            Type = type,
            X = x,
            Y = y,
            Width = width ?? descriptor.DefaultWidth,
            Height = height ?? descriptor.DefaultHeight,
            Data = descriptor.CreateDefaultData(new DateTime(2026, 9, 14, 18, 0, 0, DateTimeKind.Utc)),
        };
    }

    public static CanvasStore Store(int historyDepth = 50, int maxNodes = 2000) => new() { HistoryDepth = historyDepth, MaxNodes = maxNodes };

    /// <summary>A store holding the given blocks, added through the store so they have paint order.</summary>
    public static CanvasStore StoreWith(params CanvasNode[] nodes)
    {
        var store = Store();
        var document = new CanvasDocument();
        foreach (var node in nodes)
        {
            node.Z = document.NextZ++;
            document.Nodes.Add(node);
        }

        store.Load(document);
        return store;
    }

    /// <summary>Runs <paramref name="body"/> with the current culture set, restoring it afterwards.</summary>
    public static void InCulture(string name, Action body)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    public static readonly byte[] PngHead = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];
    public static readonly byte[] JpegHead = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1];
    public static readonly byte[] HeicHead = [0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x69, 0x63];
    public static readonly byte[] PdfHead = "%PDF-1.4\n%xx"u8.ToArray();
}
