using System.Net;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ben.Web.Tests.Website;

#pragma warning disable BL0006 // RenderTree types: this IS a renderer, so it reads the frames a renderer reads.

/// <summary>
/// A renderer that says it is interactive, so a page guarded by
/// <c>WaitUntilAuthReadyAsync(RendererInfo.IsInteractive)</c> runs its guard and its load rather
/// than bailing out the way it does under <see cref="Microsoft.AspNetCore.Components.Web.HtmlRenderer"/>
/// (10/22/2026, Administration → Pricing).
/// </summary>
/// <remarks>
/// It also clicks, types and toggles through the page's real event handlers, so a test can save a
/// value and read the sentence the page shows back — the page's code path, not a copy of it.
/// The HTML it writes is a plain reading of the render tree: enough to search, not a browser.
/// </remarks>
internal sealed class InteractiveTestRenderer : Renderer
{
    private readonly List<Exception> _errors = [];

    public InteractiveTestRenderer(IServiceProvider services)
        : base(services, NullLoggerFactory.Instance) { }

    public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

    protected override RendererInfo RendererInfo { get; } = new("Server", isInteractive: true);

    /// <summary>Anything a component threw, so a test can fail with it rather than hang.</summary>
    public IReadOnlyList<Exception> Errors => _errors;

    protected override void HandleException(Exception exception) => _errors.Add(exception);

    protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;

    /// <summary>Renders <typeparamref name="T"/> and waits for it to settle; answers its root id.</summary>
    public async Task<int> RenderAsync<T>(IDictionary<string, object?>? parameters = null) where T : IComponent
    {
        var component = InstantiateComponent(typeof(T));
        var id = AssignRootComponentId(component);
        await Dispatcher.InvokeAsync(() => RenderRootComponentAsync(id,
            ParameterView.FromDictionary(parameters ?? new Dictionary<string, object?>())));
        if (_errors.Count > 0) throw new InvalidOperationException("The component threw.", _errors[0]);
        return id;
    }

    /// <summary>The component's current markup, entities decoded.</summary>
    public string Html(int componentId)
    {
        var sb = new StringBuilder();
        var frames = GetCurrentRenderTreeFrames(componentId);
        Write(sb, frames.Array, 0, frames.Count);
        return WebUtility.HtmlDecode(sb.ToString());
    }

    /// <summary>
    /// Fires the <paramref name="eventName"/> handler (e.g. "onclick") on the first element that
    /// satisfies <paramref name="match"/>, inside the first element carrying
    /// <paramref name="withinAttribute"/> = <paramref name="withinValue"/> when one is given.
    /// </summary>
    public async Task FireAsync(int componentId, string eventName, Func<ElementView, bool> match, EventArgs args,
                                string? withinAttribute = null, string? withinValue = null)
    {
        var elements = new List<ElementView>();
        var frames = GetCurrentRenderTreeFrames(componentId);
        Collect(frames.Array, 0, frames.Count, elements);

        var scope = withinAttribute is null
            ? elements
            : elements.Where(e => e.Within.Contains($"{withinAttribute}={withinValue}")).ToList();

        var target = scope.FirstOrDefault(match)
            ?? throw new InvalidOperationException($"No element matched for {eventName}.");
        if (!target.Handlers.TryGetValue(eventName, out var handlerId))
            throw new InvalidOperationException($"The element has no {eventName} handler.");

        await Dispatcher.InvokeAsync(() => DispatchEventAsync(handlerId, null, args));
        if (_errors.Count > 0) throw new InvalidOperationException("A handler threw.", _errors[0]);
    }

    /// <summary>One element as <see cref="FireAsync"/> sees it.</summary>
    public sealed record ElementView(string Name, IReadOnlyDictionary<string, string> Attributes, string Text,
                                     IReadOnlyDictionary<string, ulong> Handlers, IReadOnlyList<string> Within);

    // ── Reading the frames ──────────────────────────────────────────────────

