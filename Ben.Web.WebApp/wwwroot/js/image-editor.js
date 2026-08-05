// Image editor module — wraps Fabric.js v6 for Blazor interop.
// Loaded lazily by ImageEditorPlayer.razor via import().

const _instances = new Map(); // containerId → { canvas, dotNetRef, options }

// ── Init / Destroy ───────────────────────────────────────────────────────────

export function init(containerId, imageUrl, editStateJson, dotNetRef) {
    destroy(containerId);

    const container = document.getElementById(containerId);
    if (!container) return;

    // Create canvas element inside container
    const el = document.createElement('canvas');
    container.appendChild(el);

    const canvas = new fabric.Canvas(el, {
        preserveObjectStacking: true,
        enableRetinaScaling: true,
        selection: true,
    });

    _instances.set(containerId, { canvas, dotNetRef, baseImage: null });

    // Fit canvas to container width on resize
    const ro = new ResizeObserver(() => _fitToContainer(containerId));
    ro.observe(container);
    _instances.get(containerId).ro = ro;

    if (editStateJson) {
        canvas.loadFromJSON(editStateJson, () => {
            canvas.renderAll();
            _fitToContainer(containerId);
            _notifyDirty(containerId);
        });
    } else if (imageUrl) {
        _loadBaseImage(containerId, imageUrl);
    }
}

export function destroy(containerId) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    inst.ro?.disconnect();
    inst.canvas.dispose();
    _instances.delete(containerId);
}

// ── Image Loading ─────────────────────────────────────────────────────────────

function _loadBaseImage(containerId, url) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    fabric.Image.fromURL(url, (img) => {
        inst.canvas.clear();
        inst.baseImage = img;
        img.set({ selectable: false, evented: false, excludeFromExport: false });
        inst.canvas.add(img);
        inst.canvas.sendToBack(img);
        _fitToContainer(containerId);
        inst.canvas.renderAll();
    }, { crossOrigin: 'anonymous' });
}

function _fitToContainer(containerId) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    const container = document.getElementById(containerId);
    if (!container) return;
    const w = container.clientWidth;
    if (w <= 0) return;
    const img = inst.baseImage;
    if (!img) {
        inst.canvas.setWidth(w);
        inst.canvas.setHeight(Math.max(400, container.clientHeight));
        return;
    }
    const aspect = img.height / img.width;
    const h = Math.round(w * aspect);
    const scaleX = w / img.width;
    const scaleY = h / img.height;
    img.set({ scaleX, scaleY, left: 0, top: 0 });
    inst.canvas.setWidth(w);
    inst.canvas.setHeight(h);
    inst.canvas.renderAll();
}

// ── Adjustments (pixel-level via ImageData) ───────────────────────────────────

export function applyAdjustments(containerId, opts) {
    const inst = _instances.get(containerId);
    if (!inst || !inst.baseImage) return;
    const img = inst.baseImage;

    const filters = [];

    if (opts.brightness !== 0)
        filters.push(new fabric.Image.filters.Brightness({ brightness: opts.brightness / 100 }));
    if (opts.contrast !== 0)
        filters.push(new fabric.Image.filters.Contrast({ contrast: opts.contrast / 100 }));
    if (opts.saturation !== 0)
        filters.push(new fabric.Image.filters.Saturation({ saturation: opts.saturation / 100 }));
    if (opts.hue !== 0)
        filters.push(new fabric.Image.filters.HueRotation({ rotation: opts.hue / 360 }));
    if (opts.blur > 0)
        filters.push(new fabric.Image.filters.Blur({ blur: opts.blur / 100 }));
    if (opts.noise > 0)
        filters.push(new fabric.Image.filters.Noise({ noise: opts.noise }));

    img.filters = filters;
    img.applyFilters();
    inst.canvas.renderAll();
    _notifyDirty(containerId);
}

export function applyPreset(containerId, preset) {
    const inst = _instances.get(containerId);
    if (!inst || !inst.baseImage) return;
    const img = inst.baseImage;
    const F = fabric.Image.filters;

    const presets = {
        none:         [],
        grayscale:    [new F.Grayscale()],
        sepia:        [new F.Sepia()],
        invert:       [new F.Invert()],
        highcontrast: [new F.Contrast({ contrast: 0.4 }), new F.Brightness({ brightness: -0.05 })],
        nightvision:  [new F.Grayscale(), new F.BlendColor({ color: '#00ff00', mode: 'multiply', alpha: 0.4 })],
        heatmap:      [new F.Grayscale(), new F.BlendColor({ color: '#ff4400', mode: 'multiply', alpha: 0.6 })],
    };

    img.filters = presets[preset] ?? [];
    img.applyFilters();
    inst.canvas.renderAll();
    _notifyDirty(containerId);
}

// ── Transform ─────────────────────────────────────────────────────────────────

export function rotate(containerId, degrees) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    const active = inst.canvas.getActiveObject();
    if (active) {
        active.rotate((active.angle + degrees) % 360);
        inst.canvas.renderAll();
    } else if (inst.baseImage) {
        inst.canvas.getObjects().forEach(o => {
            o.rotate((o.angle + degrees) % 360);
        });
        inst.canvas.renderAll();
    }
    _notifyDirty(containerId);
}

