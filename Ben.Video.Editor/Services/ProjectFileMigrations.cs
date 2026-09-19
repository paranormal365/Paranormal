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
        // Version 2 → 3 moved an arrow or line callout's path points from canvas fractions to
        // fractions of the callout's own box. As canvas fractions they had no relationship to the
        // shape that owned them, so moving or resizing a callout left its arrow behind
        // (2026-09-05 audit, callouts-3). An older file's values are read in the space they were
        // written in and rewritten in the new one.
        if (file.SchemaVersion < 3)
        {
            foreach (var callout in file.Tracks.SelectMany(t => t.CalloutClips))
                MigrateCalloutPathPoints(callout);
        }

        // Stamping the version is the part that matters, so a file read once is written back at
        // the current version rather than being migrated again on every open.
        file.SchemaVersion = ProjectFile.CurrentSchemaVersion;
        return file;
    }

    /// <summary>
    /// Rewrites one callout's path points from canvas fractions to box fractions.
    /// </summary>
    /// <remarks>
    /// Works on the DTO rather than the model because migration happens before anything is
    /// restored — the same arithmetic, applied where the old values still are.
    /// </remarks>
    private static void MigrateCalloutPathPoints(ProjectCalloutClip callout)
    {
        var cpv = callout.ControlPointValues;

        foreach (var key in CalloutControlPointSpace.PathKeys)
        {
            if (!cpv.TryGetValue(key, out var canvasValue)) continue;

            var isX = key.EndsWith('X');
            cpv[key] = CalloutControlPointSpace.FromCanvas(
                canvasValue,
                isX ? callout.X : callout.Y,
                isX ? callout.Width : callout.Height);
        }
    }
}
