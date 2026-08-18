using System.Reflection;
using Telerik.SvgIcons;

namespace Ben.Web.Website.Library.Manage.Icon;

// ── Telerik icon entry (enumerated via reflection at startup) ─────────────────

public sealed record TelerikIconEntry(string Name, ISvgIcon Icon);

// ── Bootstrap icon names ──────────────────────────────────────────────────────

public static class IconPickerData
{
    /// <summary>Lazily-enumerated list of all Telerik SvgIcon static properties.</summary>
    private static IReadOnlyList<TelerikIconEntry>? _telerikCache;

    public static IReadOnlyList<TelerikIconEntry> TelerikIcons =>
        _telerikCache ??= typeof(SvgIcon)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => typeof(ISvgIcon).IsAssignableFrom(p.PropertyType))
            .Select(p => new TelerikIconEntry(p.Name, (ISvgIcon)p.GetValue(null)!))
            .OrderBy(e => e.Name)
            .ToList();

    // ── Bootstrap Icons (bi bi-*) ─────────────────────────────────────────────

    public static readonly IReadOnlyList<string> BootstrapIcons =
    [
        "alarm","app","archive","arrow-bar-down","arrow-bar-left","arrow-bar-right","arrow-bar-up",
        "arrow-clockwise","arrow-counterclockwise","arrow-down","arrow-down-circle","arrow-down-left",
        "arrow-down-right","arrow-left","arrow-left-circle","arrow-repeat","arrow-return-left",
        "arrow-return-right","arrow-right","arrow-right-circle","arrow-up","arrow-up-circle",
        "arrow-up-right","arrows-move","aspect-ratio","asterisk","at","award",
        "bag","bag-check","bag-dash","bag-fill","bag-plus","bag-x","balloon","balloon-heart",
        "bar-chart","bar-chart-fill","bar-chart-line","bar-chart-steps","battery","battery-charging",
        "battery-full","battery-half","bell","bell-fill","bell-slash","bluetooth","bookmark",
        "bookmark-fill","book","book-fill","book-half","bookmarks","box","box-arrow-down",
        "box-arrow-up","box-seam","broadcast","brush","bug","building","building-check",
        "bullseye","calculator","calendar","calendar-check","calendar-event","calendar-fill",
        "calendar-range","camera","camera-fill","camera-reels","camera-video","capslock",
        "card-checklist","card-heading","card-image","card-list","card-text","cart",
        "cart-check","cart-fill","cart-plus","cash","cash-coin","chat","chat-dots",
        "chat-dots-fill","chat-fill","chat-left","chat-right","chat-square","check",
        "check-circle","check-circle-fill","check-lg","check-square","check2","check2-all",
        "check2-circle","check2-square","chevron-down","chevron-left","chevron-right","chevron-up",
        "circle","clock","clock-fill","cloud","cloud-arrow-down","cloud-arrow-up","cloud-check",
        "cloud-download","cloud-fill","cloud-upload","code","code-slash","code-square",
        "collection","columns","columns-gap","compass","compass-fill","cone","cone-striped",
        "controller","copy","cpu","crop","currency-dollar","currency-euro","cursor",
        "dash","dash-circle","database","database-fill","diagram-3","display","door-closed",
        "door-open","download","droplet","droplet-fill","earbuds","egg","emoji-angry",
        "emoji-expressionless","emoji-heart-eyes","emoji-laughing","emoji-neutral","emoji-smile",
        "emoji-sunglasses","emoji-wink","envelope","envelope-fill","envelope-open","eraser",
        "exclude","eye","eye-fill","eye-slash","file","file-arrow-down","file-arrow-up",
        "file-bar-graph","file-check","file-code","file-earmark","file-earmark-code",
        "file-earmark-image","file-earmark-music","file-earmark-pdf","file-earmark-text",
        "file-earmark-word","file-earmark-zip","file-image","file-music","file-pdf",
        "file-person","file-play","file-plus","file-richtext","file-text","file-word","file-zip",
        "files","film","filter","flag","flag-fill","folder","folder-check","folder-fill",
        "folder-minus","folder-open","folder-plus","folder-symlink","folder-x","fonts",
        "forward","funnel","funnel-fill","gear","gear-fill","gear-wide","geo-alt","geo-fill",
        "gift","gift-fill","globe","globe-americas","globe-europe-africa","graph-down",
        "graph-up","grid","grid-1x2","grid-3x2","grid-fill","hammer","hand-index",
        "hand-thumbs-down","hand-thumbs-up","hash","headphones","heart","heart-fill",
        "house","house-door","house-door-fill","house-fill","image","inbox","info-circle",
        "info-circle-fill","input-cursor","joystick","kanban","key","keyboard","ladder",
        "layers","layout-sidebar","layout-split","lightning","lightning-charge","lightning-fill",
        "link","link-45deg","list","list-check","list-ol","list-ul","lock","lock-fill",
        "mailbox","map","map-fill","megaphone","mic","mic-fill","mic-mute","minecart",
        "modem","moon","mouse","music-note","music-note-beamed","music-note-list",
        "newspaper","node-minus","node-plus","nurse","palette","palette-fill","paperclip",
        "patch-check","patch-plus","patch-question","pause","pause-circle","paypal",
        "pen","pencil","pencil-fill","pencil-square","people","people-fill",
        "person","person-badge","person-check","person-circle","person-dash","person-fill",
        "person-gear","person-lines-fill","person-lock","person-plus","person-raised-hand",
        "person-vcard","person-workspace","phone","phone-fill","pie-chart","pie-chart-fill",
        "pin","pin-angle","pin-fill","play","play-btn","play-circle","play-fill","plug",
        "plus","plus-circle","plus-circle-fill","plus-lg","plus-square","power","printer",
        "puzzle","question","question-circle","question-circle-fill","question-lg",
        "question-octagon","quote","reception-4","record","recycle","reply","robot",
        "rocket","rss","rss-fill","rulers","save","save-fill","scissors","search",
        "send","server","share","shield","shield-check","shield-fill","shop","shop-window",
        "shuffle","signal","skip-backward","skip-forward","skip-start","sliders","sliders2",
        "smartwatch","snow","speedometer","speedometer2","square","stack","star","star-fill",
        "star-half","stars","stop","stop-circle","stopwatch","stopwatch-fill","stoplights",
        "suit-club","suit-diamond","suit-heart","suit-spade","sunglasses","symmetry-horizontal",
        "table","tablet","tag","tag-fill","tags","tags-fill","telephone","telephone-fill",
        "terminal","terminal-fill","text-left","text-right","thermometer","toggle-off","toggle-on",
        "tools","translate","trash","trash-fill","trash2","trophy","trophy-fill","truck",
        "tv","twitch","twitter","type","upc","upload","usb","vector-pen","vinyl",
        "voicemail","volume-down","volume-mute","volume-up","wallet","watch","wifi","window",
        "wrench","wrench-adjustable","x","x-circle","x-circle-fill","x-lg","x-octagon","x-square",
        "youtube","zoom-in","zoom-out",
    ];

    // ── Font Awesome icon names (fa-*) ────────────────────────────────────────

    public static readonly IReadOnlyList<string> FontAwesomeIcons =
    [
        // Arrows / navigation
        "arrow-down","arrow-left","arrow-right","arrow-up","arrows-alt","chevron-down",
        "chevron-left","chevron-right","chevron-up","caret-down","caret-left","caret-right",
        "caret-up","sort","sort-down","sort-up","angle-double-down","angle-double-left",
        "angle-double-right","angle-double-up","angle-down","angle-left","angle-right",
        "angle-up","long-arrow-alt-down","long-arrow-alt-left","long-arrow-alt-right",
        "long-arrow-alt-up","reply","reply-all","share","exchange-alt","sync","redo","undo",
        // People / communication
        "user","users","user-plus","user-minus","user-edit","user-check","user-cog",
        "user-circle","user-lock","user-shield","user-tag","user-tie","address-book",
        "address-card","id-badge","id-card","phone","phone-alt","phone-slash","mobile-alt",
        "fax","envelope","envelope-open","envelope-open-text","at","comment","comments",
        "sms","voicemail","bell","bell-slash",
        // Files / documents
        "file","file-alt","file-code","file-contract","file-csv","file-excel","file-export",
        "file-image","file-import","file-invoice","file-invoice-dollar","file-medical",
        "file-pdf","file-powerpoint","file-prescription","file-signature","file-upload",
        "file-video","file-word","file-archive","file-audio","file-download","file-medical-alt",
        "folder","folder-open","folder-plus","folder-minus","copy","paste","cut","save",
        "archive","book","book-open","books","bookmark","newspaper","sticky-note",
        // UI / interface
        "bars","th","th-large","th-list","list","list-alt","list-ol","list-ul","filter",
        "search","search-plus","search-minus","home","plus","minus","times","check",
        "circle","square","dot-circle","minus-circle","plus-circle","times-circle",
        "check-circle","info-circle","exclamation-circle","question-circle","exclamation-triangle",
        "ban","eye","eye-slash","lock","lock-open","unlock","unlock-alt","trash","trash-alt",
        "edit","pen","pen-alt","pen-fancy","pen-square","pencil-alt","eraser","paint-brush",
        "highlighter","magic","wrench","tools","cog","cogs","sliders-h","toggle-off","toggle-on",
        // Navigation / maps
        "map","map-marker","map-marker-alt","map-signs","compass","globe","globe-americas",
        "globe-europe","globe-africa","globe-asia","location-arrow","directions",
        "road","route","signs-post","street-view","crosshairs","flag","flag-checkered",
        "thumbtack","paper-plane",
        // Media / entertainment
        "play","pause","stop","forward","backward","fast-forward","fast-backward","step-forward",
        "step-backward","eject","music","headphones","volume-up","volume-down","volume-mute",
        "volume-off","microphone","microphone-alt","microphone-slash","podcast","radio",
        "record-vinyl","compact-disc","drum","drum-steelpan","guitar","broadcast-tower",
        "film","video","video-slash","camera","camera-retro","photo-video","image","images",
        "portrait","id-card-alt","tv","desktop","laptop","tablet-alt","mobile","qrcode","barcode",
        // Commerce / finance
        "shopping-cart","shopping-bag","shopping-basket","store","store-alt",
        "dollar-sign","euro-sign","pound-sign","yen-sign","ruble-sign","rupee-sign",
        "credit-card","money-bill","money-bill-alt","money-bill-wave","money-check",
        "cash-register","receipt","percent","tag","tags","wallet","piggy-bank","coins",
        "hand-holding-usd","chart-bar","chart-line","chart-pie","chart-area","poll",
        // Technology
        "server","database","hdd","terminal","code","code-branch","bug","robot",
        "microchip","memory","ethernet","network-wired","wifi","bluetooth","usb",
        "keyboard","mouse","desktop","laptop-code","print","scanner","plug","battery-full",
        "battery-half","battery-empty","power-off","cloud","cloud-upload-alt","cloud-download-alt",
        "cloud-sun","cloud-moon","upload","download","share-alt","link","unlink","external-link-alt",
        "rss","rss-square","at","hashtag","lock","unlock","key","fingerprint","shield",
        "shield-alt","shield-check","shield-virus","lock-open",
        // Social / misc
        "star","star-half","star-half-alt","heart","heart-broken","thumbs-up","thumbs-down",
        "smile","laugh","grin","meh","frown","sad-tear","angry","surprise","dizzy","tired",
        "kiss","kiss-wink-heart","grin-hearts","grin-stars","grin-tongue-wink",
        "trophy","award","medal","crown","gem","diamond","fire","snowflake","sun","moon",
        "cloud-sun","cloud-moon","rainbow","wind","water","leaf","tree","seedling",
        "apple-alt","carrot","pepper-hot","pizza-slice","ice-cream","coffee","mug-hot",
        "utensils","hamburger","hotdog","birthday-cake","wine-glass","cocktail","beer",
        // Actions / tools
        "cut","crop","compress","expand","zoom-in","zoom-out","random","recycle",
        "history","clock","calendar","calendar-alt","calendar-check","calendar-times",
        "calendar-week","calendar-day","stopwatch","hourglass","hourglass-start",
        "hourglass-half","hourglass-end","alarm-clock","binoculars","calculator",
        "ruler","ruler-combined","ruler-horizontal","ruler-vertical","palette","swatchbook",
        "fill-drip","brush","pen-ruler","pencil-ruler","scissors","object-group",
        "object-ungroup","layer-group","draw-polygon","bezier-curve","cubes","cube",
        "box","boxes","box-open","dolly","dolly-flatbed","pallet","warehouse","industry",
        "hammer","screwdriver","wrench","toolbox","first-aid","stethoscope","hospital",
        "hospital-alt","clinic-medical","ambulance","capsules","pills","prescription",
        "syringe","thermometer","weight","lungs","brain","tooth","bone","eye-dropper",
        "microscope","flask","vial","vials","dna","virus","radiation","biohazard",
    ];
}
