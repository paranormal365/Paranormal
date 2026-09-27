using System.Reflection;
using Telerik.SvgIcons;

namespace Ben.Web.Website.Library.Manage.Icon;

// ── Telerik icon entry (enumerated via reflection at startup) ─────────────────

public sealed record TelerikIconEntry(string Name, ISvgIcon Icon);

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
        "microchip","memory","ethernet","network-wired","wifi","bluetooth","usb-drive",
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
        "cut","crop","compress","expand","magnifying-glass-plus","magnifying-glass-minus","random","recycle",
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
