using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// The one way a project file is written and read.
/// </summary>
/// <remarks>
/// There were four copies of these settings. The editor's own two agreed; the site's My Videos page
/// and its case editor each built their own inline, with case-insensitive names and no string-enum
/// converter — and the editor writes every enum as a string, so neither could read a project the
/// editor had written. Opening a server project from either page threw and was swallowed by a
/// catch, which is why it looked like nothing happened (2026-09-05 audit, persistence-1).
/// </remarks>
public sealed class ProjectSerializerTests
{
    [Fact]
    public void Enums_are_written_as_names_so_inserting_a_value_cannot_shift_them()
    {
        var file = new ProjectFile();
        file.Tracks.Add(new ProjectTrack { Type = TrackType.Audio, Label = "Audio 1" });

        var json = ProjectSerializer.Serialize(file);

        Assert.Contains("\"Audio\"", json);
        Assert.DoesNotContain("\"Type\": 1", json);
    }

    /// <summary>
    /// The exact failure the four copies produced: a file written by the editor, read with the
    /// site's old settings, threw on the very first enum.
    /// </summary>
    [Fact]
    public void A_file_this_writes_is_a_file_this_reads()
    {
        var file = new ProjectFile { ProjectName = "Basement" };
        file.Tracks.Add(new ProjectTrack
        {
            Type = TrackType.Audio,
            Transitions = { new ProjectTransition { Style = TransitionStyle.CircleOpen } },
        });

        var back = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(file));

        Assert.NotNull(back);
        Assert.Equal(TrackType.Audio, back!.Tracks[0].Type);
        Assert.Equal(TransitionStyle.CircleOpen, back.Tracks[0].Transitions[0].Style);
    }

    [Fact]
    public void Files_written_by_the_older_settings_still_read()
    {
        // Property names in a different case — one of the four old copies wrote them this way.
        var json = """{"schemaversion":2,"projectname":"Old","tracks":[]}""";

        var (file, problem) = ProjectSerializer.Parse(json);

        Assert.Null(problem);
        Assert.Equal("Old", file!.ProjectName);
    }

    // ── Parse refuses things that are not projects ────────────────────────────

    /// <summary>
    /// JSON with none of the expected properties deserialises into an object whose every list is
    /// empty. Opening one used to replace the person's timeline with a blank one and report
    /// success (2026-09-05 audit, persistence-8).
    /// </summary>
    [Theory]
    [InlineData("""{"hello":"world"}""")]
    [InlineData("""{"tracks":[]}""")]
    [InlineData("[]")]
    public void Json_that_is_not_a_project_is_refused(string json)
    {
        var (file, problem) = ProjectSerializer.Parse(json);

        Assert.Null(file);
        Assert.NotNull(problem);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData(null)]
    public void Something_that_is_not_json_is_refused_with_a_reason(string? json)
    {
        var (file, problem) = ProjectSerializer.Parse(json);

        Assert.Null(file);
        Assert.NotEmpty(problem!);
    }

    /// <summary>
    /// A file from a newer editor is refused rather than opened half-understood, because the parts
    /// this version does not know about would be silently dropped on the next save.
    /// </summary>
    [Fact]
    public void A_project_from_a_newer_editor_is_refused_and_says_so()
    {
        var json = $$"""{"schemaVersion":{{ProjectFile.CurrentSchemaVersion + 1}},"projectName":"Future"}""";

        var (file, problem) = ProjectSerializer.Parse(json);

        Assert.Null(file);
        Assert.Contains("newer version", problem);
    }

    [Fact]
    public void A_real_project_parses_and_is_stamped_with_the_current_format()
    {
        var json = """{"schemaVersion":1,"projectName":"Old one","tracks":[]}""";

        var (file, problem) = ProjectSerializer.Parse(json);

        Assert.Null(problem);
        Assert.Equal(ProjectFile.CurrentSchemaVersion, file!.SchemaVersion);
    }
}
