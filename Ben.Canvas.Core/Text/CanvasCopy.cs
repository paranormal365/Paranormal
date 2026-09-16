using System.Globalization;

namespace Ben.Canvas.Core.Text;

/// <summary>
/// Every word the canvas shows people, in one place, in the site's plain voice.
/// </summary>
/// <remarks>
/// <para>One class so each sentence can be read, reviewed and tested together. The tests hold the voice:
/// no exclamation marks, no "Oops", no "successfully", and every refusal says what happened and what to
/// do next.</para>
///
/// <para>Numbers are formatted with the invariant culture, because the WebAssembly host runs with
/// invariant globalization and a sentence must read the same on every device.</para>
/// </remarks>
public static class CanvasCopy
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Titles and headings.</summary>
    public static class Titles
    {
        public const string DocumentTitle = "IsHaunted Canvas";
        public const string LoginTitle = "Sign in - IsHaunted Canvas";
        public const string DefaultBoardTitle = "Untitled board";
        public const string EmptyBoard = "This board is empty";
        public const string PublishConfirm = "Publish this board?";
        public const string NewerCopy = "A newer copy is on the case";
        public const string CaseFiles = "The case's files";
    }

    /// <summary>Field labels and placeholders.</summary>
    public static class Labels
    {
        public const string SearchFiles = "Search these files";
    }

    /// <summary>
    /// What a block is called when the words have to name one. Not "Words": that name already
    /// belongs to the board's announcements (Ben.Canvas.Editor.Services.Words).
    /// </summary>
    public static class Kinds
    {
        public const string Picture = "Picture";
        public const string Recording = "Recording";
        public const string Video = "Video";
        public const string File = "File";
    }

    /// <summary>Button and menu labels: short, no end punctuation.</summary>
    public static class Buttons
    {
        public const string Add = "Add";
        public const string Paste = "Paste";
        public const string Photo = "Photo";
        public const string Undo = "Undo";
        public const string Redo = "Redo";
        public const string SaveToCase = "Save to case";
        public const string Publish = "Publish";
        public const string ExportBoard = "Export board";
        public const string ImportBoard = "Import board";
        public const string NewBoard = "New board";
        public const string Boards = "Boards";
        public const string Fit = "Fit";
        public const string Connect = "Connect";
        public const string Lock = "Lock";
        public const string Unlock = "Unlock";
        public const string Duplicate = "Duplicate";
        public const string Delete = "Delete";
        public const string BringToFront = "Bring to front";
        public const string SendToBack = "Send to back";
        public const string Group = "Group";
        public const string Ungroup = "Ungroup";
        public const string SignIn = "Sign in";
        public const string SignOut = "Sign out";
        public const string KeepMine = "Keep mine";
        public const string TakeTheirs = "Take theirs";
        public const string ExportMineFirst = "Export mine first";
        public const string AddACard = "Add a card";
        public const string StartANewBoard = "Start a new board";
        public const string ImportAFile = "Import a file";
        public const string Download = "Download";
        public const string ViewOnly = "View only";
        public const string CaseFiles = "Case files";
        public const string TryAgain = "Try again";
    }

    /// <summary>Whole sentences: refusals, confirmations and explanations.</summary>
    public static class Sentences
    {
        public const string Heic =
            "That photo is in HEIC format, which browsers cannot show. On the iPhone, choose the photo in Photos, tap Share, then Copy — it comes across as a JPEG.";

        public static string TooLargeImage(int mb) =>
            string.Format(Invariant, "That image is {0} MB, and the board keeps images under 25 MB. Try a smaller copy.", mb);

        public const string TooLargeFile =
            "That file is over 100 MB. Upload it to the case's Files tab instead and paste the link here.";

        // Not "Safari": the board cannot know which browser refused, and this was read in Chrome.
        public const string Permission =
            "The browser did not let the board read the clipboard. Tap Paste again and choose Allow, or paste with Ctrl+V or Cmd+V on the board.";

        public const string NothingToPaste = "There is nothing on the clipboard the board can use.";

        public static string TooManyItems(int max) =>
            string.Format(Invariant, "Only the first {0} items were placed. Paste the rest separately.", max);

        public static string TextTruncated(int max) =>
            string.Format(Invariant, "That text was longer than {0} characters, so only the start was placed.", max);

        public const string EmptyBoardHint =
            "Paste anything — text, a link, a photo, a file — or add a card from the toolbar.";

        public const string Offline =
            "You are offline. Everything you do is kept on this device and will save to the case when you are back.";

        public const string Unsaved = "This board has changes that are not saved to the case yet.";

        public const string NotFinishedSaving = "This board has changes that have not finished saving.";

        public const string PublishRunning = "A publish is still running in this tab. Leaving now cancels it.";

        public const string ServerConflict =
            "Somebody saved a newer copy of this board while you were working. Keep yours, or take theirs?";

        public static string SaveFailed(string problem) =>
            string.Format(Invariant, "Could not save to the case: {0}. Your work is still on this device.", (problem ?? "").Trim().TrimEnd('.'));

        public const string StorageRefused =
            "This browser is not letting the board store anything (private browsing, or storage is full). Export the board to keep it.";

        /// <summary>The browser threw while the board was being written, rather than refusing: the save did not happen.</summary>
        public const string LocalSaveFailed =
            "This device could not save the board just now. Keep working — it will try again with your next change, and you can export a copy from the toolbar.";

        public const string SignedOutSave = "Sign in to save this board to the case.";

        public const string MapUnavailable = "The map could not be loaded.";

        public const string PublishConfirmBody =
            "A picture of the whole board is added to the case's files, where only people on the case can see it, and the board is marked as published. Map boxes are shown as their address, not as map tiles.";

        public const string PublishFailed = "The picture of the board could not be made. Try again, or export the board.";

        public const string PublishDone = "Published to the case.";

        public static string AssetNotStored(string name) =>
            string.Format(Invariant, "{0} could not be kept on this device (storage may be full). Export the board, then try again.", name);

        public static string DeleteConfirm(int n) =>
            string.Format(Invariant, "Delete {0} item(s)? You can undo this.", n);

        // ── Paste, device storage, export and import ────────────────────────

        public static string Pasted(int n) =>
            string.Format(Invariant, "Added {0} item(s) from the clipboard.", n);

        public static string BoardFull(int max) =>
            string.Format(Invariant, "A board holds at most {0} blocks, so nothing was pasted. Remove some blocks, then paste again.", max);

        public const string PasteRefusedSheet =
            "This browser did not let the board read the clipboard. Touch and hold in the box below, then choose Paste.";

        public const string MapsNotEnabled =
            "Maps are switched off for this board, so the place was added as a note.";

        public const string StorageNotPersistent =
            "This browser may clear boards kept only on this device if the site goes unused for a while. Save to case or export the board to keep a copy.";

        public static string RestoreFailed(string problem) =>
            string.Format(Invariant, "The last board on this device could not be opened, so a new board was started. {0}", EndSentence(problem));

        public const string ImportNotABoard =
            "That file is not a board saved from IsHaunted Canvas. Choose a file that ends in .ishcanvas.";

        public static string ImportAssetTooLarge(string name) =>
            string.Format(Invariant, "The picture or file named {0} is too large to bring in, so it was left out.", name);

        public const string Imported = "The board was imported and is open.";

        public static string ExportTooLarge(int mb, string largest) =>
            string.Format(Invariant, "This board's pictures and files come to {0} MB, more than the 300 MB an export can hold. The largest are {1}.", mb, (largest ?? "").Trim().TrimEnd('.'));

        public static string ExportAssetsMissing(int n) =>
            string.Format(Invariant, "{0} picture(s) or file(s) were not on this device, so they are not in the export.", n);

        public const string ExportReady = "The export is ready. Tap Download to save it.";

        public const string ExportFailed = "The board could not be exported. Try again, or save it to the case.";

        // ── The server ──────────────────────────────────────────────────────

        public const string ServerNotConfigured = "This editor is not set up to save to a server.";

        public const string ServerUnreachable = "Could not reach the server.";

        public const string SignInExpired = "Your sign-in has expired. Sign in again and save once more.";

        public const string SaveForbidden = "Your account is not allowed to save boards on this case.";

        public const string BoardGoneFromServer = "That board is no longer on the server. Saving again will create a new one.";

        public const string ServerRefusedBoard = "The server refused this board.";

        public static string NewerServerCopy(int revision) =>
            string.Format(Invariant, "Somebody saved a newer copy of this board (revision {0}).", revision);

        public const string AddFilesForbidden = "Your account is not allowed to add files to this case.";

        // ── The case's own files, offered to the board ──────────────────────

        public const string CaseFilesLoading = "Looking at what the case already holds.";

        public const string CaseFilesEmpty =
            "This case has no files yet. Drop one on the board and it is added to the case as well.";

        public const string CaseFilesNoMatch = "No file on this case has that in its name.";

        public const string CaseFilesNoCase =
            "This board is not on a case yet, so there are no case files to reach for. Save it to a case first.";

        // ── Presenting ──────────────────────────────────────────────────────

        public const string NothingToPresent =
            "There is nothing on this board to present yet. Add a card, then try again.";

        public const string SlideGone =
            "That card is no longer on the board, so the walk stopped.";

        public const string PresentingStopped = "Stopped presenting.";

        public static string AddedFromCase(string name) =>
            string.Format(Invariant, "Added {0} from the case's files.", name);

        public const string ViewOnly =
            "You can view this board but not change it. Ask the case manager for edit access, or export a copy.";

        public const string SavedToCase = "Saved to the case.";

        public const string OpenedFromCase = "The case's board is open.";

        public const string TookTheirs = "The newer copy from the case is open.";

        public static string FileNotUploaded(string name, string problem) =>
            string.Format(Invariant, "{0} could not be added to the case, so the board was not saved: {1}", name, EndSentence(problem));

        private static string EndSentence(string? text)
        {
            var s = (text ?? "").Trim();
            if (s.Length == 0) return "";
            return s.EndsWith('.') || s.EndsWith('?') ? s : s + ".";
        }
        // ── Reading a board file ────────────────────────────────────────────

        public const string FileEmpty = "That file is empty.";

        public static string NotReadable(string detail)
        {
            var d = (detail ?? "").Trim();
            if (!d.EndsWith('.')) d += ".";
            return "That file is not readable as a board: " + d;
        }

        public const string NotABoard = "That file is valid JSON, but it is not a canvas board.";

        public static string NewerFormat(int file, int reader) =>
            string.Format(Invariant, "That board was saved by a newer version of the editor (format {0}, this one reads {1}).", file, reader);
    }

    /// <summary>Save-state words for the header's status indicator.</summary>
    public static class Status
    {
        public const string SavedLocal = "Saved on this device";
        public const string Saving = "Saving…";

        public static string SavedServer(string relative) => "Saved to case " + (relative ?? "").Trim();

        public const string SaveRetry = "Could not save — tap to retry";
        public const string OfflineShort = "Offline — changes stay on this device";
        public const string ConflictShort = "Someone else saved a newer copy";
    }
}
