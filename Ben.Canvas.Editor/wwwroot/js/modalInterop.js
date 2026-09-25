// Copied from Ben.Web.Website.Library/Kit/BenModal.razor.js, because Kit cannot be referenced from
// WebAssembly. Support for BcModal - only the two things the DOM genuinely owns.
//
// Note what is NOT here: no node creation, moving or removal. Bootstrap's own Modal plugin
// re-parents the dialog and injects its backdrop, which is exactly what breaks Blazor's diffing.
// Everything structural stays in the component's render tree; this module only sets a class on
// <body> and moves focus.

// Modals can nest (a confirm inside an editor). Counting means the inner one closing does not
// unlock scrolling while the outer one is still open.
let openCount = 0;

export function open(dialog) {
    openCount++;
    document.body.classList.add('modal-open');

    if (!dialog) return;

    // Focus the first genuinely focusable control, else the dialog itself so Escape reaches the
    // component's @onkeydown.
    //
    // Deferred with setTimeout rather than requestAnimationFrame: a browser does not paint a hidden
    // tab, so an rAF callback would never run and a modal opened in a background tab would stay
    // unfocused forever.
    const focusFirst = () => {
        const target = dialog.querySelector(
            'input:not([type=hidden]):not([disabled]), textarea:not([disabled]), ' +
            'select:not([disabled]), [contenteditable="true"]'
        ) || dialog;

        try {
            target.focus({ preventScroll: true });
        } catch {
            /* detached between scheduling and running - nothing to focus */
        }
    };

    setTimeout(focusFirst, 0);
}

export function close() {
    openCount = Math.max(0, openCount - 1);
    if (openCount === 0) document.body.classList.remove('modal-open');
}
