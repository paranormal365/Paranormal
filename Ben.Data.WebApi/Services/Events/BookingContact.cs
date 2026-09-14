using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// The name and number every booking on the web carries, so an organizer can reach the person
/// behind it (item 235 slice 11d).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-13: <i>"I don't think it would be asking too much for First Last Name, E-mail
/// Address, Phone Number required so the event organizer can contact them to make arrangements for
/// collecting fees. We can let them know during sign up that we only collect their information for
/// the event organization."</i></para>
///
/// <para><b>Asked only for what the profile lacks.</b> A signed-in guest whose account already has a
/// name and a phone sends nothing and is not refused; the form shows them what will be passed on.
/// </para>
///
/// <para><b>The phone goes on the booking, never onto the account.</b> It was given to one venue for
/// one weekend. A name is different — an account with no name is one nobody can greet — so an empty
/// first or last name on the profile is filled from what they typed.</para>
///
/// <para><b>Not required of the shipped phone app's RSVP or of a host booking for somebody</b>: the
/// first cannot send it and the second is the venue writing down a person it is already talking to.
/// </para>
/// </remarks>
public static class BookingContact
{
    public const int NameMaxLength = 100;
    public const int PhoneMaxLength = 40;

    /// <summary>What a guest is told at the moment they give the three.</summary>
    public static string Disclosure(string? organizationName)
        => $"Your name, email address and phone number go to {organizationName ?? "the organizer"} for "
         + "this event only, so they can reach you about your booking. We don't use them for anything else.";

    /// <param name="FirstName">Trimmed.</param>
    /// <param name="Phone">Trimmed, as typed.</param>
    public sealed record Details(string FirstName, string LastName, string Phone);

    /// <summary>
    /// Why these three are not enough to reach somebody, or null when they are.
    /// </summary>
    /// <remarks>
    /// <b>A phone is judged by its digits</b>, seven to fifteen of them, because people write numbers
    /// every way there is — "(615) 555-0100", "+44 20 7946 0958" — and refusing one of those for its
    /// brackets is a form being difficult about something it cannot check anyway.
    /// </remarks>
    public static string? WhyNotEnough(string? firstName, string? lastName, string? phone)
    {
        if (Trimmed(firstName) is not { } first) return "Add your first name, so the organizer knows who is coming.";
        if (Trimmed(lastName) is not { } last) return "Add your last name, so the organizer knows who is coming.";
        if (first.Length > NameMaxLength || last.Length > NameMaxLength) return "That name is too long.";
        if (Trimmed(phone) is not { } number) return "Add a phone number, so the organizer can reach you about your booking.";
        if (!LooksLikeAPhone(number)) return "That phone number doesn't look right. Include the area code.";
        return null;
    }

    /// <summary>Seven to fifteen digits, and nothing but the marks people write numbers with.</summary>
    public static bool LooksLikeAPhone(string phone)
    {
        if (phone.Length > PhoneMaxLength) return false;
        if (phone.Any(c => !(char.IsAsciiDigit(c) || c is '+' or '(' or ')' or '-' or '.' or ' '))) return false;
        var digits = phone.Count(char.IsAsciiDigit);
        return digits is >= 7 and <= 15;
    }

    /// <summary>
    /// The three for a signed-in guest: what they typed, else what their account already says.
    /// </summary>
    /// <returns>The details, or the sentence saying what is missing.</returns>
    public static async Task<(Details? Details, string? Refusal)> ForAccountAsync(
        BenDataContext db, Guid userId, string? firstName, string? lastName, string? phone,
        CancellationToken ct)
    {
        var user = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.FirstName,
                u.LastName,
                u.PhoneNumber,
                Listed = u.UserPhones
                    .OrderByDescending(p => p.IsPrimary)
                    .Select(p => p.PhoneNumber)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct);

        var first = Trimmed(firstName) ?? Trimmed(user?.FirstName);
        var last = Trimmed(lastName) ?? Trimmed(user?.LastName);
        var number = Trimmed(phone) ?? Trimmed(user?.Listed) ?? Trimmed(user?.PhoneNumber);

        if (WhyNotEnough(first, last, number) is { } why) return (null, why);
        return (new Details(first!, last!, number!), null);
    }

    /// <summary>
    /// Fills an empty first or last name on the account from what the guest typed. Never the phone.
    /// </summary>
    public static async Task FillEmptyNamesAsync(
        BenDataContext db, Guid userId, Details details, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return;

        if (Trimmed(user.FirstName) is null) user.FirstName = details.FirstName;
        if (Trimmed(user.LastName) is null) user.LastName = details.LastName;
    }

    internal static string? Trimmed(string? value)
        => value?.Trim() is { Length: > 0 } v ? v : null;
}
