using System.Reflection;
using Ben.Canvas.Editor.Components;
using Microsoft.AspNetCore.Components;

namespace Ben.Canvas.Tests.Hosting;

/// <summary>
/// The CanvasEditor host contract is exactly the agreed parameters.
/// </summary>
/// <remarks>
/// Two hosts mount this component - the standalone app now, the site later - and a parameter added for
/// one quietly becomes something the other forgets to pass. There is deliberately no EnablePublish
/// flag: server buttons are gated on each seam's availability, the lesson of Ben.Video.Editor's
/// IProjectServerStore. When a later milestone adds a parameter, it adds it to the expected set here.
/// </remarks>
public sealed class CanvasEditorContractTests
{
    private static Dictionary<string, Type> Parameters() =>
        typeof(CanvasEditor)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null)
            .ToDictionary(p => p.Name, p => p.PropertyType, StringComparer.Ordinal);

    [Fact]
    public void The_host_parameters_are_the_agreed_set()
    {
        Assert.Equal(
            new[] { "BackContent", "CaseId", "HostStatusContent", "OpenServerDocumentId", "OrganizationId", "ShowDiagnostics" },
            Parameters().Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void There_is_no_enable_publish_flag()
    {
        var names = Parameters().Keys;

        Assert.DoesNotContain("EnablePublish", names);
        Assert.DoesNotContain("DocumentIdFromHost", names);
    }

    [Fact]
    public void The_parameter_types_are_pinned()
    {
        var p = Parameters();

        Assert.Equal(typeof(Guid?), p["CaseId"]);
        Assert.Equal(typeof(Guid?), p["OrganizationId"]);
        Assert.Equal(typeof(Guid?), p["OpenServerDocumentId"]);
        Assert.Equal(typeof(bool), p["ShowDiagnostics"]);
        Assert.Equal(typeof(RenderFragment), p["HostStatusContent"]);
        Assert.Equal(typeof(RenderFragment), p["BackContent"]);
    }
}
