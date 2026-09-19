namespace Ben.Service.Models.Admin;

/// <summary>
/// When one account last arrived, and how many times it has.
/// </summary>
/// <remarks>
/// <para><b>Its own record, not two more fields on <c>AppUserRecord</c>.</b> That record is read
/// on pages all over the site and by the iPhone app; carrying a last-sign-in and a count on it
/// would put a correlated subquery on every one of those reads to answer a question only one
/// admin screen asks. This is fetched once, by the screen that wants it.</para>
///
/// <para><b>Successful arrivals only.</b> A failed attempt is kept — a rise in them is the signal
/// the dashboard exists for — but it is not somebody logging in, and counting it here would make
/// a person under attack look like the site's most active user.</para>
/// </remarks>
/// <param name="AppUserId">The account.</param>
/// <param name="LastUtc">Its most recent successful sign-in, in UTC.</param>
/// <param name="Count">
/// How many successful sign-ins it has, across every method — password, Apple, the editor
/// handoff, and one per twelve-hour Microsoft visit.
/// </param>
public sealed record UserSignInSummary(Guid AppUserId, DateTime LastUtc, int Count);
