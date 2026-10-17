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

    public bool IsEnough(bool withEmail) => WhatIsMissing(withEmail, null) is null;

    /// <summary>
    /// The one thing still keeping the form from sending, in the sentence the form shows beside
    /// its button; null when there is enough.
    /// </summary>
    /// <remarks>
    /// The button used to go grey with nothing to say why: a signed-in guest whose account had a
    /// name but no phone saw the whole form filled in and a button that would not press
    /// (production, 10/16/2026). One reason at a time, in the order the fields are on the screen,
    /// so the sentence always names the next thing to do.
    /// </remarks>
    /// <param name="organizationName">Who the phone number is for, named in the sentence.</param>
    public string? WhatIsMissing(bool withEmail, string? organizationName)
    {
        var first = FirstName.Trim().Length > 0;
        var last = LastName.Trim().Length > 0;
        if (!first && !last) return "Add your first and last name.";
        if (!first) return "Add your first name.";
        if (!last) return "Add your last name.";

        if (withEmail && !Email.Contains('@'))
            return Email.Trim().Length == 0
                ? "Add your email address so we can send you the link."
                : "Check your email address. It needs an @.";

        var digits = Phone.Count(char.IsAsciiDigit);
        if (digits == 0) return $"Add a phone number so {organizationName ?? "the organizer"} can reach you.";
        if (digits is < 7 or > 15) return "Check your phone number. It needs 7 to 15 digits.";

        return null;
    }

    /// <summary>A trimmed value, or null for an empty one, as the requests want them.</summary>
    public static string? Sent(string value) => value.Trim() is { Length: > 0 } v ? v : null;
}
