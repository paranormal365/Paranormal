using System.Text;

namespace Ben.Web.Website.Library.Organization.Cms;

/// <summary>
/// Titles to start a page from, for somebody looking at an empty Title box (Ben, 10/09/2026: "type in the
/// title or also be able to select from a list if they need some ideas").
/// </summary>
/// <remarks>
/// Pages the groups on the site actually need: an investigation group's, a tour company's, and a venue's —
/// the haunted hotel that hosts the other two. A title is only a start; the person can change it, and the
/// address follows the title until they edit the address themselves.
/// </remarks>
public static class CmsPageIdeas
{
    /// <summary>One idea: the title, and a few words on what such a page is for.</summary>
    public sealed record Idea(string Title, string What);

    /// <summary>A heading in the ideas list and the ideas under it.</summary>
    public sealed record Group(string Heading, IReadOnlyList<Idea> Ideas);

    public static IReadOnlyList<Group> All { get; } =
    [
        new("About you",
        [
            new("About us", "Who you are and how you work"),
            new("Our team", "The people behind the group"),
            new("Join us", "How to become a member"),
            new("Our equipment", "The gear you take out"),
            new("Frequently asked questions", "What people ask before they get in touch"),
        ]),
        new("Your work",
        [
            new("Our investigations", "Places you have looked into"),
            new("Evidence", "What you have recorded and found"),
            new("Request an investigation", "How a client asks you to come out"),
            new("Ghost stories", "The accounts people have shared with you"),
        ]),
        new("Events and visits",
        [
            new("Upcoming events", "Nights the public can book"),
            new("Ghost tours", "Walks and tours, and how to come along"),
            new("Private bookings", "Hosting a group of your own"),
        ]),
        new("A place",
        [
            new("History of the property", "The building's story, from the start"),
            new("Rooms", "Where guests stay, and what each room is known for"),
            new("Reported activity", "What has been seen and heard, and where"),
            new("House rules", "What guests and investigators agree to"),
            new("Getting here", "Directions, parking and what to bring"),
        ]),
        new("Keeping in touch",
        [
            new("Contact us", "How to reach you"),
            new("In the news", "Press, podcasts and TV"),
            new("Kind words", "What visitors and clients have said"),
        ]),
    ];

    /// <summary>
    /// A page address from a title: lowercase letters and numbers, words joined by hyphens —
    /// "History of the Property!" becomes <c>history-of-the-property</c>.
    /// </summary>
    public static string SlugFrom(string? title)
    {
        var slug = new StringBuilder();
        foreach (var c in (title ?? "").Trim().ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }
        return slug.ToString().Trim('-');
    }
}
