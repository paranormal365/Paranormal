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

/**
 * Every shape here is placed by its top-left corner - a rectangle starts where the press was and
 * grows towards the pointer. The vendored Fabric places new objects by their CENTRE by default,
 * so a rectangle grew outwards from the press in both directions and never sat under the pointer
 * (seen 09/25/2026: every mark landed half its own size up and to the left). Set once, for this
 * editor's objects only; anything that wants its centre - the photo, the highlight - says so.
 */
function _useTopLeftOrigins() {
    const defaults = fabric.FabricObject?.ownDefaults ?? fabric.Object?.prototype;
    if (!defaults || defaults.originX === 'left') return;
    defaults.originX = 'left';
    defaults.originY = 'top';
}

// ── Coordinates ───────────────────────────────────────────────────────────────
//
// Everything on the canvas lives in the PHOTO's own pixels: the photo sits at scale 1 with its
// top-left corner at (0, 0), and what you see is a viewport transform over that - zoomed to fit
// the space, or wherever the person has zoomed and dragged to.
//
// It used to be the other way round. The photo was scaled down to the canvas's width and
// everything was drawn in those display pixels, so "Save as New Version" wrote a copy the size of
// the dialog (a 4032-pixel photograph came back about 800 wide), the ruler measured screen pixels
// rather than the photo's, and the canvas only ever sized itself by width - in the small dialog it
// was given, that width was zero and there was no picture at all (09/25/2026).

const MIN_ZOOM = 0.02;
const MAX_ZOOM = 16;

/** Where a pointer event landed, in photo pixels. */
function _pt(canvas, opt) {
    return opt.scenePoint ?? canvas.getScenePoint(opt.e);
}

/** The photo's rectangle in scene coordinates, allowing for quarter turns. */
function _bounds(inst) {
    const img = inst.baseImage;
    if (!img) return { left: 0, top: 0, width: inst.canvas.width || 1, height: inst.canvas.height || 1 };
    const turned = Math.round(((img.angle ?? 0) % 180 + 180) % 180) === 90;
    const w = turned ? img.height : img.width;
    const h = turned ? img.width : img.height;
    const c = img.getCenterPoint();
    return { left: c.x - w / 2, top: c.y - h / 2, width: w, height: h };
}

/** Screen pixels → photo pixels at the current zoom, so a 3-pixel pen looks 3 pixels wide. */
function _screen(inst, px) {
    return px / (inst.canvas.getZoom() || 1);
}

// ── Init / Destroy ───────────────────────────────────────────────────────────

export async function init(containerId, imageUrl, editStateJson, dotNetRef) {
    // Awaited here rather than at module load: importing this file must stay cheap, and Fabric
    // should only be fetched by somebody who actually opened an editor.
    await _ensureFabric();
    _useTopLeftOrigins();

    destroy(containerId);

    const container = document.getElementById(containerId);
    if (!container) return;

    const el = document.createElement('canvas');
    container.appendChild(el);

    const canvas = new fabric.Canvas(el, {
        preserveObjectStacking: true,
        enableRetinaScaling: true,
        // Dragging on the photo moves around it (see _wirePanAndZoom); a rubber-band selection
        // on the same gesture would fight it.
        selection: false,
        fireMiddleClick: true,
        width: Math.max(1, container.clientWidth),
        height: Math.max(1, container.clientHeight),
    });

    const inst = {
        canvas, dotNetRef, baseImage: null,
        zoomMode: 'fit', tool: 'select', toolOpts: {}, cleanupTool: null, pan: null,
    };
    _instances.set(containerId, inst);

    _wirePanAndZoom(containerId);

    // Fabric caches where its canvas sits on the page and turns every pointer position into a
    // photo position through that cache. It measures once, when it is created - and here that is
    // while the dialog is still opening, so every mark landed a fixed distance up and to the left
    // of the pointer. Measured again at the start of every press, before Fabric reads the press
    // (a capturing listener on the wrapper runs ahead of Fabric's own on the canvas inside it).
    canvas.wrapperEl.addEventListener('pointerdown', () => canvas.calcOffset(), { capture: true });
    canvas.wrapperEl.addEventListener('wheel', () => canvas.calcOffset(), { capture: true, passive: true });

    // A pen stroke and a moved or resized mark are edits too; without these the Save buttons
    // never appeared after drawing with the pen.
    canvas.on('path:created', e => {
        e.path?.set({ layerId: _newId(), layerName: 'Pen', selectable: false });
        _notifyChanged(containerId);
    });
    canvas.on('object:modified', () => _notifyChanged(containerId));

    const ro = new ResizeObserver(() => _fitToContainer(containerId));
    ro.observe(container);
    inst.ro = ro;

    if (editStateJson) {
        // Fabric v6's loadFromJSON is Promise-based (the old (json, callback) signature
        // from v5 silently no-ops — the callback is never invoked and nothing renders).
        canvas.loadFromJSON(editStateJson).then(() => {
            inst.baseImage = canvas.getObjects().find(o => o.layerName === '__bg__') ?? null;
            canvas.renderAll();
            _fitToContainer(containerId);
            _notifyChanged(containerId);
        }).catch(e => console.error('image-editor: failed to load edit state', e));
    } else if (imageUrl) {
        await _loadBaseImage(containerId, imageUrl);
    }
}

