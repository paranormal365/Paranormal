namespace Ben.Web.Library.Organization.Cms;

/// <summary>
/// One ready-made block an author can drop into a page and fill in.
/// </summary>
/// <param name="Name">What it is called in the picker.</param>
/// <param name="Description">One line saying what it is for.</param>
/// <param name="Build">
/// Produces the markup. Takes a per-insertion unique suffix, because the interactive blocks are
/// wired together by <c>id</c>.
/// </param>
public sealed record CmsSnippet(string Name, string Description, Func<string, string> Build);

/// <summary>
/// The palette of blocks the CMS editor offers.
/// </summary>
/// <remarks>
/// <para>Ben's ask, in his words: <i>"They can pick from a list and it adds to their html editor
/// where it places it in for them to fill in the parts."</i> These are look-and-behaviour helpers —
/// a card, a collapsible list, a carousel — not a page builder. The output is ordinary markup in a
/// section type that already renders markup, so nothing on the public side has to change.</para>
///
/// <para><b>Every id is made unique per insertion.</b> Bootstrap's collapsibles and carousels find
/// each other through <c>id</c> and <c>data-bs-target</c>. Two carousels built from one snippet
/// would otherwise share ids and drive each other — the reader clicks one and the other moves,
/// which looks like a browser bug rather than a content mistake and is miserable to diagnose.</para>
///
/// <para>The classes are Bootstrap's, which the public pages already load. A snippet that needed
/// its own stylesheet would look right in the editor and wrong on the site.</para>
///
/// <para>Placeholder text is deliberately instructional rather than lorem ipsum: an author who
/// forgets to replace a heading ships "Card title", which is obviously unfinished, instead of a
/// paragraph of Latin that looks deliberate.</para>
/// </remarks>
public static class CmsSnippets
{
    public static IReadOnlyList<CmsSnippet> All { get; } =
    [
        new("Card",
            "A titled box with a body — good for one idea at a time.",
            _ => """
                 <div class="card mb-3">
                   <div class="card-body">
                     <h5 class="card-title">Card title</h5>
                     <p class="card-text">Replace this with what the card is about.</p>
                   </div>
                 </div>
                 """),

        new("Card with header",
            "A card with a coloured strip across the top.",
            _ => """
                 <div class="card mb-3">
                   <div class="card-header">Header</div>
                   <div class="card-body">
                     <h5 class="card-title">Card title</h5>
                     <p class="card-text">Replace this with what the card is about.</p>
                   </div>
                 </div>
                 """),

        new("Collapsible list",
            "Questions or headings that open one at a time. Good for an FAQ.",
            id => $"""
                   <div class="accordion mb-3" id="acc{id}">
                     <div class="accordion-item">
                       <h2 class="accordion-header">
                         <button class="accordion-button" type="button" data-bs-toggle="collapse" data-bs-target="#acc{id}-one" aria-expanded="true" aria-controls="acc{id}-one">
                           First heading
                         </button>
                       </h2>
                       <div id="acc{id}-one" class="accordion-collapse collapse show" data-bs-parent="#acc{id}">
                         <div class="accordion-body">Replace this with the first answer.</div>
                       </div>
                     </div>
                     <div class="accordion-item">
                       <h2 class="accordion-header">
                         <button class="accordion-button collapsed" type="button" data-bs-toggle="collapse" data-bs-target="#acc{id}-two" aria-expanded="false" aria-controls="acc{id}-two">
                           Second heading
                         </button>
                       </h2>
                       <div id="acc{id}-two" class="accordion-collapse collapse" data-bs-parent="#acc{id}">
                         <div class="accordion-body">Replace this with the second answer.</div>
                       </div>
                     </div>
                   </div>
                   """),

        new("Carousel",
            "Slides the reader steps through. Replace each slide's image and caption.",
            id => $"""
                   <div id="car{id}" class="carousel slide mb-3" data-bs-ride="carousel">
                     <div class="carousel-inner">
                       <div class="carousel-item active">
                         <img src="" class="d-block w-100" alt="Describe the first image">
                         <div class="carousel-caption d-none d-md-block">
                           <h5>First slide</h5>
                           <p>Replace this caption.</p>
                         </div>
                       </div>
                       <div class="carousel-item">
                         <img src="" class="d-block w-100" alt="Describe the second image">
                         <div class="carousel-caption d-none d-md-block">
                           <h5>Second slide</h5>
                           <p>Replace this caption.</p>
                         </div>
                       </div>
                     </div>
                     <button class="carousel-control-prev" type="button" data-bs-target="#car{id}" data-bs-slide="prev">
                       <span class="carousel-control-prev-icon" aria-hidden="true"></span>
                       <span class="visually-hidden">Previous</span>
                     </button>
                     <button class="carousel-control-next" type="button" data-bs-target="#car{id}" data-bs-slide="next">
                       <span class="carousel-control-next-icon" aria-hidden="true"></span>
                       <span class="visually-hidden">Next</span>
                     </button>
                   </div>
                   """),

        new("Two columns",
            "Side by side on a wide screen, stacked on a phone.",
            _ => """
                 <div class="row g-3 mb-3">
                   <div class="col-md-6">
                     <h5>Left heading</h5>
                     <p>Replace this with the left-hand content.</p>
                   </div>
                   <div class="col-md-6">
                     <h5>Right heading</h5>
                     <p>Replace this with the right-hand content.</p>
                   </div>
                 </div>
                 """),

        new("Three across",
            "Three equal blocks — services, team members, anything that comes in threes.",
            _ => """
                 <div class="row g-3 mb-3">
                   <div class="col-md-4">
                     <h5>First</h5>
                     <p>Replace this.</p>
                   </div>
                   <div class="col-md-4">
                     <h5>Second</h5>
                     <p>Replace this.</p>
                   </div>
                   <div class="col-md-4">
                     <h5>Third</h5>
                     <p>Replace this.</p>
                   </div>
                 </div>
                 """),

        new("Callout",
            "A tinted box for something the reader should not skip.",
            _ => """
                 <div class="alert alert-info mb-3" role="alert">
                   <h5 class="alert-heading">Worth knowing</h5>
                   <p class="mb-0">Replace this with the thing you want to stand out.</p>
                 </div>
                 """),

        new("Button link",
            "A link styled as a button — a booking form, a contact page.",
            _ => """
                 <p class="mb-3"><a class="btn btn-primary" href="#">Change this label</a></p>
                 """),
    ];

    /// <summary>
    /// The markup for one snippet, with fresh ids.
    /// </summary>
    /// <remarks>
    /// Eight hex characters from a fresh Guid. Short enough to stay readable in the markup an author
    /// may end up looking at, and far past any chance of two insertions colliding on one page.
    /// </remarks>
    public static string Render(CmsSnippet snippet)
        => snippet.Build(Guid.NewGuid().ToString("N")[..8]);
}
