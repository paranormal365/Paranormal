// Image editor module — wraps Fabric.js for Blazor interop.
// Loaded lazily by ImageEditorPlayer.razor via import().
//
// Fabric is loaded by THIS module, on demand, rather than by a <script> tag in App.razor.
// It used to sit in the shell, which meant every page on the site — the sign-in page, the public
// microsite, every case screen — fetched a 300KB library from a third-party CDN that only this
// file uses. See wwwroot/plugins/fabric/VENDORED.md and item 114.
//
// Filters live at fabric.filters.*, NOT fabric.Image.filters.* — the latter is the v5 path and is
// undefined in v6 and v7 alike, so every filter call through it threw.

const FABRIC_SRC = '/plugins/fabric/fabric.min.js';
let _fabricLoading = null;

/**
 * Loads Fabric once and resolves when window.fabric is usable.
 *
 * The promise is cached rather than the boolean, so two editors opening at the same moment share
 * one fetch instead of racing to inject two script tags.
 */
function _ensureFabric() {
    if (window.fabric) return Promise.resolve(window.fabric);
    if (_fabricLoading) return _fabricLoading;

    _fabricLoading = new Promise((resolve, reject) => {
        const existing = document.querySelector(`script[src="${FABRIC_SRC}"]`);
        if (existing) {
            existing.addEventListener('load', () => resolve(window.fabric));
            existing.addEventListener('error', () => reject(new Error('Fabric failed to load.')));
            return;
        }
        const tag = document.createElement('script');
        tag.src = FABRIC_SRC;
        tag.onload = () => resolve(window.fabric);
        tag.onerror = () => reject(new Error(`Fabric failed to load from ${FABRIC_SRC}.`));
        document.head.appendChild(tag);
    });

    return _fabricLoading;
}

const _instances = new Map(); // containerId → { canvas, dotNetRef, baseImage, ... }

function _newId() { return crypto.randomUUID(); }

// ── Init / Destroy ───────────────────────────────────────────────────────────

export async function init(containerId, imageUrl, editStateJson, dotNetRef) {
    // Awaited here rather than at module load: importing this file must stay cheap, and Fabric
    // should only be fetched by somebody who actually opened an editor.
    await _ensureFabric();

    destroy(containerId);

    const container = document.getElementById(containerId);
    if (!container) return;

    const el = document.createElement('canvas');
    container.appendChild(el);

    const canvas = new fabric.Canvas(el, {
        preserveObjectStacking: true,
        enableRetinaScaling: true,
        selection: true,
    });

    const inst = { canvas, dotNetRef, baseImage: null, measureMode: false, measureStart: null };
    _instances.set(containerId, inst);

    const ro = new ResizeObserver(() => _fitToContainer(containerId));
    ro.observe(container);
    inst.ro = ro;

    if (editStateJson) {
        // Fabric v6's loadFromJSON is Promise-based (the old (json, callback) signature
        // from v5 silently no-ops — the callback is never invoked and nothing renders).
        canvas.loadFromJSON(editStateJson).then(() => {
            canvas.renderAll();
            _fitToContainer(containerId);
            _notifyChanged(containerId);
        }).catch(e => console.error('image-editor: failed to load edit state', e));
    } else if (imageUrl) {
        _loadBaseImage(containerId, imageUrl);
    }
}

export function destroy(containerId) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    inst.ro?.disconnect();
    inst.canvas.dispose();
    // dispose() tears down Fabric's internal state but does not remove the wrapping
    // DOM nodes it creates around the <canvas> element — without this, every re-open
    // of the same container (e.g. clicking "Edit" on a second file without a full page
    // reload) stacks another canvas on top of the old one instead of replacing it.
    inst.canvas.wrapperEl?.remove();
    _instances.delete(containerId);
}

// ── Image Loading ─────────────────────────────────────────────────────────────

