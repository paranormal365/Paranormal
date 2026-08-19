// Resolves this library's JS modules against the page's own base, so the editor works wherever it
// is mounted.
//
// Every module here used to be imported by its root-absolute path — "/_content/Ben.Video.Editor/
// js/domInterop.js". That is correct only when the app is served from the root of its origin. The
// production editor is served from a sub-path (https://ishaunted.com/editor/), because a sub-path
// inherits the site's certificate and a subdomain would need its own, and there every one of those
// paths asks the site root for a file that lives under /editor. The files are published and
// present; the URLs simply point past them.
//
// The fix belongs in the browser rather than in C#: document.baseURI is exactly the <base href>
// the host page declares, so resolving against it is right for both hosts and for any sub-path,
// with nothing to configure and nothing to keep in sync. Callers pass a path relative to this
// library's static web assets ("js/domInterop.js"), never a leading slash.
//
// Loaded as a classic script from <head>, so it exists before any component's first import.
window.benImportEditorModule = function (relativePath) {
    return import(new URL('_content/Ben.Video.Editor/' + relativePath, document.baseURI).href);
};
