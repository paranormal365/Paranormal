/*
    Reads one rendered page and reports what looks wrong on it.

    Written on 2026-09-18, after Ben asked whether anything else on the site looked like the event
    card on the home page — whose body had no padding rule at all, so its text sat on the card's own
    border. That fault was invisible to every test we had: the markup was right, the component
    rendered, nothing threw. Only looking at it found it, and looking at every page by hand does not
    scale.

    So each check below is a shape that fault could take, written so it can be run on any page:

      text on the card edge     a bordered or filled box whose text starts within 2px of its border
      content clipped           a box with overflow:hidden that is hiding some of its own text
      colour hard-coded         a colour written into a style attribute instead of a theme token
      low contrast text         text that does not meet WCAG AA against what is actually behind it
      broken image              an <img> that finished loading with no pixels
      page scrolls sideways     the document is wider than the window

    TWO MEASUREMENT TRAPS, both of which produced false findings before they were fixed, and both of
    which are easy to reintroduce:

      The page's background lives on <html>, not on <body> — body is transparent. A walk that stops
      before <html> falls through to white and reports dark-on-dark text as 1.3:1. Every footer link
      on the site was "unreadable" until that was corrected; they are 13:1.

      Colours do not all arrive as "rgb(r, g, b)". A browser hands back whatever notation the
      stylesheet used: "color(srgb 0.7 0.61 0.72)" with components from 0 to 1, and — from Telerik's
      themes — "oklch(1 0 323.021)", which is white. Pulled apart by a regex and read as 0-255 the
      first is almost black (every outline button on the site was "1.87:1"; they are 4.5:1) and the
      second is a dark blue-green (the video editor's Preview button was "1.51:1"; it is 9.1:1).

      That happened twice, each time fixed by teaching the parser one more notation, and the second
      one cost an afternoon of chasing a contrast fault that did not exist. So there is no parser
      any more: a colour is PAINTED onto a 1x1 canvas and the pixel is read back. Whatever the
      browser can render, it can render into that pixel, in sRGB bytes, with its alpha — including
      notations that do not exist yet. Nothing here needs to know their names.
*/
window.__benVisualAudit = function () {
  const out = [];
  const seen = new Set();
  const exempt = [];

  // A colour, whatever notation it arrived in, as sRGB bytes plus alpha. See the trap above.
  const _canvas = document.createElement('canvas');
  _canvas.width = _canvas.height = 1;
  const _ctx = _canvas.getContext('2d', { willReadFrequently: true });
  const _painted = new Map();
  const paint = s => {
    s = (s || '').trim();
    if (!s) return null;
    if (_painted.has(s)) return _painted.get(s);

    // fillStyle keeps its previous value when handed something it cannot parse, so a known
    // starting colour is what tells us the browser accepted this one.
    _ctx.fillStyle = '#000000';
    _ctx.fillStyle = s;
    const rejected = _ctx.fillStyle === '#000000'
      && !/^(#000|#000000|black|rgba?\(0,\s*0,\s*0(,\s*1)?\))$/i.test(s);

    let v = null;
    if (!rejected) {
      _ctx.clearRect(0, 0, 1, 1);
      _ctx.fillRect(0, 0, 1, 1);
      const d = _ctx.getImageData(0, 0, 1, 1).data;
      v = { rgb: [d[0], d[1], d[2]], a: d[3] / 255 };
    }
    _painted.set(s, v);
    return v;
  };
  const rgb = s => paint(s)?.rgb ?? null;
  const alpha = s => paint(s)?.a ?? 1;
  const lum = c => {
    const [r, g, b] = c.map(v => { v /= 255; return v <= .03928 ? v / 12.92 : Math.pow((v + .055) / 1.055, 2.4); });
    return .2126 * r + .7152 * g + .0722 * b;
  };
  const ratio = (a, b) => { const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p); return (x + .05) / (y + .05); };

  // Up to and INCLUDING <html>: see the trap above.
  //
  // Returns what it measured against AND which element painted it. Naming the ancestor is not a
  // nicety: the dark theme's colours were first tuned against the body (#212529), where they all
  // read a comfortable 6.2:1, when almost everything on this site sits on div.app-content
  // (#363c41) and read 4.47 there. A ratio without its backdrop cannot be checked or reproduced.
  //
  // A gradient or an image stops the walk ONLY when it is the whole background. That qualifier is
  // the entire rule: the first cut of this stopped at any background-image at all, and since
  // aside.app-sidebar paints a faint tint over its solid colour, every nav link on every page
  // became "not measurable" — 3,730 rows, where the fault it replaced was 39. An image over a
  // solid colour is still measurable against that colour; an image over nothing is not, and that
  // is the org banner and the pricing-band heading, where continuing the walk invented "1.00:1".
  const bgOf = el => {
    let n = el;
    while (n) {
      const cs = getComputedStyle(n);
      const c = cs.backgroundColor;
      if (alpha(c) > .5 && rgb(c)) return { rgb: rgb(c), on: n };
      if (cs.backgroundImage && cs.backgroundImage !== 'none') return { unmeasurable: n };
      n = n.parentElement;
    }
    return { rgb: [255, 255, 255], on: null };
  };

  const path = el => {
    const id = el.id ? '#' + el.id : '';
    const cls = (el.className || '').toString().split(/\s+/).filter(Boolean).slice(0, 2).map(c => '.' + c).join('');
    return el.tagName.toLowerCase() + id + cls;
  };
  const add = (kind, el, detail) => {
    const key = kind + '|' + path(el) + '|' + detail;
    if (seen.has(key)) return;
    seen.add(key);
    out.push({ kind, el: path(el), detail });
  };

  const visible = el => {
    const r = el.getBoundingClientRect(), s = getComputedStyle(el);
    return r.width > 4 && r.height > 4 && s.visibility !== 'hidden' && s.display !== 'none' && +s.opacity > .05;
  };
  const ownText = el => [...el.childNodes].some(n => n.nodeType === 3 && n.textContent.trim().length > 1);

  for (const el of document.querySelectorAll('body *')) {
    if (!visible(el)) continue;
    const s = getComputedStyle(el), r = el.getBoundingClientRect();

    const bordered = parseFloat(s.borderTopWidth) >= 1 || parseFloat(s.borderLeftWidth) >= 1;
    const filled = alpha(s.backgroundColor) > .5 && el.parentElement
      && getComputedStyle(el.parentElement).backgroundColor !== s.backgroundColor;

    // A DATA GRID is not a card: its cells are meant to start at the content box's own corner, and
    // its scrolling body is a viewport rather than a frame round something. Telerik's grid produced
    // eight of the first run's ten "faults" this way.
    const isGridPart = /\bk-(grid|table|pager|header|virtual)/.test((el.className || '').toString());
    const scrolls = /auto|scroll/.test(s.overflow + s.overflowY + s.overflowX);

    if ((bordered || filled) && !isGridPart && !scrolls && r.width >= 90 && r.height >= 40 && el.children.length) {
      for (const kid of el.querySelectorAll('*')) {
        if (!ownText(kid) || !visible(kid)) continue;

        // Where the WORDS are, not where their box is. A .card-header's box starts on the card's
        // border and is perfectly fine, because its own padding holds the text away from it —
        // measuring the box called every such header a fault. A Range measures the glyphs.
        const t = [...kid.childNodes].find(n => n.nodeType === 3 && n.textContent.trim().length > 1);
        if (!t) continue;
        const range = document.createRange();
        range.selectNodeContents(t);
        const k = range.getBoundingClientRect();
        if (!k.width || !k.height) continue;

        const left = k.left - r.left, top = k.top - r.top;
        if (left >= -1 && left <= 2 && top >= -1 && top <= 2) {
          add('text on the card edge', el, `"${kid.textContent.trim().slice(0, 28)}" at ${Math.round(left)},${Math.round(top)}`);
          break;
        }
      }
    }

    if ((s.overflow === 'hidden' || s.overflowY === 'hidden') && !isGridPart) {
      // More than a couple of lines: a pixel or two is rounding, a hidden sentence is a fault.
      if (el.scrollHeight > el.clientHeight + 12 && el.clientHeight > 20 && el.textContent.trim().length > 12)
        add('content clipped', el, `${el.scrollHeight - el.clientHeight}px hidden`);
    }

    const inline = el.getAttribute('style') || '';
    if (/(^|[^-\w])(color|background(-color)?)\s*:\s*(#[0-9a-f]{3,8}|rgb)/i.test(inline))
      add('colour hard-coded in markup', el, inline.slice(0, 60));

    // Disabled controls are exempt from the contrast minimum (WCAG 1.4.3 excludes inactive user
    // interface components), and reporting them buries the faults that matter: 28 of the 60 rows
    // left in the dark sweep were one greyed-out toolbar and a Preview button on a merge screen
    // with nothing chosen yet. Bootstrap greys them through --bs-btn-disabled-color, a different
    // variable from the one a theme fix touches, so they also survive every fix and reappear in
    // every report looking like a regression.
    const inert = el.closest(
      ':disabled, [aria-disabled="true"], .disabled, .k-disabled, fieldset[disabled]') !== null;

    // The disabled ones are still MEASURED, and counted at the end. Dropping them in silence is
    // its own fault: a reader cannot tell a control that passed from one that was never looked at,
    // and the merge screen's Preview button spent a whole report cycle looking like a fixed fault
    // when nothing about it had changed.
    if (ownText(el) && parseFloat(s.fontSize) >= 10) {
      const fg = rgb(s.color);
      if (fg && alpha(s.color) > .5) {
        const bg = bgOf(el);
        const big = parseFloat(s.fontSize) >= 24 || (parseFloat(s.fontSize) >= 18.66 && +s.fontWeight >= 600);
        const quote = `"${el.textContent.trim().slice(0, 24)}"`;
        if (bg.unmeasurable) {
          if (!inert)
            add('contrast not measurable', el,
                `over a gradient or image on ${path(bg.unmeasurable)} — ${quote} needs checking by eye`);
        } else {
          const c = ratio(fg, bg.rgb);
          if (c < (big ? 3 : 4.5)) {
            if (inert) exempt.push({ el: path(el), ratio: c });
            else
              add('low contrast text', el,
                  `${c.toFixed(2)}:1 on rgb(${bg.rgb}) — ${quote}` + (bg.on ? ` — behind it: ${path(bg.on)}` : ''));
          }
        }
      }
    }
  }

  // One row, not one per element: saying so must not re-bury what it was introduced to surface.
  if (exempt.length) {
    const worst = exempt.reduce((a, b) => (b.ratio < a.ratio ? b : a));
    const names = [...new Set(exempt.map(e => e.el))];
    out.push({
      kind: 'contrast exempt (disabled)',
      el: `${exempt.length} element${exempt.length === 1 ? '' : 's'}`,
      detail: `under the minimum but disabled, so not counted as faults — worst ${worst.ratio.toFixed(2)}:1 on `
            + `${worst.el}; ${names.slice(0, 4).join(', ')}${names.length > 4 ? ` and ${names.length - 4} more` : ''}`
    });
  }

  for (const img of document.images)
    if (img.complete && img.naturalWidth === 0 && img.getBoundingClientRect().width > 4)
      add('broken image', img, img.currentSrc || img.src);

  const de = document.documentElement;
  if (de.scrollWidth > de.clientWidth + 2)
    out.push({ kind: 'page scrolls sideways', el: 'html', detail: `${de.scrollWidth - de.clientWidth}px` });

  return out;
};