async function _loadBaseImage(containerId, url) {
    // Fabric v6's Image.fromURL is Promise-based (the old (url, callback, options)
    // signature from v5 treats the callback as part of `options` and never invokes it —
    // no error, no image, canvas silently stays empty).
    let img;
    try {
        img = await fabric.Image.fromURL(url, { crossOrigin: 'anonymous' });
    } catch (e) {
        console.error('image-editor: failed to load image', e);
        return;
    }
    const inst = _instances.get(containerId);
    if (!inst) return; // container was destroyed while the image was loading
    inst.canvas.clear();
    inst.baseImage = img;
    img.set({ selectable: false, evented: false, layerName: '__bg__' });
    inst.canvas.add(img);
    inst.canvas.sendObjectToBack(img);
    _fitToContainer(containerId);
    inst.canvas.renderAll();
}

function _fitToContainer(containerId) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    const container = document.getElementById(containerId);
    if (!container) return;
    const w = container.clientWidth;
    if (w <= 0) return;
    const img = inst.baseImage;
    if (!img) { inst.canvas.setWidth(w); return; }
    const aspect = img.height / img.width;
    const h = Math.round(w * aspect);
    img.set({ scaleX: w / img.width, scaleY: h / img.height, left: 0, top: 0 });
    inst.canvas.setWidth(w);
    inst.canvas.setHeight(h);
    inst.canvas.renderAll();
}

// ── Adjustments ───────────────────────────────────────────────────────────────

export function applyAdjustments(containerId, opts) {
    const inst = _instances.get(containerId);
    if (!inst || !inst.baseImage) return;
    const img = inst.baseImage;
    const filters = [];
    if (opts.brightness !== 0) filters.push(new fabric.filters.Brightness({ brightness: opts.brightness / 100 }));
    if (opts.contrast   !== 0) filters.push(new fabric.filters.Contrast({ contrast: opts.contrast / 100 }));
    if (opts.saturation !== 0) filters.push(new fabric.filters.Saturation({ saturation: opts.saturation / 100 }));
    if (opts.hue        !== 0) filters.push(new fabric.filters.HueRotation({ rotation: opts.hue / 360 }));
    if (opts.blur        > 0)  filters.push(new fabric.filters.Blur({ blur: opts.blur / 100 }));
    if (opts.noise       > 0)  filters.push(new fabric.filters.Noise({ noise: opts.noise }));
    img.filters = filters;
    img.applyFilters();
    inst.canvas.renderAll();
    _notifyChanged(containerId);
}

export function applyPreset(containerId, preset) {
    const inst = _instances.get(containerId);
    if (!inst || !inst.baseImage) return;
    const img = inst.baseImage;
    const F = fabric.filters;
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
    _notifyChanged(containerId);
}

// ── Transform ─────────────────────────────────────────────────────────────────

export function rotate(containerId, degrees) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    const active = inst.canvas.getActiveObject();
    if (active) { active.rotate((active.angle + degrees) % 360); }
    else inst.canvas.getObjects().forEach(o => o.rotate((o.angle + degrees) % 360));
    inst.canvas.renderAll();
    _notifyChanged(containerId);
}

export function flip(containerId, axis) {
    const inst = _instances.get(containerId);
    if (!inst || !inst.baseImage) return;
    const img = inst.baseImage;
    if (axis === 'h') img.set('flipX', !img.flipX);
    else              img.set('flipY', !img.flipY);
    inst.canvas.renderAll();
    _notifyChanged(containerId);
}

// ── Drawing Tools ─────────────────────────────────────────────────────────────

export function setDrawingMode(containerId, mode, opts) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    const c = inst.canvas;
    c.isDrawingMode = false;
    c.defaultCursor = 'default';
    inst.measureMode = false;

    switch (mode) {
        case 'pen':
            c.isDrawingMode = true;
            c.freeDrawingBrush.color   = opts.color  ?? '#ff0000';
            c.freeDrawingBrush.width   = opts.width  ?? 3;
            c.freeDrawingBrush.opacity = opts.opacity ?? 1;
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
                    layerId: _newId(), layerName: 'Text',
                });
                c.add(t); c.setActiveObject(t); t.enterEditing(); c.renderAll();
                _notifyChanged(containerId);
            });
            break;
        case 'measure':
            _startMeasureMode(containerId);
            break;
        case 'arrow': case 'rect': case 'circle': case 'line': case 'redact':
            _startShapeMode(containerId, mode, opts);
            break;
    }
}