export function destroy(containerId) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    inst.cleanupTool?.();
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
        _instances.get(containerId)?.dotNetRef?.invokeMethodAsync('OnImageFailed');
        return;
    }
    const inst = _instances.get(containerId);
    if (!inst) return; // container was destroyed while the image was loading
    inst.canvas.clear();
    inst.baseImage = img;
    // Centre origin, so a quarter turn spins it in place rather than swinging it off the corner.
    img.set({
        originX: 'center', originY: 'center', left: img.width / 2, top: img.height / 2,
        selectable: false, evented: false, layerName: '__bg__',
    });
    inst.canvas.add(img);
    inst.canvas.sendObjectToBack(img);
    inst.zoomMode = 'fit';
    _fitToContainer(containerId);
    inst.dotNetRef?.invokeMethodAsync('OnImageLoaded', img.width, img.height);
}

function _fitToContainer(containerId) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    const container = document.getElementById(containerId);
    if (!container) return;
    const w = container.clientWidth, h = container.clientHeight;
    if (w <= 0 || h <= 0) return;
    if (inst.canvas.width !== w || inst.canvas.height !== h)
        inst.canvas.setDimensions({ width: w, height: h });
    inst.canvas.calcOffset();
    if (inst.zoomMode === 'fit') _zoomToFit(inst);
    else inst.canvas.requestRenderAll();
}

function _zoomToFit(inst) {
    const c = inst.canvas;
    const b = _bounds(inst);
    const pad = 16;
    // Never enlarged past 100% to fit: a small picture shown bigger than it is looks sharp nowhere.
    const z = Math.min(1, (c.width - pad * 2) / b.width, (c.height - pad * 2) / b.height);
    const zoom = Math.max(MIN_ZOOM, z);
    c.setViewportTransform([zoom, 0, 0, zoom,
        c.width / 2 - (b.left + b.width / 2) * zoom,
        c.height / 2 - (b.top + b.height / 2) * zoom]);
    _afterZoom(inst);
}

function _zoomAt(inst, point, zoom) {
    const z = Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, zoom));
    inst.canvas.zoomToPoint(point, z);
    inst.zoomMode = 'manual';
    _afterZoom(inst);
}

function _afterZoom(inst) {
    const c = inst.canvas;
    // A drawing tool's size is in screen pixels, so it has to follow the zoom.
    if (c.isDrawingMode && c.freeDrawingBrush)
        c.freeDrawingBrush.width = _screen(inst, inst.toolOpts.width ?? 3);
    c.requestRenderAll();
    inst.dotNetRef?.invokeMethodAsync('OnZoomChanged', Math.round(c.getZoom() * 100));
}

/**
 * Zoom from the toolbar. 'in' / 'out' step around the middle of the view, 'fit' shows the whole
 * photo and keeps fitting as the window changes size, 'actual' is one photo pixel per screen pixel.
 */
