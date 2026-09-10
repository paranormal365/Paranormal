using Ben.Desktop.App.Library.ViewModels;

namespace Ben.Desktop.App.UI.Pages;

public partial class HomePage : ContentPage
{
    private readonly SessionViewModel _session;

    public HomePage(SessionViewModel session)
    {
        InitializeComponent();
        _session = session;
        _session.PropertyChanged += (_, _) => Render();
        Render();
    }

    private void Render()
    {
        var me = _session.State.Me;
        Greeting.Text = me is null ? "Signed in" : $"Signed in as {me.Email}";

        // Named individually rather than as one "admin" flag: they are three different things on
        // the server and collapsing them here would be the client inventing a rule.
        var roles = new List<string>();
        if (me?.IsSuperAdmin == true) roles.Add("SuperAdmin");
        if (me?.IsAdmin == true) roles.Add("Admin");
        if (me?.IsModerator == true) roles.Add("Moderator");

        Roles.Text = roles.Count == 0
            ? "No site-wide roles."
            : "Site-wide roles: " + string.Join(", ", roles);
    }

    private async void OnSignOut(object? sender, EventArgs e) => await _session.SignOutAsync();
}