function _startShapeMode(containerId, shape, opts) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    const c = inst.canvas;
    let isDown = false, startX = 0, startY = 0, obj = null;

    const onDown = (e) => {
        isDown = true; const p = e.pointer; startX = p.x; startY = p.y;
        const color = opts.color ?? '#ff0000';
        const fill  = shape === 'redact' ? '#000000' : (opts.fill ?? 'transparent');
        const w     = opts.width ?? 2;
        switch (shape) {
            case 'rect': case 'redact':
                obj = new fabric.Rect({ left: startX, top: startY, width: 0, height: 0, stroke: color, strokeWidth: shape==='redact'?0:w, fill, layerId: _newId(), layerName: shape==='redact'?'Redact':'Rectangle' }); break;
            case 'circle':
                obj = new fabric.Ellipse({ left: startX, top: startY, rx: 0, ry: 0, stroke: color, strokeWidth: w, fill, layerId: _newId(), layerName: 'Ellipse' }); break;
            case 'line': case 'arrow':
                obj = new fabric.Line([startX, startY, startX, startY], { stroke: color, strokeWidth: w, layerId: _newId(), layerName: shape==='arrow'?'Arrow':'Line' }); break;
        }
        if (obj) c.add(obj);
    };
    const onMove = (e) => {
        if (!isDown || !obj) return;
        const p = e.pointer; const dx = p.x - startX, dy = p.y - startY;
        switch (shape) {
            case 'rect': case 'redact':
                obj.set({ width: Math.abs(dx), height: Math.abs(dy), left: Math.min(startX, p.x), top: Math.min(startY, p.y) }); break;
            case 'circle':
                obj.set({ rx: Math.abs(dx)/2, ry: Math.abs(dy)/2, left: Math.min(startX, p.x), top: Math.min(startY, p.y) }); break;
            case 'line': case 'arrow':
                obj.set({ x2: p.x, y2: p.y }); break;
        }
        c.renderAll();
    };
    const onUp = () => {
        isDown = false; obj = null;
        c.off('mouse:down', onDown); c.off('mouse:move', onMove); c.off('mouse:up', onUp);
        _notifyChanged(containerId);
    };
    c.on('mouse:down', onDown); c.on('mouse:move', onMove); c.on('mouse:up', onUp);
}

// ── Measurement ruler ─────────────────────────────────────────────────────────

function _startMeasureMode(containerId) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    const c = inst.canvas;
    inst.measureStart = null;

    const onDown = (e) => { inst.measureStart = { ...e.pointer }; };
    const onUp   = (e) => {
        if (!inst.measureStart) return;
        const s = inst.measureStart, end = e.pointer;
        const dist = Math.round(Math.sqrt(Math.pow(end.x - s.x, 2) + Math.pow(end.y - s.y, 2)));
        const mid  = { x: (s.x + end.x) / 2, y: (s.y + end.y) / 2 };

        const line  = new fabric.Line([s.x, s.y, end.x, end.y], { stroke: '#00ffff', strokeWidth: 2 });
        const label = new fabric.Text(`${dist}px`, {
            left: mid.x, top: mid.y - 16, fontSize: 14, fill: '#00ffff',
            backgroundColor: 'rgba(0,0,0,0.55)', padding: 2, fontFamily: 'monospace',
        });
        // Tick marks at each end
        const angle = Math.atan2(end.y - s.y, end.x - s.x);
        const tickLen = 6;
        const makeT = (px, py) => new fabric.Line(
            [px - Math.sin(angle)*tickLen, py + Math.cos(angle)*tickLen,
             px + Math.sin(angle)*tickLen, py - Math.cos(angle)*tickLen],
            { stroke: '#00ffff', strokeWidth: 2 });

        const group = new fabric.Group([line, label, makeT(s.x, s.y), makeT(end.x, end.y)], {
            selectable: true, layerId: _newId(), layerName: `Ruler (${dist}px)`,
        });
        c.add(group);
        c.renderAll();
        _notifyChanged(containerId);
        inst.measureStart = null;
        c.off('mouse:down', onDown); c.off('mouse:up', onUp);
    };
    c.on('mouse:down', onDown);
    c.on('mouse:up',   onUp);
}

