using Ben.Canvas.Core.Model;

namespace Ben.Canvas.Core.Commands;

/// <summary>
/// One undoable change to the board.
/// </summary>
/// <remarks>
/// Only the store creates and runs these. A command built elsewhere and run directly would change the
/// board without an undo entry and without telling anything that the board changed.
/// </remarks>
public interface IEditorCommand
{
    void Execute();
    void Undo();

    /// <summary>What the change was, in words a person reads on the Undo button.</summary>
    string Description { get; }
}

/// <summary>A command that moves, resizes or rewrites particular blocks, so only those re-render.</summary>
internal interface ITouchesNodes
{
    IEnumerable<Guid> NodeIds { get; }
}

/// <summary>A command that keeps block data alive in history, so stored pictures it names are not swept.</summary>
internal interface IHoldsData
{
    IEnumerable<NodeData> HeldData { get; }
}

/// <summary>Several commands that undo as one.</summary>
internal sealed class CompositeCommand(string description, List<IEditorCommand> commands) : IEditorCommand, ITouchesNodes, IHoldsData
{
    public string Description { get; } = description;
    public IReadOnlyList<IEditorCommand> Commands => commands;

    public void Execute()
    {
        foreach (var command in commands) command.Execute();
    }

    public void Undo()
    {
        for (var i = commands.Count - 1; i >= 0; i--) commands[i].Undo();
    }

    public IEnumerable<Guid> NodeIds => commands.OfType<ITouchesNodes>().SelectMany(c => c.NodeIds);
    public IEnumerable<NodeData> HeldData => commands.OfType<IHoldsData>().SelectMany(c => c.HeldData);
}

/// <summary>What changed, so listeners can re-render only what they must.</summary>
public enum CanvasChangeKind { Document, NodeGeometry, NodeData, Edges, Groups, Reset }

/// <param name="NodeIds">The blocks affected, or null when the change is not about particular blocks.</param>
public readonly record struct CanvasChange(CanvasChangeKind Kind, IReadOnlyList<Guid>? NodeIds);
