using System.Runtime.InteropServices;

namespace Ben.Video.Sidecar.Lifetime;

/// <summary>
/// The listening socket launchd has already opened for us, when launchd started this process.
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-19: the sidecar should run only while the video editor is being used. The
/// other half of that is how it comes BACK, and on macOS the answer is launchd: the LaunchAgent
/// declares the socket rather than RunAtLoad, launchd binds the port itself and holds it whether
/// or not the process exists, and the first connection starts the process and hands it the
/// already-bound socket. The editor's existing health probe is that first connection, so nothing
/// in the editor has to know any of this — the sidecar simply appears to be there.</para>
///
/// <para>Away from launchd — <c>dotnet run</c>, the tests, Windows — <see cref="TryTakeListener"/>
/// finds nothing and the caller binds the port itself exactly as it always has. That fallback is
/// the normal path for every developer, so it is the one that must not be clever.</para>
/// </remarks>
public static class LaunchdSockets
{
    /// <summary>The name the LaunchAgent's Sockets dictionary gives the entry.</summary>
    public const string SocketName = "Listener";

    /// <summary>
    /// launchd's own API for claiming the sockets it opened on this job's behalf. Returns 0 on
    /// success and fills <paramref name="fds"/> with a malloc'd array the caller frees.
    /// </summary>
    [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "launch_activate_socket")]
    private static extern int LaunchActivateSocket(
        [MarshalAs(UnmanagedType.LPStr)] string name, out IntPtr fds, out uint count);

    [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "free")]
    private static extern void Free(IntPtr ptr);

    /// <summary>
    /// The file descriptor of launchd's listening socket, or null when this process was not
    /// started by launchd with a Sockets entry of that name.
    /// </summary>
    public static ulong? TryTakeListener(string name = SocketName)
    {
        if (!OperatingSystem.IsMacOS()) return null;

        IntPtr fds = IntPtr.Zero;
        try
        {
            // Anything other than 0 means "there is no such socket for this process", which is the
            // ordinary case when a developer runs this directly. It is not an error worth a word.
            if (LaunchActivateSocket(name, out fds, out var count) != 0 || count == 0 || fds == IntPtr.Zero)
                return null;

            // One socket, because the LaunchAgent declares one. Reading the first is the whole of it.
            return unchecked((ulong)Marshal.ReadInt32(fds));
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
        finally
        {
            if (fds != IntPtr.Zero) Free(fds);
        }
    }
}
