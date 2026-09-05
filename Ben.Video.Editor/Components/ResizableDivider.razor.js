// ResizableDivider.razor.js
// Handles pointer-drag resizing. Called from ResizableDivider.razor via JS isolation.
//
// initDivider(el, dotNetRef, direction, target)
//   el          – the divider <div> element
//   dotNetRef   – DotNetObjectReference to ResizableDivider.razor
//   direction   – "horizontal" (left/right, changes a sibling's width)
//               | "vertical"  (up/down,   changes a sibling's height)
//   target      – "previous" (default) or "next": which neighbour is resized. Dragging toward a
//                 "next" target shrinks it, so the delta is inverted for that case.

export function initDivider(el, dotNetRef, direction, target) {
    // The element can already be gone. Blazor resolves an ElementReference at call time, and these
    // dividers are rendered per track inside a loop, so a re-render between OnAfterRenderAsync
    // firing and the interop landing — restoring a saved project adds tracks, which is exactly
    // that — hands us null. Unguarded, that threw inside the render loop and the WebAssembly host
    // showed "An unhandled error has occurred" on load, with no way to dismiss it but Reload
    // (2026-09-05). Nothing is lost by returning: the divider that replaced this one runs its own
    // init.
    if (!el) return;

    let dragging = false;
    let startPos = 0;
    let startSize = 0;

    const isH = direction === "horizontal";
    const resizesNext = target === "next";

    el.addEventListener("pointerdown", e => {
        if (e.button !== 0) return;
        dragging = true;
        startPos = isH ? e.clientX : e.clientY;

        const sibling = resizesNext ? el.nextElementSibling : el.previousElementSibling;
        if (!sibling) { dragging = false; return; }
        const rect = sibling.getBoundingClientRect();
        startSize = isH ? rect.width : rect.height;

        el.setPointerCapture(e.pointerId);
        e.preventDefault();
    });

    el.addEventListener("pointermove", e => {
        if (!dragging) return;
        const raw     = (isH ? e.clientX : e.clientY) - startPos;
        // Toward a "next" neighbour is away from its far edge, so the sign flips.
        const delta   = resizesNext ? -raw : raw;
        const newSize = Math.round(startSize + delta);
        dotNetRef.invokeMethodAsync("OnDragAsync", newSize);
    });

    el.addEventListener("pointerup",    () => { dragging = false; });
    el.addEventListener("pointercancel", () => { dragging = false; });
}

export function dispose(el) {
    if (!el) return;
    // pointer-capture is released automatically; nothing additional needed
}
