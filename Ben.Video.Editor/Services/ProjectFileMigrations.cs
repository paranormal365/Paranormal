using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Brings a project file written by an older editor up to what this one expects.
/// </summary>
/// <remarks>
/// <para>Every reader used to run straight off the deserialised object, so what an older file's
/// gaps became was whatever the DTO defaults happened to be, decided independently at each call
/// site. Doing it in one place means an old project opens the same way wherever it is opened
/// from — the editor, the site's My Videos page, or a case.</para>
///
/// <para>Migrations are additive and never destructive: an older file has less information, not
/// wrong information, so the job is filling gaps rather than rewriting anything.</para>
/// </remarks>
public static class ProjectFileMigrations
{
    /// <summary>Upgrades <paramref name="file"/> in place and returns it.</summary>
    public static ProjectFile Upgrade(ProjectFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        // Version 1 → 2 added the media bin, and needs no data change here: ClipStore.RestoreBin
        // already seeds an empty bin from what is on the timeline, giving each entry its own id
        // and linking it to the clips it was derived from. Filling the bin here instead would
        // hand it entries sharing ids with timeline clips, which is worse than doing nothing.
        //
        // Stamping the version is the part that matters, so a file read once is written back at
        // the current version rather than being migrated again on every open.
        file.SchemaVersion = ProjectFile.CurrentSchemaVersion;
        return file;
    }
}