export function zoom(containerId, action) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    const c = inst.canvas;
    const middle = new fabric.Point(c.width / 2, c.height / 2);
    switch (action) {
        case 'in':     _zoomAt(inst, middle, c.getZoom() * 1.25); break;
        case 'out':    _zoomAt(inst, middle, c.getZoom() / 1.25); break;
        case 'actual': {
            // Centre on what is in the middle of the view now, not on the photo's corner.
            _zoomAt(inst, middle, 1);
            break;
        }
        default:
            inst.zoomMode = 'fit';
            _zoomToFit(inst);
    }
}

function _wirePanAndZoom(containerId) {
    const inst = _instances.get(containerId);
    const c = inst.canvas;

    // The wheel zooms about the pointer, the way every photo viewer does. It is taken from the
    // page, which would otherwise scroll the dialog underneath.
    c.on('mouse:wheel', opt => {
        const e = opt.e;
        const factor = Math.pow(0.999, e.deltaY * (e.deltaMode === 1 ? 33 : 1));
        _zoomAt(inst, new fabric.Point(e.offsetX, e.offsetY), c.getZoom() * factor);
        e.preventDefault();
        e.stopPropagation();
    });

    // Moving around a zoomed photo: drag with the Select tool anywhere that isn't something you
    // added (the photo itself takes no clicks), or hold Alt / use the middle button with any tool.
    c.on('mouse:down', opt => {
        const e = opt.e;
        const byTool = inst.tool === 'select' && !opt.target;
        if (c.isDrawingMode || !(byTool || e.altKey || e.button === 1)) return;
        inst.pan = { x: e.clientX, y: e.clientY };
        c.setCursor('grabbing');
    });
    c.on('mouse:move', opt => {
        if (!inst.pan) return;
        const e = opt.e;
        const vpt = c.viewportTransform.slice();
        vpt[4] += e.clientX - inst.pan.x;
        vpt[5] += e.clientY - inst.pan.y;
        inst.pan = { x: e.clientX, y: e.clientY };
        c.setViewportTransform(vpt);
        inst.zoomMode = 'manual';
        c.requestRenderAll();
    });
    c.on('mouse:up', () => {
        if (!inst.pan) return;
        inst.pan = null;
        c.setCursor(c.defaultCursor);
    });
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

/**
 * Turns the selected mark, or with nothing selected the whole picture: the photo about its centre
 * and every mark with it, so a circle drawn round something is still round it afterwards. (It used
 * to spin each object about its own corner, which sent the photo out of view.)
 */
export function rotate(containerId, degrees) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    const c = inst.canvas;
    const active = c.getActiveObject();
    if (active && active !== inst.baseImage) {
        active.rotate(((active.angle ?? 0) + degrees + 360) % 360);
        active.setCoords();
    } else if (inst.baseImage) {
        const centre = inst.baseImage.getCenterPoint();
        const radians = fabric.util.degreesToRadians(degrees);
        c.getObjects().forEach(o => {
            const moved = o.getCenterPoint().rotate(radians, centre); // Point#rotate; there is no util.rotatePoint in this Fabric
            o.rotate(((o.angle ?? 0) + degrees + 360) % 360);
            o.setPositionByOrigin(moved, 'center', 'center');
            o.setCoords();
        });
        if (inst.zoomMode === 'fit') _zoomToFit(inst);
    }
    c.renderAll();
    _notifyChanged(containerId);
    // The picture's size as it will be saved, which a quarter turn swaps.
    const b = _bounds(inst);
    return [Math.round(b.width), Math.round(b.height)];
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

/**
 * Switches tool. A tool stays chosen until another is picked - draw three rectangles without
 * pressing Rectangle three times - and switching removes the last tool's listeners first. They
 * used to be one-shot and never removed on a switch, so a rectangle tool abandoned for the pen was
 * still drawing rectangles underneath it.
 */
export function setDrawingMode(containerId, mode, opts) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    const c = inst.canvas;
    inst.cleanupTool?.();
    inst.cleanupTool = null;
    inst.tool = mode;
    inst.toolOpts = opts ?? {};
    c.isDrawingMode = false;
    c.discardActiveObject();

    // Only the Select tool can pick up and move what has been added; with any other tool a click
    // on an existing mark should draw, not grab it.
    c.getObjects().forEach(o => { if (o !== inst.baseImage && o.layerName !== '__grid__') o.selectable = mode === 'select'; });

    c.defaultCursor = mode === 'select' ? 'grab' : 'crosshair';
    c.hoverCursor = mode === 'select' ? 'move' : 'crosshair';

    switch (mode) {
        case 'pen':
            c.freeDrawingBrush = new fabric.PencilBrush(c);
            c.freeDrawingBrush.color = inst.toolOpts.color ?? '#ff0000';
            c.freeDrawingBrush.width = _screen(inst, inst.toolOpts.width ?? 3);
            c.isDrawingMode = true;
            break;
        case 'text':
            _startTextMode(containerId);
            break;
        case 'measure':
            _startMeasureMode(containerId);
            break;
        case 'arrow': case 'rect': case 'circle': case 'line': case 'redact':
            _startShapeMode(containerId, mode);
            break;
    }
    c.requestRenderAll();
}

