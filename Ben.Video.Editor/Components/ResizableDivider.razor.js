// ResizableDivider.razor.js
// Handles pointer-drag resizing. Called from ResizableDivider.razor via JS isolation.
//
// initDivider(el, dotNetRef, direction)
//   el          – the divider <div> element
//   dotNetRef   – DotNetObjectReference to ResizableDivider.razor
//   direction   – "horizontal" (left/right, changes width of prev sibling)
//               | "vertical"  (up/down,   changes height of prev sibling)

export function initDivider(el, dotNetRef, direction) {
    let dragging = false;
    let startPos = 0;
    let startSize = 0;

    const isH = direction === "horizontal";

    el.addEventListener("pointerdown", e => {
        if (e.button !== 0) return;
        dragging = true;
        startPos = isH ? e.clientX : e.clientY;

        const sibling = el.previousElementSibling;
        if (!sibling) { dragging = false; return; }
        const rect = sibling.getBoundingClientRect();
        startSize = isH ? rect.width : rect.height;

        el.setPointerCapture(e.pointerId);
        e.preventDefault();
    });

    el.addEventListener("pointermove", e => {
        if (!dragging) return;
        const delta   = (isH ? e.clientX : e.clientY) - startPos;
        const newSize = Math.round(startSize + delta);
        dotNetRef.invokeMethodAsync("OnDragAsync", newSize);
    });

    el.addEventListener("pointerup",    () => { dragging = false; });
    el.addEventListener("pointercancel", () => { dragging = false; });
}

export function dispose(el) {
    // pointer-capture is released automatically; nothing additional needed
}
