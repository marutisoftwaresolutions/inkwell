/*
 * Inkwell block builder: the one engine behind the post and page editors (ADR-0026 follow-up, v1.0.9).
 * It used to be three inline copies that drifted apart; every editor now loads this file and calls
 * InkwellBuilder.init with its own elements. A block type is one InkwellBuilder.register call:
 *
 *   register(type, {
 *     title:     'Gallery',                          // settings panel heading
 *     template:  () => html | Promise<html|null>,    // markup for a new block (null cancels)
 *     settings:  (content) => html,                  // settings panel body for the selected block
 *     recognise: (el) => bool,                       // claims a saved top-level element when editing
 *     prepare:   (el) => html,                       // makes a recognised element editable again
 *     order:     number                              // recognisers run lowest first (specific before generic)
 *   })
 *
 * Saved markup is plain HTML with no editor attributes: extract() strips contenteditable, onclick and the
 * editor-only placeholders. Global function names (selectBlockImage, moveBlock, updateBtnLabel, …) are kept
 * because the block toolbars and settings panels call them inline.
 */
(function () {
    'use strict';

    var registry = {};
    var state = null;   // the one builder on the page

    function esc(s) {
        return String(s == null ? '' : s).replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    function editable(el, selector) { el.querySelectorAll(selector).forEach(function (x) { x.setAttribute('contenteditable', 'true'); }); }

    function register(type, def) { registry[type] = Object.assign({ order: 50, title: type }, def); }

    function parseVideoUrl(url) {
        var m = url.match(/(?:youtube\.com\/watch\?v=|youtu\.be\/)([\w-]+)/);
        if (m) return 'https://www.youtube.com/embed/' + m[1];
        m = url.match(/vimeo\.com\/(\d+)/);
        if (m) return 'https://player.vimeo.com/video/' + m[1];
        return null;
    }

    function alignSelect(current, withJustify) {
        var opts = [['left', 'Left'], ['center', 'Center'], ['right', 'Right']].concat(withJustify ? [['justify', 'Justify']] : []);
        return '<div class="setting-field"><label>Alignment</label><select onchange="changeBlockAlign(this.value)">' +
            opts.map(function (o) { return '<option value="' + o[0] + '"' + ((current || 'left') === o[0] ? ' selected' : '') + '>' + o[1] + '</option>'; }).join('') +
            '</select></div>';
    }

    var imageButton = '<div class="setting-field"><button type="button" onclick="changeBlockImage()" class="w-full px-3 py-2 bg-secondary text-secondary-foreground border border-border rounded-md text-xs font-medium">Change image</button></div>';

    // ── Built-in blocks ─────────────────────────────────────────────────────────

    register('heading', {
        title: 'Heading', order: 90,
        template: function () { return '<h2 contenteditable="true" style="font-size:1.8rem;font-weight:700;color:var(--foreground);">Your heading here</h2>'; },
        settings: function (b) {
            var h = b.querySelector('h1,h2,h3,h4,h5,h6');
            return '<div class="setting-field"><label>Heading text</label><input type="text" value="' + esc(h && h.textContent) + '" oninput="updateBlockText(this.value, \'h1,h2,h3,h4,h5,h6\')" /></div>' +
                '<div class="setting-field"><label>Level</label><select onchange="changeHeadingLevel(this.value)">' +
                ['h1', 'h2', 'h3'].map(function (l) { return '<option value="' + l + '"' + (h && h.tagName === l.toUpperCase() ? ' selected' : '') + '>' + l.toUpperCase() + '</option>'; }).join('') +
                '</select></div>' + alignSelect(h && h.style.textAlign, false);
        },
        recognise: function (el) { return /^H[1-6]$/.test(el.tagName); },
        prepare: function (el) { el.setAttribute('contenteditable', 'true'); return el.outerHTML; }
    });

    register('text', {
        title: 'Text', order: 95,
        template: function () { return '<p contenteditable="true" style="font-size:1rem;line-height:1.8;color:var(--foreground);">Click to edit this text block.</p>'; },
        settings: function (b) {
            var p = b.querySelector('p');
            return '<div class="setting-field"><label>Text</label><textarea rows="3" oninput="updateBlockText(this.value, \'p\')">' + esc(p && p.textContent) + '</textarea></div>' +
                alignSelect(p && p.style.textAlign, true);
        },
        recognise: function (el) { return el.tagName === 'P'; },
        prepare: function (el) { el.setAttribute('contenteditable', 'true'); return el.outerHTML; }
    });

    register('gallery', {
        title: 'Gallery', order: 10,
        template: function () { return InkwellGallery.pick(); },
        settings: function (b) { return InkwellGallery.settings(b); },
        recognise: function (el) { return el.classList.contains('comp-gallery'); },   // before images: a gallery is full of them
        prepare: function (el) { return el.outerHTML; }
    });

    register('image', {
        title: 'Image', order: 80,
        template: function () {
            return '<div class="comp-image"><div class="img-placeholder" onclick="selectBlockImage(this)"><svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" style="margin:0 auto .5rem;display:block;color:var(--muted-foreground)" aria-hidden="true"><rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="8.5" cy="8.5" r="1.5"/><polyline points="21 15 16 10 5 21"/></svg><span style="color:var(--muted-foreground);font-size:.85rem;">Click to select an image</span></div></div>';
        },
        settings: function (b) {
            var img = b.querySelector('.comp-image img'), w = (img && img.style.width) || '100%';
            return '<div class="setting-field"><label>Size</label><select onchange="updateImgSize(this.value)">' +
                [['100%', 'Full width'], ['75%', 'Large (75%)'], ['50%', 'Medium (50%)'], ['33%', 'Small (33%)']].map(function (o) { return '<option value="' + o[0] + '"' + (w === o[0] ? ' selected' : '') + '>' + o[1] + '</option>'; }).join('') +
                '</select></div><div class="setting-field"><label>Alt text</label><input type="text" value="' + esc(img && img.alt) + '" oninput="updateImgAlt(this.value)" /></div>' + imageButton;
        },
        recognise: function (el) { return el.classList.contains('comp-image') || el.tagName === 'IMG'; },
        prepare: function (el) { return el.tagName === 'IMG' ? '<div class="comp-image">' + el.outerHTML + '</div>' : el.outerHTML; }
    });

    register('button', {
        title: 'Button', order: 20,
        template: function () { return '<div class="comp-button"><a href="#" contenteditable="true">Click me</a></div>'; },
        settings: function (b) {
            var a = b.querySelector('.comp-button a'), outline = a && a.style.background === 'transparent';
            return '<div class="setting-field"><label>Label</label><input type="text" value="' + esc(a && a.textContent) + '" oninput="updateBtnLabel(this.value)" /></div>' +
                '<div class="setting-field"><label>Link URL</label><input type="text" value="' + esc((a && a.getAttribute('href')) || '#') + '" oninput="updateBtnUrl(this.value)" placeholder="https://…" /></div>' +
                '<div class="setting-field"><label>Style</label><select onchange="updateBtnStyle(this.value)"><option value="filled"' + (outline ? '' : ' selected') + '>Filled</option><option value="outline"' + (outline ? ' selected' : '') + '>Outline</option></select></div>';
        },
        recognise: function (el) { return el.classList.contains('comp-button'); },
        prepare: function (el) { editable(el, 'a'); return el.outerHTML; }
    });

    register('hero', {
        title: 'Hero', order: 20,
        template: function () { return '<div class="comp-hero"><h2 contenteditable="true">Welcome to our site</h2><p contenteditable="true">A brief description.</p><a href="#" contenteditable="true">Get started</a></div>'; },
        settings: function (b) {
            var a = b.querySelector('.comp-hero a');
            return '<div class="setting-field"><label>Button label</label><input type="text" value="' + esc(a && a.textContent) + '" oninput="updateHeroBtnLabel(this.value)" /></div>' +
                '<div class="setting-field"><label>Button URL</label><input type="text" value="' + esc((a && a.getAttribute('href')) || '#') + '" oninput="updateHeroBtnUrl(this.value)" placeholder="https://…" /></div>';
        },
        recognise: function (el) { return el.classList.contains('comp-hero'); },
        prepare: function (el) { editable(el, 'h2,p,a'); return el.outerHTML; }
    });

    register('imagetext', {
        title: 'Image + text', order: 20,
        template: function () { return '<div class="comp-imagetext"><img src="" alt="" onclick="selectBlockImage(this)" style="cursor:pointer;width:50%;" /><div class="it-content"><h3 contenteditable="true" style="font-size:1.3rem;font-weight:700;margin-bottom:0.5rem;">Featured title</h3><p contenteditable="true" style="line-height:1.7;">Describe your feature here.</p></div></div>'; },
        settings: function (b) {
            var img = b.querySelector('.comp-imagetext img'), w = (img && img.style.width) || '50%';
            return '<div class="setting-field"><label>Image size</label><select onchange="updateImgTextSize(this.value)">' +
                [['30%', 'Small (30%)'], ['40%', 'Medium (40%)'], ['50%', 'Large (50%)']].map(function (o) { return '<option value="' + o[0] + '"' + (w === o[0] ? ' selected' : '') + '>' + o[1] + '</option>'; }).join('') +
                '</select></div>' + imageButton;
        },
        recognise: function (el) { return el.classList.contains('comp-imagetext'); },
        prepare: function (el) {
            editable(el, 'h3,p');
            var img = el.querySelector('img');
            if (img) { img.setAttribute('onclick', 'selectBlockImage(this)'); img.style.cursor = 'pointer'; }
            return el.outerHTML;
        }
    });

    register('quote', {
        title: 'Quote', order: 20,
        template: function () { return '<div class="comp-quote"><blockquote contenteditable="true">"The best way to predict the future is to create it."</blockquote><cite contenteditable="true">— Abraham Lincoln</cite></div>'; },
        settings: function () { return '<p class="text-xs text-muted-foreground">Click the quote on the canvas to edit it.</p>'; },
        recognise: function (el) { return el.classList.contains('comp-quote'); },
        prepare: function (el) { editable(el, 'blockquote,cite'); return el.outerHTML; }
    });

    register('divider', {
        title: 'Divider', order: 30,
        template: function () { return '<div class="comp-divider"><hr/></div>'; },
        settings: function () { return '<p class="text-xs text-muted-foreground">A horizontal line. It has no settings.</p>'; },
        recognise: function (el) { return el.classList.contains('comp-divider') || el.tagName === 'HR'; },
        prepare: function (el) { return el.tagName === 'HR' ? el.outerHTML : '<div class="comp-divider"><hr/></div>'; }   // a bare <hr> stays bare
    });

    register('spacer', {
        title: 'Spacer', order: 20,
        template: function () { return '<div class="comp-spacer" style="height:60px;"><span>spacer</span></div>'; },
        settings: function (b) {
            var s = b.querySelector('.comp-spacer'), h = parseInt(s && s.style.height, 10) || 60;
            return '<div class="setting-field"><label>Height (px)</label><input type="number" value="' + h + '" min="10" max="300" oninput="updateSpacerHeight(this.value)" /></div>';
        },
        recognise: function (el) { return el.classList.contains('comp-spacer'); },
        prepare: function (el) { return '<div class="comp-spacer" style="height:' + (el.style.height || '60px') + ';"><span>spacer</span></div>'; }
    });

    register('video', {
        title: 'Video', order: 20,
        // Asks for the URL through the Desk dialog with live validation; resolves to HTML, or null when cancelled.
        template: function () {
            return InkwellDialog.prompt({
                title: 'Embed a video',
                body: 'Paste a YouTube or Vimeo link. The player is embedded lazily and never blocks the page.',
                label: 'Video URL', placeholder: 'https://www.youtube.com/watch?v=…', action: 'Embed video',
                validate: function (v) { return v.trim() && !parseVideoUrl(v.trim()) ? 'That is not a YouTube or Vimeo URL.' : null; },
                valid: 'Looks good.'
            }).then(function (url) {
                if (!url) return null;
                var embed = parseVideoUrl(url);
                return embed ? '<div class="comp-video"><iframe src="' + esc(embed) + '" allowfullscreen loading="lazy"></iframe></div>' : null;
            });
        },
        settings: function (b) {
            var f = b.querySelector('iframe');
            return '<div class="setting-field"><label>Video URL</label><input type="text" value="' + esc(f && f.src) + '" oninput="updateVideoUrl(this.value)" /></div>';
        },
        recognise: function (el) { return el.classList.contains('comp-video'); },
        prepare: function (el) { return el.outerHTML; }
    });

    // Contact form (ADR-0030): the page stores only a placeholder naming the form; the public page renders the real
    // form with fresh tokens. The palette offers it on pages only.
    var formOptions = null;
    function loadForms() {
        if (formOptions) return Promise.resolve(formOptions);
        return fetch('/admin/forms/options', { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (r) { return r.ok ? r.json() : []; })
            .then(function (list) { formOptions = list || []; return formOptions; })
            .catch(function () { return []; });
    }
    function formMarkup(f) { return '<div class="comp-form" data-form="' + esc(f.key) + '" data-form-name="' + esc(f.name) + '"></div>'; }
    window.changeBlockForm = function (key) {
        var div = q('.comp-form'); if (!div || !formOptions) return;
        var f = formOptions.filter(function (x) { return x.key === key; })[0]; if (!f) return;
        div.setAttribute('data-form', f.key); div.setAttribute('data-form-name', f.name);
    };
    register('form', {
        title: 'Form', order: 10,
        template: function () {
            return loadForms().then(function (list) {
                if (!list.length) {
                    if (window.InkwellDialog) InkwellDialog.toast('error', 'There are no forms yet. Create one in Forms, then add it here.');
                    return null;
                }
                var first = list.filter(function (f) { return f.active; })[0] || list[0];
                return formMarkup(first);
            });
        },
        settings: function (b) {
            var div = b.querySelector('.comp-form'), current = div && div.getAttribute('data-form');
            var list = formOptions || [];
            if (!formOptions) loadForms().then(function () { if (state && state.selected) window.selectBlock(state.selected); });
            return '<div class="setting-field"><label>Form</label><select onchange="changeBlockForm(this.value)">' +
                list.map(function (f) { return '<option value="' + esc(f.key) + '"' + (f.key === current ? ' selected' : '') + '>' + esc(f.name) + (f.active ? '' : ' (off)') + '</option>'; }).join('') +
                (list.some(function (f) { return f.key === current; }) ? '' : '<option value="" selected>' + esc(current || '') + ' (deleted)</option>') +
                '</select></div><p class="text-xs text-muted-foreground">Fields, button and messages are set in <a class="underline" href="/admin/forms">Forms</a>. Readers see the real form; it shows nothing while the form is off.</p>';
        },
        recognise: function (el) { return el.classList.contains('comp-form'); },
        prepare: function (el) { return el.outerHTML; }
    });

    // ── Inline helpers the toolbars and settings panels call ─────────────────────

    function sel() { return state && state.selected; }
    function q(selector) { var b = sel(); return b ? b.querySelector(selector) : null; }

    window.changeHeadingLevel = function (level) {
        var old = q('h1,h2,h3,h4,h5,h6'); if (!old) return;
        var nw = document.createElement(level);
        nw.innerHTML = old.innerHTML; nw.setAttribute('contenteditable', 'true'); nw.style.cssText = old.style.cssText;
        nw.style.fontSize = { h1: '2.2rem', h2: '1.8rem', h3: '1.4rem' }[level];
        old.replaceWith(nw);
    };
    window.changeBlockAlign = function (a) { var el = q('[contenteditable]'); if (el) el.style.textAlign = a; };
    window.updateBlockText = function (v, selector) { var el = q(selector); if (el) el.textContent = v; };
    window.updateBtnLabel = function (v) { var a = q('.comp-button a'); if (a) a.textContent = v; };
    window.updateBtnUrl = function (v) { var a = q('.comp-button a'); if (a) a.href = v; };
    window.updateBtnStyle = function (v) {
        var a = q('.comp-button a'); if (!a) return;
        if (v === 'outline') { a.style.background = 'transparent'; a.style.border = '2px solid var(--primary)'; a.style.color = 'var(--primary)'; }
        else { a.style.background = 'var(--primary)'; a.style.border = 'none'; a.style.color = 'var(--primary-foreground)'; }
    };
    window.updateImgSize = function (v) { var img = q('.comp-image img'); if (img) { img.style.width = v; img.style.margin = v === '100%' ? '0' : '0 auto'; img.style.display = 'block'; } };
    window.updateImgAlt = function (v) { var img = q('.comp-image img'); if (img) img.alt = v; };
    window.changeBlockImage = function () { var img = q('img'); if (img) window.selectBlockImage(img); };
    window.updateHeroBtnLabel = function (v) { var a = q('.comp-hero a'); if (a) a.textContent = v; };
    window.updateHeroBtnUrl = function (v) { var a = q('.comp-hero a'); if (a) a.href = v; };
    window.updateSpacerHeight = function (v) { var s = q('.comp-spacer'); if (s) s.style.height = v + 'px'; };
    window.updateImgTextSize = function (v) { var img = q('.comp-imagetext img'); if (img) img.style.width = v; };
    window.updateVideoUrl = function (v) { var f = q('iframe'); if (f) f.src = v; };

    window._pendingImageTarget = null;
    window.selectBlockImage = function (el) {
        window._pendingImageTarget = el;
        window.dispatchEvent(new CustomEvent('open-media-selector', { detail: { targetInput: '__blockImage__' } }));
    };

    // ── The builder ─────────────────────────────────────────────────────────────

    var toolbar = '<div class="block-toolbar">' +
        '<button type="button" class="drag-handle" title="Drag" aria-label="Drag"><svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><circle cx="9" cy="5" r="1"/><circle cx="9" cy="12" r="1"/><circle cx="9" cy="19" r="1"/><circle cx="15" cy="5" r="1"/><circle cx="15" cy="12" r="1"/><circle cx="15" cy="19" r="1"/></svg></button>' +
        '<button type="button" onclick="moveBlock(this,-1)" title="Move up" aria-label="Move up"><svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><polyline points="18 15 12 9 6 15"/></svg></button>' +
        '<button type="button" onclick="moveBlock(this,1)" title="Move down" aria-label="Move down"><svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><polyline points="6 9 12 15 18 9"/></svg></button>' +
        '<button type="button" onclick="duplicateBlock(this)" title="Duplicate" aria-label="Duplicate"><svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/></svg></button>' +
        '<button type="button" onclick="deleteBlock(this)" title="Delete" aria-label="Delete" style="color:var(--destructive)"><svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg></button>' +
        '</div>';

    /**
     * Starts the builder. opts: { canvas, canvasEmpty, canvasScroll, settingsView, componentView, componentTitle,
     * componentBody, content (hidden input holding saved HTML), hydrate (bool) }. Element options take an id or an element.
     * Returns { extract(), extractText(), hydrate() }.
     */
    function init(opts) {
        function $(x) { return typeof x === 'string' ? document.getElementById(x) : x; }
        var s = state = {
            canvas: $(opts.canvas), empty: $(opts.canvasEmpty), scroll: $(opts.canvasScroll),
            settingsView: $(opts.settingsView), componentView: $(opts.componentView),
            componentTitle: $(opts.componentTitle), componentBody: $(opts.componentBody), content: $(opts.content),
            selected: null, dragSrc: null, dragFromPalette: null
        };

        function updateEmpty() { if (s.empty) s.empty.style.display = s.canvas.querySelectorAll('.builder-block').length === 0 ? 'flex' : 'none'; }
        function deselect() { if (s.selected) s.selected.classList.remove('selected'); s.selected = null; }
        function showSettings() { deselect(); s.settingsView.classList.remove('hidden'); s.componentView.classList.add('hidden'); }
        function showComponent(block) {
            var def = registry[block.dataset.type], content = block.querySelector('.block-content');
            s.componentTitle.textContent = def ? def.title : 'Block';
            s.componentBody.innerHTML = def && def.settings ? def.settings(content) : '<p class="text-xs text-muted-foreground">This block has no settings.</p>';
            s.settingsView.classList.add('hidden'); s.componentView.classList.remove('hidden');
        }
        function select(block) { deselect(); s.selected = block; block.classList.add('selected'); showComponent(block); }

        function wire(block) {
            block.addEventListener('dragstart', function (e) { s.dragSrc = this; this.classList.add('dragging'); e.dataTransfer.effectAllowed = 'move'; e.dataTransfer.setData('text/html', ''); });
            block.addEventListener('dragend', function () { this.classList.remove('dragging'); clearIndicators(); s.dragSrc = null; });
            block.addEventListener('dragover', function (e) {
                e.preventDefault(); if (!s.dragSrc || s.dragSrc === this) return;
                clearIndicators();
                var r = this.getBoundingClientRect(), ind = document.createElement('div'); ind.className = 'drop-indicator';
                this.parentNode.insertBefore(ind, e.clientY < r.top + r.height / 2 ? this : this.nextSibling);
            });
            block.addEventListener('drop', function (e) {
                e.stopPropagation(); e.preventDefault(); clearIndicators();
                if (s.dragFromPalette) { add(s.dragFromPalette, this); s.dragFromPalette = null; return; }
                if (!s.dragSrc || s.dragSrc === this) return;
                var r = this.getBoundingClientRect();
                this.parentNode.insertBefore(s.dragSrc, e.clientY < r.top + r.height / 2 ? this : this.nextSibling);
            });
            block.addEventListener('click', function (e) { if (!e.target.closest('.block-toolbar')) select(block); });
            return block;
        }
        function clearIndicators() { s.canvas.querySelectorAll('.drop-indicator').forEach(function (el) { el.remove(); }); }

        function create(html, type) {
            var block = document.createElement('div');
            block.className = 'builder-block'; block.dataset.type = type; block.draggable = true;
            block.innerHTML = toolbar + '<div class="block-content">' + html + '</div>';
            return wire(block);
        }

        function add(type, before) {
            var def = registry[type]; if (!def) return;
            Promise.resolve(def.template()).then(function (html) {
                if (html === null || html === undefined) return;
                var block = create(html, type);
                if (before) s.canvas.insertBefore(block, before); else s.canvas.appendChild(block);
                updateEmpty(); select(block);
            });
        }

        window.showPageSettings = window.showPostSettings = showSettings;
        window.selectBlock = select;
        window.moveBlock = function (btn, dir) {
            var b = btn.closest('.builder-block');
            if (dir === -1 && b.previousElementSibling && b.previousElementSibling.classList.contains('builder-block')) b.parentNode.insertBefore(b, b.previousElementSibling);
            else if (dir === 1 && b.nextElementSibling && b.nextElementSibling.classList.contains('builder-block')) b.parentNode.insertBefore(b.nextElementSibling, b);
        };
        window.deleteBlock = function (btn) { var b = btn.closest('.builder-block'); if (b === s.selected) showSettings(); b.remove(); updateEmpty(); };
        window.duplicateBlock = function (btn) { var b = btn.closest('.builder-block'); b.parentNode.insertBefore(wire(b.cloneNode(true)), b.nextSibling); };

        document.querySelectorAll('.palette-item').forEach(function (btn) {
            btn.addEventListener('dragstart', function (e) { s.dragFromPalette = btn.dataset.component; e.dataTransfer.effectAllowed = 'copy'; e.dataTransfer.setData('text/plain', btn.dataset.component); });
            btn.addEventListener('dragend', function () { s.dragFromPalette = null; });
            btn.addEventListener('click', function () { add(btn.dataset.component); });
        });
        s.canvas.addEventListener('dragover', function (e) { e.preventDefault(); });
        s.canvas.addEventListener('drop', function (e) { e.preventDefault(); if (s.dragFromPalette) { add(s.dragFromPalette); s.dragFromPalette = null; } });
        if (s.scroll) s.scroll.addEventListener('click', function (e) {
            if (e.target === s.scroll || e.target === s.canvas || e.target === s.empty) showSettings();
        });

        window.addEventListener('media-selected', function (e) {
            var t = window._pendingImageTarget;
            if (t && e.detail) {
                var url = e.detail.url || (document.getElementById('__blockImage__') || {}).value;
                if (url && t.tagName === 'IMG') { t.src = url; t.style.background = 'none'; }
                else if (url && t.classList.contains('img-placeholder')) { t.outerHTML = '<img src="' + esc(url) + '" alt="" style="width:100%;border-radius:var(--radius);display:block;" />'; }
                window._pendingImageTarget = null;
                if (s.selected) showComponent(s.selected);
            }
            var remove = document.getElementById('RemoveFeaturedImageBtn'); if (remove) remove.classList.remove('hidden');
        });

        function extract() {
            var html = '';
            s.canvas.querySelectorAll('.builder-block').forEach(function (block) {
                var content = block.querySelector('.block-content'); if (!content) return;
                var clone = content.cloneNode(true);
                clone.querySelectorAll('[contenteditable]').forEach(function (el) { el.removeAttribute('contenteditable'); });
                clone.querySelectorAll('[onclick]').forEach(function (el) { el.removeAttribute('onclick'); });
                clone.querySelectorAll('.img-placeholder, .comp-spacer span, [data-editor-only]').forEach(function (el) { el.remove(); });
                html += clone.innerHTML.trim() + '\n';
            });
            return html.trim();
        }
        function extractText() {
            var text = '';
            s.canvas.querySelectorAll('.builder-block .block-content').forEach(function (c) { text += c.textContent.trim() + '\n'; });
            return text.trim();
        }

        var recognisers = Object.keys(registry).filter(function (t) { return registry[t].recognise; })
            .sort(function (a, b) { return registry[a].order - registry[b].order; });
        function hydrate() {
            var raw = (s.content && s.content.value || '').trim();
            if (!raw) { updateEmpty(); return; }
            var temp = document.createElement('div'); temp.innerHTML = raw;
            if (temp.children.length === 0) {
                s.canvas.appendChild(create('<p contenteditable="true">' + esc(temp.textContent) + '</p>', 'text'));
            } else {
                Array.prototype.slice.call(temp.children).forEach(function (el) {
                    var type = null;
                    for (var i = 0; i < recognisers.length; i++) if (registry[recognisers[i]].recognise(el)) { type = recognisers[i]; break; }
                    var html;
                    if (type) html = registry[type].prepare(el);
                    else { type = 'text'; el.setAttribute('contenteditable', 'true'); html = el.outerHTML; }   // unknown markup stays editable as text
                    s.canvas.appendChild(create(html, type));
                });
            }
            updateEmpty();
        }

        if (opts.hydrate) hydrate(); else updateEmpty();
        return { extract: extract, extractText: extractText, hydrate: hydrate };
    }

    window.InkwellBuilder = { register: register, init: init, parseVideoUrl: parseVideoUrl, escape: esc };
})();