/** Tool options changed (colour, width, font size) without the tool changing. */
export function setToolOptions(containerId, opts) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    inst.toolOpts = opts ?? {};
    const c = inst.canvas;
    if (c.isDrawingMode && c.freeDrawingBrush) {
        c.freeDrawingBrush.color = inst.toolOpts.color ?? '#ff0000';
        c.freeDrawingBrush.width = _screen(inst, inst.toolOpts.width ?? 3);
    }
}

function _startTextMode(containerId) {
    const inst = _instances.get(containerId);
    const c = inst.canvas;
    const onDown = (opt) => {
        if (opt.e.altKey || opt.e.button === 1) return;
        if (opt.target && opt.target.type === 'i-text') return; // clicking into existing text edits it
        const p = _pt(c, opt);
        const t = new fabric.IText('Text', {
            left: p.x, top: p.y,
            fontSize: _screen(inst, inst.toolOpts.fontSize ?? 24),
            fill: inst.toolOpts.color ?? '#ffffff',
            fontFamily: inst.toolOpts.fontFamily ?? 'Arial',
            layerId: _newId(), layerName: 'Text',
        });
        c.add(t); c.setActiveObject(t); t.enterEditing(); t.selectAll(); c.renderAll();
        _notifyChanged(containerId);
    };
    c.on('mouse:down', onDown);
    inst.cleanupTool = () => c.off('mouse:down', onDown);
}

function _startShapeMode(containerId, shape) {
    const inst = _instances.get(containerId);
    const c = inst.canvas;
    let startX = 0, startY = 0, obj = null;

    const onDown = (opt) => {
        if (opt.e.altKey || opt.e.button === 1) return; // that is a pan
        const p = _pt(c, opt); startX = p.x; startY = p.y;
        const o = inst.toolOpts;
        const color = o.color ?? '#ff0000';
        const fill  = shape === 'redact' ? '#000000' : (o.fill ?? 'transparent');
        const w     = _screen(inst, o.width ?? 2);
        switch (shape) {
            case 'rect': case 'redact':
                obj = new fabric.Rect({ left: startX, top: startY, width: 0, height: 0, stroke: color, strokeWidth: shape==='redact'?0:w, fill, layerId: _newId(), layerName: shape==='redact'?'Redact':'Rectangle', selectable: false }); break;
            case 'circle':
                obj = new fabric.Ellipse({ left: startX, top: startY, rx: 0, ry: 0, stroke: color, strokeWidth: w, fill, layerId: _newId(), layerName: 'Ellipse', selectable: false }); break;
            case 'line': case 'arrow':
                obj = new fabric.Line([startX, startY, startX, startY], { stroke: color, strokeWidth: w, layerId: _newId(), layerName: 'Line', selectable: false }); break;
        }
        if (obj) c.add(obj);
    };
    const onMove = (opt) => {
        if (!obj) return;
        const p = _pt(c, opt); const dx = p.x - startX, dy = p.y - startY;
        switch (shape) {
            case 'rect': case 'redact':
                obj.set({ width: Math.abs(dx), height: Math.abs(dy), left: Math.min(startX, p.x), top: Math.min(startY, p.y) }); break;
            case 'circle':
                obj.set({ rx: Math.abs(dx)/2, ry: Math.abs(dy)/2, left: Math.min(startX, p.x), top: Math.min(startY, p.y) }); break;
            case 'line': case 'arrow':
                obj.set({ x2: p.x, y2: p.y }); break;
        }
        c.requestRenderAll();
    };
    const onUp = () => {
        if (!obj) return;
        // A click without a drag leaves a shape nobody can see or find; do not keep it.
        const empty = (obj.width ?? 0) < 1 && (obj.height ?? 0) < 1 && !(obj.rx > 0);
        if (empty) c.remove(obj);
        else { obj.setCoords(); _notifyChanged(containerId); }
        obj = null;
    };
    c.on('mouse:down', onDown); c.on('mouse:move', onMove); c.on('mouse:up', onUp);
    inst.cleanupTool = () => { c.off('mouse:down', onDown); c.off('mouse:move', onMove); c.off('mouse:up', onUp); };
}

