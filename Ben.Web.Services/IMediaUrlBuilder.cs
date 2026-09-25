namespace Ben.Web.Services;

/// <summary>
/// Where the browser should fetch a file's picture or bytes from.
/// </summary>
/// <remarks>
/// Components in the shared library need these URLs but must not know how the host authenticates
/// them, so the interface lives here and the implementation sits in the website.
///
/// <para><b>Use this instead of fetching file bytes in a component</b> whenever more than one file
/// can appear at once. Fetching puts a copy of each file in this process's memory and another,
/// larger, base64 copy in the page.</para>
/// </remarks>
public interface IMediaUrlBuilder
{
    /// <summary>A shrunken picture of the file — for tiles, avatars and grids.</summary>
    string Thumbnail(Guid fileId);

    /// <summary>The file itself, streamed — for audio, video and downloads.</summary>
    string Download(Guid fileId);

    /// <summary>
    /// A recording belonging to an uploaded field session.
    /// </summary>
    /// <remarks>
    /// Its own route because a session's files are served by the session endpoint, which checks
    /// access against the investigation rather than the file's own audience.
    /// </remarks>
    string FieldSessionFile(Guid sessionId, Guid fileId);

    /// <summary>
    /// A recording reached through a share link, for a viewer with no account (item 207).
    /// </summary>
    /// <remarks>
    /// No ticket, because there is no token to put in one: the share token IS the authority, and
    /// the API re-checks its expiry, its revocation and which file it covers on every request. The
    /// session id is deliberately absent from the URL — a recipient's browser should hold a string
    /// that names a share row, never one that names a session.
    /// </remarks>
    string SharedFieldSessionFile(string shareToken, Guid fileId);

    /// <summary>
    /// One of a hosted event's files (item 235 phase 11).
    /// </summary>
    /// <remarks>
    /// Its own route because the event decides who may have it — staff, confirmed guests, or anybody
    /// — and a signed-in guest's ticket has to reach that check with their token inside it.
    /// </remarks>
    string EventFile(Guid eventId, Guid fileId);

    /// <summary>A photo or video posted in an event's room, for the people in it (item 235 phase 11).</summary>
    string EventRoomMedia(Guid eventId, Guid messageId);

    /// <summary>A picture from an event's public gallery — anonymous, like a tour's (item 235 phase 11).</summary>
    string EventPhoto(Guid uploadFileId);

    /// <summary>A picture a venue kept in its photo library, for its public page (item 235 phase 12).</summary>
    string VenuePhoto(Guid uploadFileId);

    /// <summary>
    /// The chosen files, pictures and photos of an event as one zip (item 235 phase 12). Not cached: every
    /// choice is a different address.
    /// </summary>
    string EventKeepZip(Guid orgId, Guid eventId, IReadOnlyCollection<Guid> uploadFileIds);

    /// <summary>
    /// A tour guide's published photograph (item 233).
    /// </summary>
    /// <remarks>
    /// No ticket: this one is anonymous by design. Ben asked that a guest be shown who is leading
    /// their walk, and that guest may have no account at all — the picture has to render for
    /// somebody reading an email.
    /// </remarks>
    string GuidePhoto(Guid uploadFileId);

    /// <summary>A picture from a tour's gallery. Anonymous, for the same reason.</summary>
    string TourPhoto(Guid uploadFileId);

    /// <summary>
    /// A store picture — a category's, a product's, or the one an order line was bought with
    /// (storefront). Anonymous, and served whether or not the shop is switched on.
    /// </summary>
    string StoreImage(Guid uploadFileId, bool thumbnail = false);

    /// <summary>
    /// A product's file for the people who keep it — the store's staff and the product's seller (store
    /// sellers P11). Private files included, so the viewer's ticket is required.
    /// </summary>
    string StoreProductFile(Guid fileId);

    /// <summary>A store product's video (store sellers P14). Anonymous, like the pictures.</summary>
    string StoreVideo(Guid uploadFileId);

    /// <summary>
    /// A file a buyer downloads from their paid order (store sellers P11). The order's own link token
    /// travels as well, for a guest with no account; the API checks paid, not cancelled and not refunded.
    /// </summary>
    string StoreOrderDownload(Guid orderId, Guid fileId, string? orderToken);
}