export function flip(containerId, axis) {
    const inst = _instances.get(containerId);
    if (!inst || !inst.baseImage) return;
    const img = inst.baseImage;
    if (axis === 'h') img.set('flipX', !img.flipX);
    else              img.set('flipY', !img.flipY);
    inst.canvas.renderAll();
    _notifyDirty(containerId);
}

// ── Drawing Tools ─────────────────────────────────────────────────────────────

export function setDrawingMode(containerId, mode, opts) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    const c = inst.canvas;

    c.isDrawingMode = false;
    c.defaultCursor = 'default';

    switch (mode) {
        case 'pen':
            c.isDrawingMode = true;
            c.freeDrawingBrush.color   = opts.color  ?? '#ff0000';
            c.freeDrawingBrush.width   = opts.width  ?? 3;
            c.freeDrawingBrush.opacity = opts.opacity ?? 1;
            break;
        case 'select':
            // default selection mode — nothing to set
            break;
        case 'text':
            c.defaultCursor = 'text';
            c.once('mouse:down', (e) => {
                if (mode !== 'text') return;
                const p = e.pointer;
                const t = new fabric.IText('Text', {
                    left: p.x, top: p.y,
                    fontSize: opts.fontSize ?? 24,
                    fill: opts.color ?? '#ffffff',
                    fontFamily: opts.fontFamily ?? 'Arial',
                });
                c.add(t);
                c.setActiveObject(t);
                t.enterEditing();
                c.renderAll();
                _notifyDirty(containerId);
            });
            break;
        case 'arrow':
        case 'rect':
        case 'circle':
        case 'line':
            _startShapeMode(containerId, mode, opts);
            break;
        case 'redact':
            _startShapeMode(containerId, 'rect', { color: '#000000', fill: '#000000' });
            break;
    }
}

function _startShapeMode(containerId, shape, opts) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    const c = inst.canvas;
    let isDown = false, startX = 0, startY = 0, obj = null;

    const onMouseDown = (e) => {
        isDown = true;
        const p = e.pointer;
        startX = p.x; startY = p.y;
        const color = opts.color ?? '#ff0000';
        const fill  = opts.fill  ?? 'transparent';
        const w     = opts.width ?? 2;

        switch (shape) {
            case 'rect':
                obj = new fabric.Rect({ left: startX, top: startY, width: 0, height: 0, stroke: color, strokeWidth: w, fill, selectable: true });
                break;
            case 'circle':
                obj = new fabric.Ellipse({ left: startX, top: startY, rx: 0, ry: 0, stroke: color, strokeWidth: w, fill, selectable: true });
                break;
            case 'line':
            case 'arrow':
                obj = new fabric.Line([startX, startY, startX, startY], { stroke: color, strokeWidth: w, selectable: true });
                break;
        }
        if (obj) c.add(obj);
    };

    const onMouseMove = (e) => {
        if (!isDown || !obj) return;
        const p = e.pointer;
        const dx = p.x - startX, dy = p.y - startY;
        switch (shape) {
            case 'rect':
                obj.set({ width: Math.abs(dx), height: Math.abs(dy), left: Math.min(startX, p.x), top: Math.min(startY, p.y) });
                break;
            case 'circle':
                obj.set({ rx: Math.abs(dx) / 2, ry: Math.abs(dy) / 2, left: Math.min(startX, p.x), top: Math.min(startY, p.y) });
                break;
            case 'line':
            case 'arrow':
                obj.set({ x2: p.x, y2: p.y });
                break;
        }
        c.renderAll();
    };

    const onMouseUp = () => {
        isDown = false;
        obj = null;
        c.off('mouse:down', onMouseDown);
        c.off('mouse:move', onMouseMove);
        c.off('mouse:up',   onMouseUp);
        _notifyDirty(containerId);
    };

    c.on('mouse:down', onMouseDown);
    c.on('mouse:move', onMouseMove);
    c.on('mouse:up',   onMouseUp);
}

// ── Object Management ─────────────────────────────────────────────────────────

export function deleteSelected(containerId) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    const active = inst.canvas.getActiveObjects();
    active.forEach(o => {
        if (o !== inst.baseImage) inst.canvas.remove(o);
    });
    inst.canvas.discardActiveObject();
    inst.canvas.renderAll();
    _notifyDirty(containerId);
}

export function bringForward(containerId) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    const active = inst.canvas.getActiveObject();
    if (active && active !== inst.baseImage) inst.canvas.bringForward(active);
    inst.canvas.renderAll();
}

export function sendBackward(containerId) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    const active = inst.canvas.getActiveObject();
    if (active && active !== inst.baseImage) inst.canvas.sendBackwards(active);
    inst.canvas.renderAll();
}

// ── State / Export ────────────────────────────────────────────────────────────

export function getStateJson(containerId) {
    const inst = _instances.get(containerId);
    if (!inst) return null;
    return JSON.stringify(inst.canvas.toJSON(['selectable', 'evented', 'excludeFromExport']));
}

export function exportToBase64(containerId, format, quality) {
    const inst = _instances.get(containerId);
    if (!inst) return null;
    // Deselect all before export so handles are not in the image
    inst.canvas.discardActiveObject();
    inst.canvas.renderAll();
    return inst.canvas.toDataURL({ format: format ?? 'png', quality: quality ?? 0.92, multiplier: 1 });
}

function _notifyDirty(containerId) {
    const inst = _instances.get(containerId);
    inst?.dotNetRef?.invokeMethodAsync('OnEditorChanged');
}