// ── Evidence tools ────────────────────────────────────────────────────────────

export function addAnomalyHighlight(containerId, opts) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    const cx = opts?.cx ?? (inst.canvas.width  / 2);
    const cy = opts?.cy ?? (inst.canvas.height / 2);
    const rx = opts?.rx ?? 60, ry = opts?.ry ?? 40;
    const color = opts?.color ?? '#ff6600';
    const rings = opts?.rings ?? 3;

    const objects = [];
    for (let i = rings; i >= 1; i--) {
        const s = 1 + (rings - i) * 0.3;
        objects.push(new fabric.Ellipse({
            originX: 'center', originY: 'center', left: 0, top: 0,
            rx: rx * s, ry: ry * s,
            fill: 'transparent', stroke: color,
            strokeWidth: Math.max(1, 3 - (rings - i)),
            opacity: 0.85 / (rings - i + 1),
        }));
    }
    // Inner glow dot
    objects.push(new fabric.Circle({
        originX: 'center', originY: 'center', left: 0, top: 0,
        radius: 4, fill: color, opacity: 0.95,
    }));

    const group = new fabric.Group(objects, {
        left: cx, top: cy, originX: 'center', originY: 'center',
        selectable: true, layerId: _newId(), layerName: 'Anomaly Highlight',
    });
    inst.canvas.add(group);
    inst.canvas.setActiveObject(group);
    inst.canvas.renderAll();
    _notifyChanged(containerId);
}

export function toggleGrid(containerId, opts) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    const existing = inst.canvas.getObjects().find(o => o.layerName === '__grid__');
    if (existing) {
        inst.canvas.remove(existing);
        inst.canvas.renderAll();
        _notifyChanged(containerId);
        return;
    }
    const { cols = 10, rows = 10, color = 'rgba(255,255,255,0.3)', strokeWidth = 1 } = opts ?? {};
    const w = inst.canvas.width, h = inst.canvas.height;
    const lines = [];
    for (let i = 1; i < cols; i++)
        lines.push(new fabric.Line([i*w/cols, 0, i*w/cols, h], { stroke: color, strokeWidth, selectable: false, evented: false }));
    for (let i = 1; i < rows; i++)
        lines.push(new fabric.Line([0, i*h/rows, w, i*h/rows], { stroke: color, strokeWidth, selectable: false, evented: false }));
    const group = new fabric.Group(lines, {
        selectable: false, evented: false, layerId: _newId(), layerName: '__grid__',
    });
    inst.canvas.add(group);
    inst.canvas.renderAll();
    _notifyChanged(containerId);
}

export function addTimestampStamp(containerId, text, position) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    const w = inst.canvas.width, h = inst.canvas.height;
    const pad = 10;
    const fontSize = Math.max(12, Math.round(w / 40));

    const t = new fabric.Text(text || new Date().toLocaleString(), {
        fontSize, fill: '#ffffff',
        backgroundColor: 'rgba(0,0,0,0.6)', padding: 4,
        fontFamily: 'monospace',
        layerId: _newId(), layerName: 'Timestamp',
    });

    // Measure after creation to position correctly
    inst.canvas.add(t);
    const tw = t.width, th = t.height;
    switch (position) {
        case 'tr': t.set({ left: w - tw - pad, top: pad }); break;
        case 'bl': t.set({ left: pad,          top: h - th - pad }); break;
        case 'br': t.set({ left: w - tw - pad, top: h - th - pad }); break;
        default:   t.set({ left: pad,          top: pad }); // tl
    }
    inst.canvas.setActiveObject(t);
    inst.canvas.renderAll();
    _notifyChanged(containerId);
}

