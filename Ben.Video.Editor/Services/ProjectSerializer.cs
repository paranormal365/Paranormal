using System.Text.Json;
using System.Text.Json.Serialization;
using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>
/// The one way a project file is turned into JSON and back.
/// </summary>
/// <remarks>
/// <para>There were four copies of these settings. The editor's own two agreed with each other;
/// the site's My Videos page and its case editor each built their own inline, with case-insensitive
/// names and <b>no string-enum converter</b>. The editor writes every enum as a string, so those
/// two could not parse a project the editor had written at all — opening a server project from
/// either page threw and was swallowed by a catch, which is why it looked like nothing happened
/// (2026-09-05 audit, persistence-1).</para>
///
/// <para>Settings duplicated in four places will drift; settings in one place cannot. Everything
/// that reads or writes a project file goes through here, including the tests, so a change to the
/// shape is a change everybody sees at once.</para>
/// </remarks>
public static class ProjectSerializer
{
    /// <summary>
    /// The settings themselves.
    /// </summary>
    /// <remarks>
    /// <para><c>JsonStringEnumConverter</c> is the load-bearing one: a project file is meant to be
    /// readable and diffable, and a saved file full of bare integers would also break the moment
    /// anybody inserted a value into an enum.</para>
    ///
    /// <para>Case-insensitive reading is kept because files already exist that were written by
    /// each of the four old copies.</para>
    /// </remarks>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented               = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        Converters                  = { new JsonStringEnumConverter() },
    };

    /// <summary>Writes a project file.</summary>
    public static string Serialize(ProjectFile file) => JsonSerializer.Serialize(file, Options);

    /// <summary>Writes a project file straight to bytes, for an upload.</summary>
    public static byte[] SerializeToUtf8Bytes(ProjectFile file) =>
        JsonSerializer.SerializeToUtf8Bytes(file, Options);

    /// <summary>Reads a project file, or null when the text is not one.</summary>
    public static ProjectFile? Deserialize(string json) =>
        JsonSerializer.Deserialize<ProjectFile>(json, Options);

    /// <summary>
    /// Reads a project file and says what was wrong when it cannot.
    /// </summary>
    /// <returns>
    /// The file, or null with a message fit to show somebody.
    /// </returns>
    /// <remarks>
    /// Opening a file that is not a project used to succeed: JSON with none of the expected
    /// properties deserialises into an object whose every list is empty, so the editor replaced the
    /// person's work with a blank timeline and said "Project loaded" (2026-09-05 audit,
    /// persistence-8). A project has at least a name and a schema version; anything without them
    /// is not one.
    /// </remarks>
    public static (ProjectFile? File, string? Problem) Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return (null, "That file is empty.");

        ProjectFile? file;
        try
        {
            file = Deserialize(json);
        }
        catch (JsonException ex)
        {
            return (null, $"That file is not readable as a project: {ex.Message}");
        }

        if (file is null)
            return (null, "That file is not a project.");

        // Whether the properties were actually THERE, not what they deserialised to. Every one of
        // them has a default on the DTO, so JSON containing none of them produces a perfectly
        // valid-looking ProjectFile called "Untitled Project" — which is exactly how opening a
        // stranger's JSON used to replace somebody's timeline with a blank one and report success.
        if (!LooksLikeAProject(json))
            return (null, "That file is valid JSON, but it is not a video project.");

        if (file.SchemaVersion > ProjectFile.CurrentSchemaVersion)
            return (null,
                $"That project was saved by a newer version of the editor (format "
                + $"{file.SchemaVersion}, this one reads {ProjectFile.CurrentSchemaVersion}).");

        return (ProjectFileMigrations.Upgrade(file), null);
    }

    /// <summary>
    /// Whether the JSON actually carries what a project file carries.
    /// </summary>
    /// <remarks>
    /// A project the editor wrote always names its format version and its project name. Requiring
    /// them to be present — rather than reading what they deserialised to — is what tells a real
    /// project from any other object, since every property here has a default.
    /// </remarks>
    private static bool LooksLikeAProject(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;

            return Has(document.RootElement, "schemaVersion")
                && Has(document.RootElement, "projectName");
        }
        catch (JsonException)
        {
            return false;
        }

        // Case-insensitively, because files exist that were written by each of the old copies.
        static bool Has(JsonElement root, string name) =>
            root.EnumerateObject().Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
