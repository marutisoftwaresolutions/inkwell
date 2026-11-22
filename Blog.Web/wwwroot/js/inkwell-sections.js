/*
 * Inkwell section blocks: the website-shaped blocks for pages (columns, feature grid, call to action,
 * testimonials, pricing, questions, contact, logo strip). Each is one InkwellBuilder.register call on
 * the shared builder (inkwell-builder.js), so they drag, duplicate, move and save like every other
 * block. Loaded by the page editors only, after inkwell-builder.js.
 *
 * Rules these blocks keep:
 *   - Saved markup is plain HTML with sec-* classes and no inline colours; all colour comes from theme
 *     tokens in inkwell-sections.css, so a section follows every layout, preset and the dark scheme.
 *   - Placeholder copy says it is a placeholder. Testimonials and logos start empty of claims: the
 *     site owner writes them, nothing is invented here.
 *   - A section works without JavaScript on the reader's side: it is headings, paragraphs, lists,
 *     links and images.
 */
(function () {
    'use strict';
    if (!window.InkwellBuilder) return;

    var esc = InkwellBuilder.escape;
    var MAX_ITEMS = 8;
    var TEXT = 'h2,h3,p,li,blockquote,strong,span.sec-role,a.sec-btn';

    function content() { return document.querySelector('.builder-block.selected .block-content'); }
    function refresh() { var b = document.querySelector('.builder-block.selected'); if (b && window.selectBlock) window.selectBlock(b); }
    function makeEditable(root) { root.querySelectorAll(TEXT).forEach(function (x) { x.setAttribute('contenteditable', 'true'); }); }
    function wireImages(root) {
        root.querySelectorAll('img').forEach(function (img) { img.setAttribute('onclick', 'selectBlockImage(this)'); });
    }
    function prepared(el) { makeEditable(el); wireImages(el); return el.outerHTML; }
    function note(text) { return '<p class="text-xs text-muted-foreground">' + text + '</p>'; }

    // ── Settings fragments shared by the blocks ─────────────────────────────────

    function itemButtons(selector, noun) {
        var c = content(), count = c ? c.querySelectorAll(selector).length : 0;
        return '<div class="setting-field"><label>' + esc(noun) + 's: ' + count + '</label><div style="display:flex;gap:6px;">' +
            '<button type="button" class="desk-btn desk-btn-outline desk-btn-sm" onclick="sectionAddItem(\'' + selector + '\')"' + (count >= MAX_ITEMS ? ' disabled' : '') + '>Add ' + esc(noun) + '</button>' +
            '<button type="button" class="desk-btn desk-btn-outline desk-btn-sm" onclick="sectionRemoveItem(\'' + selector + '\')"' + (count <= 1 ? ' disabled' : '') + '>Remove last</button>' +
            '</div></div>';
    }
    function columnSelect(options) {
        var c = content(), root = c && c.querySelector('[data-columns]'), current = (root && root.getAttribute('data-columns')) || '3';
        return '<div class="setting-field"><label>Columns</label><select onchange="sectionColumns(this.value)">' +
            options.map(function (n) { return '<option value="' + n + '"' + (String(n) === current ? ' selected' : '') + '>' + n + '</option>'; }).join('') +
            '</select></div>';
    }
    function linkFields() {
        var c = content(), links = c ? c.querySelectorAll('a.sec-btn') : [], html = '';
        Array.prototype.forEach.call(links, function (a, i) {
            html += '<div class="setting-field"><label>Button ' + (links.length > 1 ? (i + 1) + ' ' : '') + 'address</label>' +
                '<input type="text" value="' + esc(a.getAttribute('href') || '') + '" oninput="sectionLink(' + i + ', this.value)" placeholder="/contact or https://…" /></div>';
        });
        return html;
    }
    function altFields() {
        var c = content(), imgs = c ? c.querySelectorAll('img') : [], html = '';
        Array.prototype.forEach.call(imgs, function (img, i) {
            html += '<div class="setting-field"><label>Logo ' + (i + 1) + ' name (read aloud to screen-reader users)</label>' +
                '<input type="text" value="' + esc(img.getAttribute('alt') || '') + '" oninput="sectionAlt(' + i + ', this.value)" placeholder="Company name" /></div>';
        });
        return html;
    }

    window.sectionAddItem = function (selector) {
        var c = content(); if (!c) return;
        var items = c.querySelectorAll(selector); if (!items.length || items.length >= MAX_ITEMS) return;
        var last = items[items.length - 1], copy = last.cloneNode(true);
        if (copy.tagName === 'IMG') { copy.setAttribute('src', ''); copy.setAttribute('alt', ''); }
        last.parentNode.insertBefore(copy, last.nextSibling);
        var grid = c.querySelector('.sec-columns'); if (grid) grid.setAttribute('data-columns', String(Math.min(4, items.length + 1)));
        refresh();
    };
    window.sectionRemoveItem = function (selector) {
        var c = content(); if (!c) return;
        var items = c.querySelectorAll(selector); if (items.length <= 1) return;
        items[items.length - 1].remove();
        var grid = c.querySelector('.sec-columns'); if (grid) grid.setAttribute('data-columns', String(Math.max(2, Math.min(4, items.length - 1))));
        refresh();
    };
    window.sectionColumns = function (n) {
        var c = content(), root = c && c.querySelector('[data-columns]'); if (!root) return;
        root.setAttribute('data-columns', String(n));
        // The plain columns block has exactly as many columns as it shows.
        if (root.classList.contains('sec-columns')) {
            var cols = root.querySelectorAll('.sec-col'), want = parseInt(n, 10);
            for (var i = cols.length; i < want; i++) root.appendChild(cols[cols.length - 1].cloneNode(true));
            for (var j = cols.length; j > want; j--) root.querySelectorAll('.sec-col')[j - 1].remove();
        }
        refresh();
    };
    window.sectionLink = function (index, value) {
        var c = content(), a = c && c.querySelectorAll('a.sec-btn')[index]; if (a) a.setAttribute('href', value || '#');
    };
    window.sectionAlt = function (index, value) {
        var c = content(), img = c && c.querySelectorAll('img')[index]; if (img) img.setAttribute('alt', value);
    };
    window.sectionToggleForm = function (key) { if (window.changeBlockForm) window.changeBlockForm(key); };

    function byClass(name) { return function (el) { return el.classList.contains(name); }; }
    function e(tag, text, cls) { return '<' + tag + (cls ? ' class="' + cls + '"' : '') + ' contenteditable="true">' + text + '</' + tag.split(' ')[0] + '>'; }

    // ── The blocks ──────────────────────────────────────────────────────────────

    InkwellBuilder.register('columns', {
        title: 'Columns', order: 15,
        template: function () {
            var col = '<div class="sec-col">' + e('h3', 'Column heading') + e('p', 'Placeholder text. Replace it with your own.') + '</div>';
            return '<div class="sec-columns" data-columns="2">' + col + col + '</div>';
        },
        settings: function () { return columnSelect([2, 3, 4]) + note('Click any text on the canvas to edit it. Columns stack on a phone.'); },
        recognise: byClass('sec-columns'), prepare: prepared
    });

    InkwellBuilder.register('features', {
        title: 'Feature grid', order: 15,
        template: function () {
            var item = '<div class="sec-feature">' + e('h3', 'Feature name') + e('p', 'Placeholder: say what this does for the reader, in one or two sentences.') + '</div>';
            return '<section class="sec-section sec-features" data-columns="3">' + e('h2', 'What you get') + e('p', 'Placeholder introduction. Replace or delete it.', 'sec-lede') +
                '<div class="sec-grid">' + item + item + item + '</div></section>';
        },
        settings: function () { return columnSelect([2, 3, 4]) + itemButtons('.sec-feature', 'feature'); },
        recognise: byClass('sec-features'), prepare: prepared
    });

    InkwellBuilder.register('cta', {
        title: 'Call to action', order: 15,
        template: function () {
            return '<section class="sec-section sec-cta">' + e('h2', 'A clear next step') + e('p', 'Placeholder: one sentence on why to take it.') +
                '<p class="sec-actions"><a class="sec-btn" href="#" contenteditable="true">Button label</a></p></section>';
        },
        settings: function () { return linkFields() + note('Click the button on the canvas to change its label.'); },
        recognise: byClass('sec-cta'), prepare: prepared
    });

    InkwellBuilder.register('testimonials', {
        title: 'Testimonials', order: 15,
        template: function () {
            var item = '<figure class="sec-testimonial">' + e('blockquote', 'Placeholder. Paste a real quote from a real customer here, with their permission.') +
                '<figcaption>' + e('strong', 'Their name') + ' <span class="sec-role" contenteditable="true">Role, company</span></figcaption></figure>';
            return '<section class="sec-section sec-testimonials" data-columns="2">' + e('h2', 'What people say') + '<div class="sec-grid">' + item + item + '</div></section>';
        },
        settings: function () {
            return columnSelect([1, 2, 3]) + itemButtons('.sec-testimonial', 'testimonial') +
                note('Use only quotes people really gave you. Delete a placeholder you cannot replace: an invented testimonial misleads readers.');
        },
        recognise: byClass('sec-testimonials'), prepare: prepared
    });

    InkwellBuilder.register('pricing', {
        title: 'Pricing table', order: 15,
        template: function () {
            var plan = '<div class="sec-plan">' + e('h3', 'Plan name') + e('p', 'Price', 'sec-price') + e('p', 'Placeholder: who this plan is for.') +
                '<ul>' + e('li', 'What is included') + e('li', 'What is included') + e('li', 'What is included') + '</ul>' +
                '<p class="sec-actions"><a class="sec-btn" href="#" contenteditable="true">Choose plan</a></p></div>';
            return '<section class="sec-section sec-pricing" data-columns="3">' + e('h2', 'Pricing') + '<div class="sec-grid">' + plan + plan + plan + '</div></section>';
        },
        settings: function () { return columnSelect([2, 3, 4]) + itemButtons('.sec-plan', 'plan') + linkFields() + note('Press Enter inside a list to add a line.'); },
        recognise: byClass('sec-pricing'), prepare: prepared
    });

    InkwellBuilder.register('faqsection', {
        title: 'Questions and answers', order: 15,
        template: function () {
            var item = '<div class="sec-faq-item">' + e('h3', 'A question readers ask?') + e('p', 'Placeholder answer. Keep it to a few sentences.') + '</div>';
            return '<section class="sec-section sec-faq">' + e('h2', 'Questions and answers') + item + item + item + '</section>';
        },
        settings: function () { return itemButtons('.sec-faq-item', 'question'); },
        recognise: byClass('sec-faq'), prepare: prepared
    });

    InkwellBuilder.register('contact', {
        title: 'Contact section', order: 12,   // before the plain Form block: this section contains one
        template: function () {
            var head = e('h2', 'Get in touch') + e('p', 'Placeholder: say when people can expect a reply.', 'sec-lede');
            return fetch('/admin/forms/options', { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
                .then(function (r) { return r.ok ? r.json() : []; })
                .catch(function () { return []; })
                .then(function (list) {
                    var form = (list || []).filter(function (f) { return f.active; })[0] || (list || [])[0];
                    if (!form && window.InkwellDialog) InkwellDialog.toast('info', 'No form exists yet, so the section has none. Create one in Forms, then add the Form block inside the page.');
                    var slot = form ? '<div class="comp-form" data-form="' + esc(form.key) + '" data-form-name="' + esc(form.name) + '"></div>' : '';
                    return '<section class="sec-section sec-contact">' + head + slot + '</section>';
                });
        },
        settings: function (b) {
            var has = b.querySelector('.comp-form');
            return note(has ? 'The form shown here is "' + esc(has.getAttribute('data-form-name') || '') + '". Its fields and messages are set in <a class="underline" href="/admin/forms">Forms</a>. Readers see the real form.'
                            : 'This section has no form. Create one in <a class="underline" href="/admin/forms">Forms</a>, delete this block and add it again.');
        },
        recognise: byClass('sec-contact'), prepare: prepared
    });

    InkwellBuilder.register('logos', {
        title: 'Logo strip', order: 15,
        template: function () {
            var img = '<img src="" alt="" onclick="selectBlockImage(this)" />';
            return '<section class="sec-section sec-logos">' + e('p', 'Placeholder heading, for example: Our partners', 'sec-eyebrow') +
                '<div class="sec-logo-row">' + img + img + img + img + '</div></section>';
        },
        settings: function () {
            return itemButtons('.sec-logo-row img', 'logo') + altFields() +
                note('Click a logo on the canvas to choose its image. Show only organisations you really work with, and give each logo its name.');
        },
        recognise: byClass('sec-logos'), prepare: prepared
    });
})();