// ── Measurement ruler ─────────────────────────────────────────────────────────

function _startMeasureMode(containerId) {
    const inst = _instances.get(containerId);
    const c = inst.canvas;
    let start = null;

    const onDown = (opt) => {
        if (opt.e.altKey || opt.e.button === 1) return;
        start = _pt(c, opt);
    };
    const onUp   = (opt) => {
        if (!start) return;
        const s = start, end = _pt(c, opt);
        start = null;
        // Photo pixels, now that the canvas is in photo pixels - it used to measure the screen.
        const dist = Math.round(Math.hypot(end.x - s.x, end.y - s.y));
        if (dist < 1) return;
        const mid  = { x: (s.x + end.x) / 2, y: (s.y + end.y) / 2 };
        const sw = _screen(inst, 2);

        const line  = new fabric.Line([s.x, s.y, end.x, end.y], { stroke: '#00ffff', strokeWidth: sw });
        const label = new fabric.Text(`${dist}px`, {
            left: mid.x, top: mid.y - _screen(inst, 16), fontSize: _screen(inst, 14), fill: '#00ffff',
            backgroundColor: 'rgba(0,0,0,0.55)', padding: 2, fontFamily: 'monospace',
        });
        // Tick marks at each end
        const angle = Math.atan2(end.y - s.y, end.x - s.x);
        const tickLen = _screen(inst, 6);
        const makeT = (px, py) => new fabric.Line(
            [px - Math.sin(angle)*tickLen, py + Math.cos(angle)*tickLen,
             px + Math.sin(angle)*tickLen, py - Math.cos(angle)*tickLen],
            { stroke: '#00ffff', strokeWidth: sw });

        const group = new fabric.Group([line, label, makeT(s.x, s.y), makeT(end.x, end.y)], {
            selectable: false, layerId: _newId(), layerName: `Ruler (${dist}px)`,
        });
        c.add(group);
        c.renderAll();
        _notifyChanged(containerId);
    };
    c.on('mouse:down', onDown);
    c.on('mouse:up',   onUp);
    inst.cleanupTool = () => { c.off('mouse:down', onDown); c.off('mouse:up', onUp); };
}

// ── Evidence tools ────────────────────────────────────────────────────────────

