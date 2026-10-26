namespace Ben.Web.Website.Library.Manage.Audio;

/// <summary>What the audio editor may do with the recording it has open.</summary>
[Flags]
public enum AudioEditorCapabilities
{
    None     = 0,
    /// <summary>Place, edit and delete markers.</summary>
    Markers  = 1,
    /// <summary>Scan for EVP and rule on the candidates.</summary>
    Scan     = 2,
    /// <summary>Save a stretch as its own file.</summary>
    Clips    = 4,
    /// <summary>The destructive edits (Cut, Normalize, …), each of which makes a new file.</summary>
    Edit     = 8,
    /// <summary>Remember how the recording is set up (spectrogram, listening chain).</summary>
    SaveView = 16,

    All = Markers | Scan | Clips | Edit | SaveView,
}

/// <summary>
/// The recording the audio editor page has open, wherever it came from.
/// </summary>
/// <remarks>
/// <para>Two kinds exist in the plan (ProjectNotes/AudioEditor-EVP-Plan-2026-10-26.md): an ordinary
/// uploaded file (<see cref="UploadFileAudioSource"/>), and a clip inside a field session's .ben
/// bundle (phase 9). A bundle member has no UploadFile of its own, so markers and scans attach to
/// <see cref="AnalysisFileId"/>, the file the editor's records hang off. For an uploaded file
/// that is the file itself.</para>
/// </remarks>
public interface IAudioEditorSource
{
    /// <summary>What the player streams. The browser fetches it; nothing passes through the site.</summary>
    string Url { get; }

    string FileName { get; }
    string ContentType { get; }
    long FileSize { get; }

    /// <summary>The UploadFile that markers, scans, clips and the saved view belong to.</summary>
    Guid AnalysisFileId { get; }

    /// <summary>Where the editor's back link goes when the page was opened without a return address.</summary>
    string BackHref { get; }

    AudioEditorCapabilities Can { get; }
}

/// <summary>An uploaded audio file, opened in the editor by its id.</summary>
public sealed record UploadFileAudioSource(
    Guid FileId,
    string Url,
    string FileName,
    string ContentType,
    long FileSize,
    AudioEditorCapabilities Can = AudioEditorCapabilities.All,
    string BackHref = "/media-library") : IAudioEditorSource
{
    public Guid AnalysisFileId => FileId;
}
