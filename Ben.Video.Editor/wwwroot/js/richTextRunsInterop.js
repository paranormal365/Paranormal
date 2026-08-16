/**
 * richTextRunsInterop.js
 *
 * Converts a TelerikEditor's rich-text HTML Value into a flat array of styled text runs
 * (item #16 — inline mixed formatting + subscript/superscript). Uses the browser's own DOMParser
 * so the walk matches exactly what the editor itself produced, rather than reimplementing HTML
 * parsing in C#. The reverse direction (runs -> HTML, for populating the editor) is plain C# —
 * see TextRun.ToHtml — since building HTML needs no DOM, only parsing arbitrary contentEditable
 * output reliably does.
 *
 * Called from RichTextRunParserService.cs via IJSRuntime.
 * Served at: /_content/Ben.Video.Editor/js/richTextRunsInterop.js
 */

/**
 * @param {string} html  Rich-text HTML from a TelerikEditor's Value.
 * @returns {Object[]} Flat array of {text, bold, underline, sub, sup, color}, in document order.
 *                      color is a "#rrggbb" string or null. Adjacent fragments with identical
 *                      style are merged into one run.
 */
export function htmlToRuns(html) {
    const doc  = new DOMParser().parseFromString(html, 'text/html');
    const runs = [];

    function sameStyle(run, style) {
        return run.bold === !!style.bold && run.underline === !!style.underline &&
               run.sub === !!style.sub && run.sup === !!style.sup &&
               run.color === (style.color || null);
    }

    function pushRun(text, style) {
        if (text.length === 0) return;
        const last = runs[runs.length - 1];
        if (last && sameStyle(last, style)) { last.text += text; return; }
        runs.push({
            text,
            bold: !!style.bold, underline: !!style.underline,
            sub: !!style.sub, sup: !!style.sup,
            color: style.color || null,
        });
    }

    // Recognizes exactly the tags TelerikEditor's restricted [Bold, Underline, SubScript,
    // SuperScript, ForeColor] toolset can produce, tracking inherited style down the tree. Any
    // other tag just contributes its own text content with whatever style was already inherited —
    // never crashes, never silently drops text, even on unexpected input (e.g. a pasted <a>).
    function walkNodes(nodes, style) {
        for (const node of nodes) {
            if (node.nodeType === Node.TEXT_NODE) { pushRun(node.textContent, style); continue; }
            if (node.nodeType !== Node.ELEMENT_NODE) continue;

            const tag = node.tagName.toLowerCase();
            if (tag === 'br') { pushRun('\n', style); continue; }

            const childStyle = Object.assign({}, style);
            if (tag === 'b' || tag === 'strong') childStyle.bold = true;
            if (tag === 'u') childStyle.underline = true;
            if (tag === 'sub') childStyle.sub = true;
            if (tag === 'sup') childStyle.sup = true;
            if (node.style && node.style.color) {
                const hex = cssColorToHex(node.style.color);
                if (hex) childStyle.color = hex;
            }

            walkNodes(node.childNodes, childStyle);
        }
    }

    // TelerikEditor typically wraps each line in its own top-level <p> (or the whole thing in one
    // <p>/<div> for single-line content) rather than always using <br>. Insert a '\n' before every
    // top-level block element after the first, so paragraph-per-line output round-trips the same
    // way explicit <br>s do. Deeper/nested blocks aren't specially handled — this toolset doesn't
    // produce them.
    const topLevelBlocks = new Set(['p', 'div']);
    let sawFirstBlock = false;
    for (const node of doc.body.childNodes) {
        if (node.nodeType === Node.ELEMENT_NODE && topLevelBlocks.has(node.tagName.toLowerCase())) {
            if (sawFirstBlock) pushRun('\n', {});
            sawFirstBlock = true;
        }
        walkNodes([node], {});
    }

    return runs;
}

/** Converts a browser-normalized CSS color ("rgb(r, g, b)"/"rgba(r, g, b, a)") to "#rrggbb". */
function cssColorToHex(cssColor) {
    const m = cssColor.match(/^rgba?\((\d+),\s*(\d+),\s*(\d+)/);
    if (!m) return null;
    const hex = [m[1], m[2], m[3]].map(n => parseInt(n, 10).toString(16).padStart(2, '0'));
    return `#${hex.join('')}`;
}