export function addAnomalyHighlight(containerId, opts) {
    const inst = _instances.get(containerId);
    if (!inst) return;
    // In the middle of what is on screen, at a size that reads at this zoom.
    const vc = inst.canvas.getVpCenter();
    const cx = opts?.cx ?? vc.x;
    const cy = opts?.cy ?? vc.y;
    const rx = opts?.rx ?? _screen(inst, 60), ry = opts?.ry ?? _screen(inst, 40);
    const color = opts?.color ?? '#ff6600';
    const rings = opts?.rings ?? 3;

    const objects = [];
    for (let i = rings; i >= 1; i--) {
        const s = 1 + (rings - i) * 0.3;
        objects.push(new fabric.Ellipse({
            originX: 'center', originY: 'center', left: 0, top: 0,
            rx: rx * s, ry: ry * s,
            fill: 'transparent', stroke: color,
            strokeWidth: _screen(inst, Math.max(1, 3 - (rings - i))),
            opacity: 0.85 / (rings - i + 1),
        }));
    }
    // Inner glow dot
    objects.push(new fabric.Circle({
        originX: 'center', originY: 'center', left: 0, top: 0,
        radius: _screen(inst, 4), fill: color, opacity: 0.95,
    }));

    const group = new fabric.Group(objects, {
        left: cx, top: cy, originX: 'center', originY: 'center',
        selectable: inst.tool === 'select', layerId: _newId(), layerName: 'Anomaly Highlight',
    });
    inst.canvas.add(group);
    if (inst.tool === 'select') inst.canvas.setActiveObject(group);
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
    const { cols = 10, rows = 10, color = 'rgba(255,255,255,0.3)' } = opts ?? {};
    // Over the photo, not the canvas, and thick enough to survive being saved at full size.
    const b = _bounds(inst);
    const strokeWidth = Math.max(1, b.width / 800);
    const lines = [];
    for (let i = 1; i < cols; i++) {
        const x = b.left + i * b.width / cols;
        lines.push(new fabric.Line([x, b.top, x, b.top + b.height], { stroke: color, strokeWidth, selectable: false, evented: false }));
    }
    for (let i = 1; i < rows; i++) {
        const y = b.top + i * b.height / rows;
        lines.push(new fabric.Line([b.left, y, b.left + b.width, y], { stroke: color, strokeWidth, selectable: false, evented: false }));
    }
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
    // A corner of the photo, sized to the photo, so it is the same stamp whatever the zoom.
    const b = _bounds(inst);
    const pad = Math.max(10, Math.round(b.width / 100));
    const fontSize = Math.max(12, Math.round(b.width / 40));

    const t = new fabric.Text(text || new Date().toLocaleString(), {
        fontSize, fill: '#ffffff',
        backgroundColor: 'rgba(0,0,0,0.6)', padding: 4,
        fontFamily: 'monospace',
        selectable: inst.tool === 'select',
        layerId: _newId(), layerName: 'Timestamp',
    });

    // Measure after creation to position correctly
    inst.canvas.add(t);
    const tw = t.width, th = t.height;
    const right = b.left + b.width, bottom = b.top + b.height;
    switch (position) {
        case 'tr': t.set({ left: right - tw - pad, top: b.top + pad }); break;
        case 'bl': t.set({ left: b.left + pad,     top: bottom - th - pad }); break;
        case 'br': t.set({ left: right - tw - pad, top: bottom - th - pad }); break;
        default:   t.set({ left: b.left + pad,     top: b.top + pad }); // tl
    }
    t.setCoords();
    if (inst.tool === 'select') inst.canvas.setActiveObject(t);
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
//
// Both come back to .NET as bytes through a JS stream reference rather than as a string return
// value. A return value travels as one SignalR message, and the site caps those at 128 KB
// (Program.cs, MaximumReceiveMessageSize) - so a saved picture larger than that, which is every
// picture, failed on the way back and "Save as New Version" could never have worked.

function _bytes(text) {
    return new TextEncoder().encode(text);
}

export function getStateBytes(containerId) {
    const inst = _instances.get(containerId);
    if (!inst) return null;
    return _bytes(JSON.stringify(inst.canvas.toJSON(['layerId', 'layerName', 'selectable', 'evented'])));
}

/**
 * The edited picture at the photo's own size: the whole photo, and nothing of the grey around it,
 * however far the person had zoomed or dragged.
 */
export async function exportBytes(containerId, format, quality) {
    const inst = _instances.get(containerId);
    if (!inst) return null;
    const c = inst.canvas;
    c.discardActiveObject();
    const saved = c.viewportTransform.slice();
    c.setViewportTransform([1, 0, 0, 1, 0, 0]);
    const b = _bounds(inst);
    let dataUrl;
    try {
        dataUrl = c.toDataURL({
            format: format ?? 'png', quality: quality ?? 0.92, multiplier: 1,
            left: b.left, top: b.top, width: Math.round(b.width), height: Math.round(b.height),
        });
    } finally {
        c.setViewportTransform(saved);
        c.requestRenderAll();
    }
    const blob = await (await fetch(dataUrl)).blob();
    return new Uint8Array(await blob.arrayBuffer());
}

function _notifyChanged(containerId) {
    _instances.get(containerId)?.dotNetRef?.invokeMethodAsync('OnEditorChanged');
}
