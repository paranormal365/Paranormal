using Ben.Service.Models.Entities;

namespace Ben.Web.Website.Library.Organization.Public;

/// <summary>
/// The name, email and phone a booking form is collecting for the organizer (item 235 slice 11d).
/// </summary>
/// <remarks>
/// The server decides what is enough and says so in words. This only keeps the button from being
/// pressed on an obviously empty form, with the same rule of thumb for a phone: seven to fifteen
/// digits, however they are written.
/// </remarks>
public sealed class BookingContactDraft
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";

    /// <summary>Whether the account already had all of it, so the form can start folded away.</summary>
    public bool CameFromTheAccount { get; private set; }

    /// <summary>Fills what is empty from the account. Never overwrites something typed.</summary>
    public void Fill(BookingContactRecord? account)
    {
        if (account is null) return;

        if (FirstName.Length == 0) FirstName = account.FirstName?.Trim() ?? "";
        if (LastName.Length == 0) LastName = account.LastName?.Trim() ?? "";
        if (Email.Length == 0) Email = account.Email?.Trim() ?? "";
        if (Phone.Length == 0) Phone = account.Phone?.Trim() ?? "";

        CameFromTheAccount = IsEnough(withEmail: false);
    }

    public bool IsEnough(bool withEmail)
        => FirstName.Trim().Length > 0
        && LastName.Trim().Length > 0
        && (!withEmail || Email.Contains('@'))
        && Phone.Count(char.IsAsciiDigit) is >= 7 and <= 15;

    /// <summary>A trimmed value, or null for an empty one, as the requests want them.</summary>
    public static string? Sent(string value) => value.Trim() is { Length: > 0 } v ? v : null;
}
