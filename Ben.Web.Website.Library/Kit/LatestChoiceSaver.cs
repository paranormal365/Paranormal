namespace Ben.Web.Website.Library.Kit;

/// <summary>
/// Saves a switch one request at a time, ending on the person's last choice.
/// </summary>
/// <remarks>
/// <para>On, then off, clicked quickly sent both requests at once. They could arrive in either order,
/// so the server sometimes kept "on" while the switch showed "off", which for a session's sharing
/// switches means a session shown publicly that its sender had withdrawn (site audit e2e,
/// 10/27/2026). A click that lands mid-save now only records the choice, and the save already going
/// sends it once it has its answer.</para>
///
/// <para>Shared by My field sessions and a session's own page (10/28/2026), so the rule lives once.</para>
/// </remarks>
public sealed class LatestChoiceSaver
{
    private readonly Dictionary<(Guid Session, string Switch), bool> _wanted = [];
    private readonly HashSet<(Guid Session, string Switch)> _saving = [];

    /// <summary>Whether any save is under way.</summary>
    public bool Busy => _saving.Count > 0;

    /// <summary>The server's reason the last save was refused, or null.</summary>
    public string? Error { get; private set; }

    /// <param name="send">Saves one value; answers whether it was done and, if not, the server's reason.</param>
    /// <param name="applied">Called with each value the server accepted.</param>
    public async Task SaveAsync(
        Guid sessionId, string which, bool value,
        Func<bool, Task<(bool Done, string? Error)>> send, Action<bool> applied)
    {
        var key = (sessionId, which);
        _wanted[key] = value;
        if (!_saving.Add(key)) return;

        Error = null;
        try
        {
            bool sent;
            do
            {
                sent = _wanted[key];
                var (done, error) = await send(sent);
                if (!done) { Error = error; return; }
                applied(sent);
            }
            while (_wanted[key] != sent);
        }
        finally
        {
            _saving.Remove(key);
            _wanted.Remove(key);
        }
    }
}
