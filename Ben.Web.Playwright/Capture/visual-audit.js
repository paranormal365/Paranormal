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

      Modern browsers return some colours as "color(srgb 0.7 0.61 0.72)", with components from 0 to 1.
      Read as 0-255 those are almost black, and every outline button on the site was "1.87:1". They
      are 4.5:1.
*/
window.__benVisualAudit = function () {
  const out = [];
  const seen = new Set();

  const rgb = s => {
    s = s || '';
    const srgb = s.startsWith('color(srgb');
    const m = s.match(/[\d.]+/g);
    if (!m) return null;
    const v = m.slice(0, 3).map(Number);
    return srgb ? v.map(x => Math.round(x * 255)) : v;
  };
  const alpha = s => { const m = (s || '').match(/[\d.]+/g); return m && m.length > 3 ? +m[3] : 1; };
  const lum = c => {
    const [r, g, b] = c.map(v => { v /= 255; return v <= .03928 ? v / 12.92 : Math.pow((v + .055) / 1.055, 2.4); });
    return .2126 * r + .7152 * g + .0722 * b;
  };
  const ratio = (a, b) => { const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p); return (x + .05) / (y + .05); };

  // Up to and INCLUDING <html>: see the trap above.
  const bgOf = el => {
    let n = el;
    while (n) { const c = getComputedStyle(n).backgroundColor; if (alpha(c) > .5 && rgb(c)) return rgb(c); n = n.parentElement; }
    return [255, 255, 255];
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

    if (ownText(el) && parseFloat(s.fontSize) >= 10) {
      const fg = rgb(s.color);
      if (fg && alpha(s.color) > .5) {
        const c = ratio(fg, bgOf(el));
        const big = parseFloat(s.fontSize) >= 24 || (parseFloat(s.fontSize) >= 18.66 && +s.fontWeight >= 600);
        if (c < (big ? 3 : 4.5)) add('low contrast text', el, `${c.toFixed(2)}:1 — "${el.textContent.trim().slice(0, 24)}"`);
      }
    }
  }

  for (const img of document.images)
    if (img.complete && img.naturalWidth === 0 && img.getBoundingClientRect().width > 4)
      add('broken image', img, img.currentSrc || img.src);

  const de = document.documentElement;
  if (de.scrollWidth > de.clientWidth + 2)
    out.push({ kind: 'page scrolls sideways', el: 'html', detail: `${de.scrollWidth - de.clientWidth}px` });

  return out;
};
