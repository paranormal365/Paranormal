using Ben.Canvas.Editor.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Ben.Canvas.Tests.Components;

/// <summary>Renders a <see cref="CanvasEditor"/> and hands the live instance to the test through <see cref="HarnessRegistry"/>.</summary>
public sealed class EditorHarness : ComponentBase
{
    [Inject] private HarnessRegistry Registry { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CanvasEditor>(0);
        builder.AddComponentReferenceCapture(1, instance => Registry.Instance = instance);
        builder.CloseComponent();
    }
}
