using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The schema filter that trims navigation properties out of the API documentation.
/// </summary>
/// <remarks>
/// It used to decide by name — anything ending in "s" — which quietly removed <c>Status</c>,
/// <c>Notes</c> and <c>RadiusMiles</c> from the documented shape of the API. Every test here is
/// written so it fails against that rule: each kept property ends in the letter it keyed on.
/// </remarks>
public class SwaggerSchemaFilterTests
{
    private sealed class Nested { public string? Label { get; set; } }

    private sealed class SampleEntity
    {
        // Scalars whose names end in "s" — the exact false positives of the old rule.
        public string? Status { get; set; }
        public string? Notes { get; set; }
        public decimal RadiusMiles { get; set; }
        public bool IsPublic { get; set; }

        // Ordinary scalars.
        public Guid Id { get; set; }
        public DateTime DateCreated { get; set; }
        public byte[]? FileData { get; set; }

        // Navigations — what the filter is actually for.
        public Nested? Parent { get; set; }
        public List<Nested>? Children { get; set; }
    }

    private static OpenApiSchema FilteredSchemaFor<T>()
    {
        var schema = new OpenApiSchema { Properties = new Dictionary<string, OpenApiSchema>() };
        foreach (var p in typeof(T).GetProperties())
            schema.Properties[char.ToLowerInvariant(p.Name[0]) + p.Name[1..]] = new OpenApiSchema();

        var filterType = typeof(Ben.Data.WebApi.Services.RateLimiting).Assembly
            .GetType("CircularReferenceSchemaFilter")!;
        var filter = (ISchemaFilter)Activator.CreateInstance(filterType)!;

        filter.Apply(schema, new SchemaFilterContext(typeof(T), null, null!));
        return schema;
    }

    [Theory]
    [InlineData("status")]
    [InlineData("notes")]
    [InlineData("radiusMiles")]
    [InlineData("isPublic")]
    public void Scalars_whose_names_end_in_s_stay_documented(string property)
    {
        Assert.Contains(property, FilteredSchemaFor<SampleEntity>().Properties.Keys);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("dateCreated")]
    [InlineData("fileData")]
    public void Ordinary_scalars_stay_documented(string property)
    {
        Assert.Contains(property, FilteredSchemaFor<SampleEntity>().Properties.Keys);
    }

    [Theory]
    [InlineData("parent")]    // entity reference
    [InlineData("children")]  // collection of entities
    public void Navigation_properties_are_removed(string property)
    {
        Assert.DoesNotContain(property, FilteredSchemaFor<SampleEntity>().Properties.Keys);
    }
}
