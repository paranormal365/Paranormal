// Resolves this library's JS modules against the page's own base, so the editor works wherever it
// is mounted.
//
// Copied from Ben.Video.Editor's loader, which learned this the hard way: a root-absolute path such
// as "/_content/Ben.Video.Editor/js/domInterop.js" is right only when the app is served from the
// root of its origin. The canvas editor is served from a sub-path (https://ishaunted.com/editors/canvas/),
// because a sub-path inherits the site's certificate, and there every root-absolute path asks the
// site root for a file that lives under /editors/canvas. The files are published and present; the
// URLs simply point past them.
//
// document.baseURI is exactly the <base href> the host page declares, so resolving against it is
// right for the standalone app, for the site, and for any sub-path, with nothing to configure.
// Callers pass a path relative to this library's static web assets ("js/modalInterop.js"), never a
// leading slash - CanvasModules.ImportAsync refuses one.
//
// Loaded as a classic script from <head>, so it exists before any component's first import.
window.benImportCanvasModule = function (relativePath) {
    return import(new URL('_content/Ben.Canvas.Editor/' + relativePath, document.baseURI).href);
};
