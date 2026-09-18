// Draws the published picture of a board (M6-21, review correction R11).
//
// C# builds the scene from the document (Ben.Canvas.Core/Persistence/BoardSnapshot.cs); this draws it on a canvas
// that is never attached to the page and answers PNG bytes. Drawing from the document rather than capturing the
// screen means every block is in the picture, however large the board and wherever it was scrolled, and no
// page markup or third-party script is involved. Colours come from the editor's own theme tokens, so the picture
// matches the board in light and dark.

const FALLBACK = {
    ground: '#212529', surface: '#2b3035', border: '#495057', text: '#dee2e6', muted: '#adb5bd',
    edge: '#8c9299', accent: '#8796b9', group: 'rgba(55,80,138,.08)',
};

/** A theme token as a colour the canvas accepts; tokens that use color-mix() fall back when the canvas cannot parse them. */
/* Blends two resolved colours. color-mix() is CSS; the canvas needs the arithmetic. */
function mix(ctx, a, b, weight) {
    const rgb = value => {
        ctx.fillStyle = value;
        const resolved = ctx.fillStyle;
        if (resolved.startsWith('#')) {
            return [1, 3, 5].map(i => parseInt(resolved.slice(i, i + 2), 16));
        }
        const parts = resolved.match(/[\d.]+/g) || ['0', '0', '0'];
        return parts.slice(0, 3).map(Number);
    };
    const [ar, ag, ab] = rgb(a);
    const [br, bg, bb] = rgb(b);
    const at = Math.round(ar * weight + br * (1 - weight));
    const gt = Math.round(ag * weight + bg * (1 - weight));
    const bt = Math.round(ab * weight + bb * (1 - weight));
    return `rgb(${at}, ${gt}, ${bt})`;
}

function tokenColour(ctx, style, name, fallback) {
    const value = style.getPropertyValue(name).trim();
    if (!value) return fallback;
    const sentinel = '#010203';
    ctx.fillStyle = sentinel;
    ctx.fillStyle = value;
    const parsed = ctx.fillStyle;
    return parsed === sentinel && value.toLowerCase() !== sentinel ? fallback : parsed;
}

function makeCanvas(width, height) {
    if (typeof OffscreenCanvas !== 'undefined') return new OffscreenCanvas(width, height);
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    return canvas;
}

async function toPng(canvas) {
    if (canvas.convertToBlob) return canvas.convertToBlob({ type: 'image/png' });
    return new Promise((resolve, reject) => canvas.toBlob(b => (b ? resolve(b) : reject(new Error('toBlob'))), 'image/png'));
}

function roundRect(ctx, x, y, w, h, r) {
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.arcTo(x + w, y, x + w, y + h, r);
    ctx.arcTo(x + w, y + h, x, y + h, r);
    ctx.arcTo(x, y + h, x, y, r);
    ctx.arcTo(x, y, x + w, y, r);
    ctx.closePath();
}

/** Wraps words to a width and cuts at a line count, ending the last line with an ellipsis when cut. */
function wrap(ctx, lines, width, maxLines) {
    const out = [];
    for (const line of lines) {
        let current = '';
        for (const word of String(line).split(/\s+/)) {
            const next = current ? current + ' ' + word : word;
            if (ctx.measureText(next).width <= width || !current) current = next;
            else { out.push(current); current = word; }
            if (out.length >= maxLines) break;
        }
        if (current && out.length < maxLines) out.push(current);
        if (out.length >= maxLines) break;
    }
    if (out.length === maxLines) {
        let last = out[maxLines - 1];
        while (last.length > 1 && ctx.measureText(last + '…').width > width) last = last.slice(0, -1);
        out[maxLines - 1] = last + '…';
    }
    return out;
}

function loadImage(url) {
    return new Promise(resolve => {
        if (!url) { resolve(null); return; }
        const image = new Image();
        image.onload = () => resolve(image);
        image.onerror = () => resolve(null);
        image.src = url;
    });
}

/**
 * Draws a scene and answers the PNG as bytes.
 * @param {HTMLElement} root The editor root, whose theme tokens colour the picture.
 * @param {object} scene SnapshotScene from C#.
 */
