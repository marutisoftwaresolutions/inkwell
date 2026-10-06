/*
 * Gallery block for the Desk editors (the post and page builders, the new-post editor, the Markdown
 * toolbar). One place builds and reads the markup, so every editor saves the same HTML:
 *
 *   <div class="comp-gallery" data-columns="3">
 *   <figure class="comp-gallery-item"><a href="/uploads/a.webp"><img src="/uploads/a.webp" alt="…" width="1200" height="800" loading="lazy" decoding="async"></a><figcaption>…</figcaption></figure>
 *   </div>
 *
 * It has no blank lines, so Markdown keeps it as one HTML block. Each image links to its own file, so the
 * gallery works without JavaScript; the reader's lightbox (inkwell-gallery.js) is an enhancement.
 */
(function () {
    'use strict';

    const COLUMNS = ['2', '3', '4'];
    let pendingResolve = null;

    function esc(s) {
        return String(s == null ? '' : s).replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    function normalizeColumns(c) { return COLUMNS.includes(String(c)) ? String(c) : '3'; }

    function itemHtml(it, i, n) {
        // A link whose only content is an image with empty alt has no name; give it the caption or its position.
        const name = (it.alt || '').trim() ? '' : ` aria-label="${esc((it.caption || '').trim() || `Image ${i + 1} of ${n}`)}"`;
        const size = (it.width && it.height) ? ` width="${esc(it.width)}" height="${esc(it.height)}"` : '';
        const cap = it.caption && it.caption.trim() ? `<figcaption>${esc(it.caption.trim())}</figcaption>` : '';
        return `<figure class="comp-gallery-item"><a href="${esc(it.url)}"${name}><img src="${esc(it.url)}" alt="${esc((it.alt || '').trim())}"${size} loading="lazy" decoding="async"></a>${cap}</figure>`;
    }

    /** The saved markup for a gallery model { columns, items: [{ url, alt, caption, width, height }] }. */
    function html(model) {
        const items = (model.items || []).filter(it => it && it.url);
        if (items.length === 0) return '';
        return `<div class="comp-gallery" data-columns="${normalizeColumns(model.columns)}">\n${items.map((it, i) => itemHtml(it, i, items.length)).join('\n')}\n</div>`;
    }

    /** Reads a .comp-gallery element back into its model. */
    function parse(el) {
        const items = Array.from(el.querySelectorAll('.comp-gallery-item')).map(f => {
            const img = f.querySelector('img');
            return {
                url: img ? img.getAttribute('src') : '',
                alt: img ? (img.getAttribute('alt') || '') : '',
                caption: (f.querySelector('figcaption')?.textContent || ''),
                width: img?.getAttribute('width') || null,
                height: img?.getAttribute('height') || null,
            };
        }).filter(it => it.url);
        return { columns: normalizeColumns(el.getAttribute('data-columns')), items };
    }

    function fromPicker(items) {
        return items.map(i => ({ url: i.url, alt: i.altText || '', caption: i.caption || '', width: i.width, height: i.height }));
    }

    /** Opens the media picker in multiple mode; resolves with the chosen items, or never if cancelled. */
    function pickItems() {
        return new Promise(resolve => {
            pendingResolve = resolve;   // a later pick replaces an abandoned one
            window.dispatchEvent(new CustomEvent('open-media-selector', { detail: { multiple: true } }));
        });
    }

    window.addEventListener('media-selected-many', e => {
        const resolve = pendingResolve;
        pendingResolve = null;
        if (resolve && e.detail && Array.isArray(e.detail.items)) resolve(fromPicker(e.detail.items));
    });

    /** Picks images and resolves with the markup of a new gallery. */
    function pick() {
        return pickItems().then(items => html({ columns: '3', items }));
    }

    // ── Settings panel (builders) ────────────────────────────────────────────
    let current = null;   // the .comp-gallery element being edited

    function touch(el) {
        // The Desk's unsaved-changes guard listens for input events on the form.
        (el || document.getElementById('galleryEditor'))?.dispatchEvent(new Event('input', { bubbles: true }));
    }

    function apply(model, focusIds, message) {
        if (!current) return;
        const tmp = document.createElement('div');
        tmp.innerHTML = html(model) || `<div class="comp-gallery" data-columns="${normalizeColumns(model.columns)}"></div>`;
        const next = tmp.firstElementChild;
        current.replaceWith(next);
        current = next;
        rerender(model, focusIds, message);
    }

    function announce(text) {
        let live = document.getElementById('gallery-live');
        if (!live) {
            live = document.createElement('p');
            live.id = 'gallery-live'; live.className = 'sr-only'; live.setAttribute('aria-live', 'polite');
            document.body.appendChild(live);
        }
        live.textContent = '';
        setTimeout(() => { live.textContent = text; }, 50);
    }

    // Redrawing replaces the panel, so focus goes back to the matching control and keyboard users keep their place.
    function rerender(model, focusIds, message) {
        const box = document.getElementById('galleryEditor');
        if (box) box.outerHTML = settingsFor(model);
        for (const id of focusIds || []) {
            const el = id && document.getElementById(id);
            if (el && !el.disabled) { el.focus(); break; }
        }
        if (message) announce(message);
        touch();
    }

    function settingsFor(model) {
        const n = model.items.length;
        const rows = model.items.map((it, i) => `
            <li class="gallery-row">
                <img src="${esc(it.url)}" alt="" class="gallery-thumb" />
                <div class="gallery-fields">
                    <label for="gal-alt-${i}">Alt text, image ${i + 1}</label>
                    <input id="gal-alt-${i}" type="text" value="${esc(it.alt)}" aria-describedby="gal-warn-${i}" oninput="InkwellGallery.setField(${i}, 'alt', this.value)" />
                    <p class="gallery-warn" id="gal-warn-${i}" ${it.alt ? 'hidden' : ''}>Describe the image for readers who cannot see it.</p>
                    <label for="gal-cap-${i}">Caption, image ${i + 1}</label>
                    <input id="gal-cap-${i}" type="text" value="${esc(it.caption)}" oninput="InkwellGallery.setField(${i}, 'caption', this.value)" />
                    <div class="gallery-actions">
                        <button type="button" id="gal-up-${i}" onclick="InkwellGallery.move(${i}, -1)" ${i === 0 ? 'disabled' : ''} aria-label="Move image ${i + 1} earlier">↑</button>
                        <button type="button" id="gal-down-${i}" onclick="InkwellGallery.move(${i}, 1)" ${i === n - 1 ? 'disabled' : ''} aria-label="Move image ${i + 1} later">↓</button>
                        <button type="button" id="gal-rm-${i}" onclick="InkwellGallery.remove(${i})" aria-label="Remove image ${i + 1} from the gallery">Remove</button>
                    </div>
                </div>
            </li>`).join('');
        return `<div id="galleryEditor" class="gallery-editor">
            <div class="setting-field"><label for="gal-cols">Columns</label>
                <select id="gal-cols" onchange="InkwellGallery.setColumns(this.value)">
                    ${COLUMNS.map(c => `<option value="${c}" ${model.columns === c ? 'selected' : ''}>${c}</option>`).join('')}
                </select>
                <p class="gallery-hint">On phones every gallery shows two images per row.</p>
            </div>
            <button type="button" id="gal-add" class="gallery-add" onclick="InkwellGallery.addImages()">Add images</button>
            ${n === 0 ? '<p class="gallery-hint">This gallery is empty, so readers see nothing here. Add images, or delete the block.</p>' : `<ol class="gallery-rows">${rows}</ol>`}
        </div>`;
    }

    /** Settings-panel HTML for the gallery inside a builder block's content element. */
    function settings(blockContent) {
        current = blockContent.querySelector('.comp-gallery');
        if (!current) return '<p class="text-xs text-muted-foreground">No gallery found in this block.</p>';
        return settingsFor(parse(current));
    }

    function edit(fn) {
        if (!current) return;
        const model = parse(current);
        const r = fn(model) || {};
        apply(model, r.focus, r.message);
    }

    window.InkwellGallery = {
        html, parse, pick, pickItems, settings,
        setColumns: c => edit(m => { m.columns = normalizeColumns(c); return { focus: ['gal-cols'], message: `${m.columns} columns.` }; }),
        // Typing updates the markup in place without redrawing the panel, so focus stays in the field.
        setField: (i, key, value) => {
            if (!current) return;
            const fig = current.querySelectorAll('.comp-gallery-item')[i];
            if (!fig) return;
            if (key === 'alt') {
                fig.querySelector('img')?.setAttribute('alt', value.trim());
                const a = fig.querySelector('a');
                if (value.trim()) a?.removeAttribute('aria-label');
                else a?.setAttribute('aria-label', fig.querySelector('figcaption')?.textContent || `Image ${i + 1}`);
                const warn = document.getElementById('gal-warn-' + i);
                if (warn) warn.hidden = !!value.trim();
                return;
            }
            let cap = fig.querySelector('figcaption');
            if (!value.trim()) { cap?.remove(); return; }
            if (!cap) { cap = document.createElement('figcaption'); fig.appendChild(cap); }
            cap.textContent = value.trim();
        },
        move: (i, d) => edit(m => {
            const j = i + d;
            if (j < 0 || j >= m.items.length) return;
            [m.items[i], m.items[j]] = [m.items[j], m.items[i]];
            const same = d < 0 ? 'gal-up-' : 'gal-down-', other = d < 0 ? 'gal-down-' : 'gal-up-';
            return { focus: [same + j, other + j, 'gal-alt-' + j], message: `Image ${i + 1} moved to position ${j + 1}.` };
        }),
        remove: i => edit(m => {
            m.items.splice(i, 1);
            const next = Math.min(i, m.items.length - 1);
            return { focus: next >= 0 ? ['gal-rm-' + next] : ['gal-add'], message: `Image removed. ${m.items.length} left.` };
        }),
        addImages: () => { const target = current; pickItems().then(items => { if (current !== target) current = target; edit(m => { m.items.push(...items); return { focus: ['gal-add'], message: `${items.length} added. ${m.items.length} in the gallery.` }; }); }); },
    };

    // In the editors a gallery link must not navigate away from unsaved work.
    document.addEventListener('click', e => {
        const a = e.target.closest && e.target.closest('.comp-gallery a');
        if (a && a.closest('#canvas, .ql-editor, .builder-block')) e.preventDefault();
    }, true);
})();