// ── Object management ─────────────────────────────────────────────────────────

export function deleteSelected(containerId) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    inst.canvas.getActiveObjects().forEach(o => { if (o !== inst.baseImage) inst.canvas.remove(o); });
    inst.canvas.discardActiveObject();
    inst.canvas.renderAll();
    _notifyChanged(containerId);
}

export function bringForward(containerId) {
    const inst = _instances.get(containerId);
    const active = inst?.canvas.getActiveObject();
    if (active && active !== inst.baseImage) { inst.canvas.bringObjectForward(active); inst.canvas.renderAll(); }
}

export function sendBackward(containerId) {
    const inst = _instances.get(containerId);
    const active = inst?.canvas.getActiveObject();
    if (active && active !== inst.baseImage) { inst.canvas.sendObjectBackwards(active); inst.canvas.renderAll(); }
}

// ── Layers API ────────────────────────────────────────────────────────────────

export function getLayersJson(containerId) {
    const inst = _instances.get(containerId);
    if (!inst) return '[]';
    const typeLabel = { 'i-text': 'Text', 'text': 'Text', rect: 'Rectangle', ellipse: 'Ellipse', line: 'Line', path: 'Path (pen)', group: 'Group', circle: 'Circle', image: 'Image' };
    const layers = inst.canvas.getObjects()
        .filter(o => o !== inst.baseImage && o.layerName !== '__grid__')
        .slice().reverse()
        .map(o => {
            if (!o.layerId) o.layerId = _newId();
            return {
                id:      o.layerId,
                name:    o.layerName ?? typeLabel[o.type] ?? o.type ?? 'Object',
                visible: o.visible !== false,
                opacity: Math.round((o.opacity ?? 1) * 100),
                type:    o.type,
            };
        });
    return JSON.stringify(layers);
}

export function setLayerVisible(containerId, id, visible) {
    const inst = _instances.get(containerId);
    const obj  = _findById(containerId, id);
    if (!obj || !inst) return;
    obj.set('visible', visible);
    inst.canvas.renderAll();
}

export function setLayerOpacity(containerId, id, pct) {
    const inst = _instances.get(containerId);
    const obj  = _findById(containerId, id);
    if (!obj || !inst) return;
    obj.set('opacity', pct / 100);
    inst.canvas.renderAll();
    _notifyChanged(containerId);
}

export function deleteLayer(containerId, id) {
    const inst = _instances.get(containerId);
    const obj  = _findById(containerId, id);
    if (!obj || !inst) return;
    inst.canvas.remove(obj);
    inst.canvas.renderAll();
    _notifyChanged(containerId);
}

export function moveLayerUp(containerId, id) {
    const inst = _instances.get(containerId);
    const obj  = _findById(containerId, id);
    if (obj && inst) { inst.canvas.bringObjectForward(obj); inst.canvas.renderAll(); }
}

export function moveLayerDown(containerId, id) {
    const inst = _instances.get(containerId);
    const obj  = _findById(containerId, id);
    if (obj && inst) { inst.canvas.sendObjectBackwards(obj); inst.canvas.renderAll(); }
}

function _findById(containerId, id) {
    const inst = _instances.get(containerId);
    return inst?.canvas.getObjects().find(o => o.layerId === id) ?? null;
}

// ── State / Export ────────────────────────────────────────────────────────────

export function getStateJson(containerId) {
    const inst = _instances.get(containerId);
    if (!inst) return null;
    return JSON.stringify(inst.canvas.toJSON(['layerId', 'layerName', 'selectable', 'evented']));
}

export function exportToBase64(containerId, format, quality) {
    const inst = _instances.get(containerId);
    if (!inst) return null;
    inst.canvas.discardActiveObject();
    inst.canvas.renderAll();
    return inst.canvas.toDataURL({ format: format ?? 'png', quality: quality ?? 0.92, multiplier: 1 });
}

function _notifyChanged(containerId) {
    _instances.get(containerId)?.dotNetRef?.invokeMethodAsync('OnEditorChanged');
}