export async function drawBoard(root, scene) {
    const canvas = makeCanvas(scene.pixelWidth, scene.pixelHeight);
    const ctx = canvas.getContext('2d');
    const style = getComputedStyle(root || document.documentElement);
    const c = {
        ground: tokenColour(ctx, style, '--bc-surface-1', FALLBACK.ground),
        surface: tokenColour(ctx, style, '--bc-surface-2', FALLBACK.surface),
        border: tokenColour(ctx, style, '--bc-border', FALLBACK.border),
        text: tokenColour(ctx, style, '--bc-text', FALLBACK.text),
        muted: tokenColour(ctx, style, '--bc-text-muted', FALLBACK.muted),
        edge: tokenColour(ctx, style, '--bc-edge-color', FALLBACK.edge),
        accent: tokenColour(ctx, style, '--bc-accent', FALLBACK.accent),
        group: tokenColour(ctx, style, '--bc-group-fill', FALLBACK.group),
    };
    const palette = key => (key ? tokenColour(ctx, style, '--bc-color-' + key, c.accent) : null);
    const typeColour = kind => tokenColour(ctx, style, '--bc-type-' + kind, c.accent);
    const font = style.getPropertyValue('--bs-body-font-family').trim() || 'system-ui, sans-serif';

    ctx.fillStyle = c.ground;
    ctx.fillRect(0, 0, scene.pixelWidth, scene.pixelHeight);
    ctx.setTransform(scene.scale, 0, 0, scene.scale, -scene.x * scene.scale, -scene.y * scene.scale);

    for (const g of scene.groups) {
        const colour = palette(g.colorKey) || c.accent;
        // A panel is solid in its own colour with a solid edge; an outline keeps the dashed tint it
        // has always had. Without this a published moodboard is a page of empty dashed rectangles.
        const panel = g.fill === 'Panel';
        roundRect(ctx, g.x, g.y, g.width, g.height, 8);
        ctx.fillStyle = panel ? mix(ctx, colour, c.surface, 0.14) : c.group;
        ctx.fill();
        if (panel) {
            ctx.lineWidth = 1;
        } else {
            ctx.setLineDash([6, 4]);
            ctx.lineWidth = 2;
        }
        ctx.strokeStyle = colour;
        ctx.stroke();
        ctx.setLineDash([]);
        if (g.label) {
            ctx.font = `600 13px ${font}`;
            ctx.fillStyle = c.text;
            ctx.textBaseline = 'bottom';
            ctx.fillText(g.label, g.x + 4, g.y - 4);
        }
    }

    for (const e of scene.connectors) {
        const colour = palette(e.colorKey) || c.edge;
        ctx.lineWidth = 2;
        ctx.strokeStyle = colour;
        ctx.stroke(new Path2D(e.path));
        ctx.fillStyle = colour;
        for (const h of e.heads) {
            ctx.beginPath();
            ctx.moveTo(h[0], h[1]);
            ctx.lineTo(h[2], h[3]);
            ctx.lineTo(h[4], h[5]);
            ctx.closePath();
            ctx.fill();
        }
        if (e.label) {
            ctx.font = `12px ${font}`;
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.lineWidth = 4;
            ctx.strokeStyle = c.ground;
            ctx.strokeText(e.label, e.labelX, e.labelY);
            ctx.fillStyle = c.text;
            ctx.fillText(e.label, e.labelX, e.labelY);
            ctx.textAlign = 'start';
        }
    }

    const images = await Promise.all(scene.blocks.map(b => loadImage(b.imageUrl)));

    scene.blocks.forEach((b, i) => {
        const accent = palette(b.colorKey) || typeColour(b.kind);
        roundRect(ctx, b.x, b.y, b.width, b.height, 8);
        // A filled block IS its colour; an unfilled one keeps the surface and wears the colour as a
        // 3 px bar down its left edge, which is what every board written before M9 looks like.
        ctx.fillStyle = b.filled
            ? accent
            : (b.kind === 'text' ? tokenColour(ctx, style, '--bc-sticky-bg', c.surface) : c.surface);
        ctx.fill();
        ctx.lineWidth = 1;
        ctx.strokeStyle = c.border;
        ctx.stroke();

        ctx.save();
        roundRect(ctx, b.x, b.y, b.width, b.height, 8);
        ctx.clip();
        if (!b.filled) {
            ctx.fillStyle = accent;
            ctx.fillRect(b.x, b.y, 3, b.height);
        }

        const pad = 10;
        const inner = b.width - pad * 2;
        ctx.textBaseline = 'top';
        ctx.font = `600 13px ${font}`;
        ctx.fillStyle = c.text;
        ctx.fillText(wrap(ctx, [b.title], inner, 1)[0] || '', b.x + pad, b.y + 9);
        ctx.strokeStyle = c.border;
        ctx.beginPath();
        ctx.moveTo(b.x, b.y + 32);
        ctx.lineTo(b.x + b.width, b.y + 32);
        ctx.stroke();

        let top = b.y + 40;
        const image = images[i];
        if (image) {
            const boxH = b.height - 40 - pad;
            const ratio = Math.min(inner / image.naturalWidth, boxH / image.naturalHeight);
            const w = image.naturalWidth * ratio;
            const h = image.naturalHeight * ratio;
            ctx.drawImage(image, b.x + pad + (inner - w) / 2, top + (boxH - h) / 2, w, h);
        } else if (b.lines.length) {
            ctx.font = `13px ${font}`;
            ctx.fillStyle = b.kind === 'map' || b.kind === 'file' ? c.muted : c.text;
            const lineHeight = 18;
            const room = Math.max(1, Math.floor((b.y + b.height - pad - top) / lineHeight));
            for (const line of wrap(ctx, b.lines, inner, room)) {
                ctx.fillText(line, b.x + pad, top);
                top += lineHeight;
            }
        }
        ctx.restore();
    });

    const blob = await toPng(canvas);
    return new Uint8Array(await blob.arrayBuffer());
}
