namespace Ben.Data.Common;

/// <summary>
/// Reads a private key named by configuration, and says plainly why it could not.
/// </summary>
/// <remarks>
/// <para>Three places each did this with <c>File.Exists</c> and their own copy of the message, and
/// <c>File.Exists</c> cannot tell the two failures apart: it answers <c>false</c> when the file is
/// missing AND when this process may not read it.</para>
///
/// <para><b>What that cost, 2026-09-11.</b> The API refused to start with "Maps:PrivateKeyPath
/// names a file that does not exist" naming a key that was sitting right there. The key lives in
/// the deploy folder, which is locked to Administrators and SYSTEM on purpose, and the application
/// pool identity is neither. The message sent the deploy hunting for a missing file for as long as
/// it took to read an ACL.</para>
///
/// <para>So: distinguish the two, and name the identity that was asking. The pool identity is
/// rarely the person who put the key on the machine, and saying which one could not read it is
/// what turns this into a one-line fix.</para>
/// </remarks>
public static class PrivateKeyFile
{
    /// <summary>
    /// The key at <paramref name="path"/>, or an empty string when no path is configured — which
    /// every caller treats as "this feature is switched off".
    /// </summary>
    /// <param name="path">The configured path. Null, empty or whitespace means not configured.</param>
    /// <param name="settingName">The setting to name in the message, e.g. <c>Maps:PrivateKeyPath</c>.</param>
    public static string ReadOrEmpty(string? path, string settingName)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        try
        {
            return File.ReadAllText(path);
        }
        catch (FileNotFoundException ex)
        {
            throw new InvalidOperationException(
                $"{settingName} names a file that is not there: {path}", ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new InvalidOperationException(
                $"{settingName} names a folder that is not there: {path}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException(
                $"{settingName} names a file this process may not read: {path}. "
                + $"Running as {Identity()}. The file may well exist — grant that identity read on "
                + "the file itself rather than opening up the folder around it.", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"{settingName} could not be read: {path}. Running as {Identity()}.", ex);
        }
    }

    /// <summary>Environment.UserName rather than WindowsIdentity: this must not throw off Windows.</summary>
    private static string Identity()
    {
        try { return Environment.UserName; }
        catch { return "an unknown identity"; }
    }
}