    private void Write(StringBuilder sb, RenderTreeFrame[] frames, int start, int end)
    {
        var i = start;
        while (i < end)
        {
            var f = frames[i];
            switch (f.FrameType)
            {
                case RenderTreeFrameType.Element:
                {
                    var stop = i + f.ElementSubtreeLength;
                    sb.Append('<').Append(f.ElementName);
                    var j = i + 1;
                    for (; j < stop && frames[j].FrameType == RenderTreeFrameType.Attribute; j++)
                    {
                        var a = frames[j];
                        switch (a.AttributeValue)
                        {
                            case string text: sb.Append(' ').Append(a.AttributeName).Append("=\"").Append(WebUtility.HtmlEncode(text)).Append('"'); break;
                            case bool b when b: sb.Append(' ').Append(a.AttributeName); break;
                            case bool: break;
                            case null: break;
                            case MulticastDelegate: break;
                            case EventCallback: break;
                            case var other when other.GetType().Name.StartsWith("EventCallback", StringComparison.Ordinal): break;
                            case var other: sb.Append(' ').Append(a.AttributeName).Append("=\"").Append(other).Append('"'); break;
                        }
                    }
                    sb.Append('>');
                    Write(sb, frames, j, stop);
                    sb.Append("</").Append(f.ElementName).Append('>');
                    i = stop;
                    break;
                }
                case RenderTreeFrameType.Text:
                    sb.Append(WebUtility.HtmlEncode(f.TextContent));
                    i++;
                    break;
                case RenderTreeFrameType.Markup:
                    sb.Append(f.MarkupContent);
                    i++;
                    break;
                case RenderTreeFrameType.Component:
                {
                    var child = GetCurrentRenderTreeFrames(f.ComponentId);
                    Write(sb, child.Array, 0, child.Count);
                    i += f.ComponentSubtreeLength;
                    break;
                }
                case RenderTreeFrameType.Region:
                    Write(sb, frames, i + 1, i + f.RegionSubtreeLength);
                    i += f.RegionSubtreeLength;
                    break;
                default:
                    i++;
                    break;
            }
        }
    }

    private void Collect(RenderTreeFrame[] frames, int start, int end, List<ElementView> into,
                         IReadOnlyList<string>? within = null)
    {
        within ??= [];
        var i = start;
        while (i < end)
        {
            var f = frames[i];
            switch (f.FrameType)
            {
                case RenderTreeFrameType.Element:
                {
                    var stop = i + f.ElementSubtreeLength;
                    var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
                    var handlers = new Dictionary<string, ulong>(StringComparer.Ordinal);
                    var j = i + 1;
                    for (; j < stop && frames[j].FrameType == RenderTreeFrameType.Attribute; j++)
                    {
                        var a = frames[j];
                        if (a.AttributeEventHandlerId != 0) handlers[a.AttributeName] = a.AttributeEventHandlerId;
                        else if (a.AttributeValue is string s) attributes[a.AttributeName] = s;
                    }

                    var text = new StringBuilder();
                    Write(text, frames, j, stop);
                    var mine = within.Concat(attributes.Select(kv => $"{kv.Key}={kv.Value}")).ToList();
                    into.Add(new ElementView(f.ElementName, attributes,
                        System.Text.RegularExpressions.Regex.Replace(WebUtility.HtmlDecode(text.ToString()), "<[^>]+>", " ").Trim(),
                        handlers, within));
                    Collect(frames, j, stop, into, mine);
                    i = stop;
                    break;
                }
                case RenderTreeFrameType.Component:
                {
                    var child = GetCurrentRenderTreeFrames(f.ComponentId);
                    Collect(child.Array, 0, child.Count, into, within);
                    i += f.ComponentSubtreeLength;
                    break;
                }
                case RenderTreeFrameType.Region:
                    Collect(frames, i + 1, i + f.RegionSubtreeLength, into, within);
                    i += f.RegionSubtreeLength;
                    break;
                default:
                    i++;
                    break;
            }
        }
    }
}
