// Small typed DOM helpers for Ben.Web.Library components, replacing hand-built `eval("…")` strings.
//
// This mirrors Ben.Video.Editor's own domInterop.js, added by that repo's phase 169 ("audit #4").
// That audit replaced all 30 of its eval call sites but could only see its own repository, so the
// equivalent calls over here were missed — this module closes that gap.
//
// The reasons were never injection: every string involved was a constant. They were
//
//   1. CSP — `eval` forces `unsafe-eval` in the script-src policy, for two element clicks.
//   2. A real, user-reachable crash class — a `getElementById(x).click()` string throws a
//      TypeError when the element isn't there, which propagates as an unhandled Blazor render
//      exception and, under Interactive Server, kills the whole circuit. The `?.` below is the
//      entire point: a missing element is a silent no-op, never a dead app.

/**
 * Click an element by id. No-op when it isn't on the page.
 *
 * Used to open a hidden <InputFile>'s native file picker from a normal button, since the file
 * input itself has to stay visually hidden but still be the thing the browser opens.
 *
 * @param {string} id
 */
export function clickElementById(id) {
    document.getElementById(id)?.click();
}
