// One pointer listener per plan, reporting a whole gesture once (item 235 phase 2).
//
// WHY THIS EXISTS AT ALL. Four hundred seats across three nights is twelve hundred @onclick
// handlers for Blazor Server to register, hold and diff. One delegated listener costs one.
//
// THE RULE IT KEEPS. It reads the DOM and toggles a class of its own while a drag is in flight;
// it never creates, moves or removes a node. That is what stops Blazor's diffing and the browser
// disagreeing about what is on the page — the same rule BenModal's module states, and the reason
// TelerikDialog is banned in this codebase.
//
// MOUSE AND PEN PAINT; TOUCH TAPS. A touch pointer stays bound to the element it went down on and
// never raises pointerenter on its neighbours, so a finger-drag cannot be read the way a mouse
// drag can. Rather than fake it with elementFromPoint on every move — which fights the browser's
// own scrolling on the one device where scrolling matters most — touch chooses one square per tap
// and the designer offers "Select many" for the rest. Deciding this here, from pointerType, is
// why the C# side never has to know what kind of pointer it is.

const attached = new Map();

export function attach(containerId, dotnet) {
    const container = document.getElementById(containerId);
    if (!container || attached.has(containerId)) return;

    const grid = container.querySelector('.plan__grid');
    if (!grid) return;

    // The gesture in flight. Null between gestures, which is also how a stray pointerup or a
    // pointer that left the window is ignored rather than reported as an empty sweep.
    let gesture = null;

    const keyOf = (element) => {
        const cell = element && element.closest ? element.closest('[data-key]') : null;
        return cell && !cell.disabled ? cell.dataset.key : null;
    };

    const paint = (key) => {
        if (!gesture || gesture.seen.has(key)) return;
        gesture.seen.add(key);
        gesture.order.push(key);
        // A class of ours, on top of whatever Blazor wrote. It is removed before the callback, so
        // the re-render that follows never has to argue with it.
        const cell = grid.querySelector(`[data-key="${key}"]`);
        if (cell) cell.classList.add('plan__unit--sweeping');
    };

    const clear = () => {
        for (const cell of grid.querySelectorAll('.plan__unit--sweeping'))
            cell.classList.remove('plan__unit--sweeping');
    };

    const onDown = (e) => {
        if (e.button !== undefined && e.button !== 0) return;

        const empty = e.target.closest ? e.target.closest('[data-empty]') : null;
        if (empty) {
            const [row, column] = empty.dataset.empty.split(',').map(Number);
            dotnet.invokeMethodAsync('EmptyAsync', row, column);
            return;
        }

        const key = keyOf(e.target);
        if (!key) return;

        const cell = grid.querySelector(`[data-key="${key}"]`);
        gesture = {
            // A sweep that began on something already chosen unchooses everything it crosses,
            // which is what anybody who has used a spreadsheet expects.
            deselect: cell?.dataset.selected === 'true',
            painting: e.pointerType === 'mouse' || e.pointerType === 'pen',
            seen: new Set(),
            order: [],
        };
        paint(key);

        if (gesture.painting) {
            // Capture on the GRID, so a pointer that leaves the last square still ends its gesture
            // here rather than stranding one that never finishes.
            try { grid.setPointerCapture(e.pointerId); } catch { /* not capturable */ }
        }
    };

    const onOver = (e) => {
        if (!gesture || !gesture.painting) return;
        // With capture on the grid, the events arrive here rather than at the square, so the
        // square under the pointer is asked for by position.
        const under = document.elementFromPoint(e.clientX, e.clientY);
        const key = keyOf(under);
        if (key) paint(key);
    };

    const onUp = () => {
        if (!gesture) return;
        const { order, deselect } = gesture;
        gesture = null;
        clear();
        if (order.length > 0) dotnet.invokeMethodAsync('GestureAsync', order, deselect);
    };

    const onCancel = () => { gesture = null; clear(); };

    grid.addEventListener('pointerdown', onDown);
    grid.addEventListener('pointermove', onOver);
    grid.addEventListener('pointerup', onUp);
    grid.addEventListener('pointercancel', onCancel);
    // A pointer released outside the window never raises pointerup on the grid.
    window.addEventListener('blur', onCancel);

    attached.set(containerId, () => {
        grid.removeEventListener('pointerdown', onDown);
        grid.removeEventListener('pointermove', onOver);
        grid.removeEventListener('pointerup', onUp);
        grid.removeEventListener('pointercancel', onCancel);
        window.removeEventListener('blur', onCancel);
    });
}

// Moves the browser's focus to the square the component thinks is focused, after an arrow key.
// Reading and focusing only; the roving tabindex itself is Blazor's, written into the markup.
export function focusCell(containerId, row, column) {
    const container = document.getElementById(containerId);
    if (!container) return;

    const cell = container.querySelector(`[style*="grid-row:${row + 2};grid-column:${column + 2}"]`);
    if (cell && cell.focus) cell.focus({ preventScroll: false });
}

export function detach(containerId) {
    const off = attached.get(containerId);
    if (off) { off(); attached.delete(containerId); }
}
