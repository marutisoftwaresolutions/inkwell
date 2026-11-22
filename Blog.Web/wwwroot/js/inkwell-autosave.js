/*
 * Inkwell draft recovery: keeps a copy of unsaved editor work in this browser, so a crashed tab, a
 * lost connection or an expired sign-in does not cost the writer their text.
 *
 * It is deliberately NOT a save. Nothing is sent to the server and nothing a reader sees changes: a
 * published post is only ever changed by the writer pressing a button. The unsaved-changes guard is
 * left armed (UI/UX rule 3), and the status line says where the copy is ("kept in this browser").
 *
 * A form opts in with:
 *   <form data-local-backup="post:{id}" data-saved-at="{server UpdatedAt, ms since 1970}">
 * and may contain an element with [data-backup-status] for the status line.
 *
 * Every 15 seconds, while the form has unsaved changes, the title, the body (rich text, block builder
 * or Markdown) and the meta description are stored under that key. When the editor is opened again and a
 * copy newer than the server's version exists, a bar offers to restore or discard it. A save that
 * goes through removes the copy. Storage problems (private window, quota) are ignored: the editor
 * works exactly as it did without this file.
 */
(function () {
    'use strict';

    var INTERVAL_MS = 15000;
    var MAX_CHARS = 2000000;      // about 2 MB of text: larger bodies are not kept
    var MAX_AGE_MS = 14 * 24 * 3600 * 1000;
    var PREFIX = 'inkwell-backup:';

    function store() { try { return window.localStorage; } catch (e) { return null; } }

    function readEditor(form) {
        var title = form.querySelector('[name="Title"]');
        var meta = form.querySelector('[name="MetaDescription"]');
        var data = { title: title ? title.value : '', meta: meta ? meta.value : '' };
        if (window.InkwellMarkdown) data.markdown = window.InkwellMarkdown.get();
        else if (window.InkwellQuill) data.rich = window.InkwellQuill.get();
        else if (window.InkwellBuilderInstance) data.html = window.InkwellBuilderInstance.extract();
        return data;
    }

    function writeEditor(form, data) {
        var title = form.querySelector('[name="Title"]');
        var meta = form.querySelector('[name="MetaDescription"]');
        if (title && typeof data.title === 'string') title.value = data.title;
        if (meta && typeof data.meta === 'string') meta.value = data.meta;
        if (typeof data.markdown === 'string' && window.InkwellMarkdown) window.InkwellMarkdown.set(data.markdown);
        else if (typeof data.rich === 'string' && window.InkwellQuill) window.InkwellQuill.set(data.rich);
        else if (typeof data.html === 'string' && window.InkwellBuilderInstance) window.InkwellBuilderInstance.load(data.html);
        // Tell the unsaved-changes guard: restored text is unsaved text.
        document.dispatchEvent(new CustomEvent('inkwell:content-changed'));
        if (title) title.dispatchEvent(new Event('input', { bubbles: true }));
    }

    function clock(ms) {
        try { return new Date(ms).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }); } catch (e) { return ''; }
    }
    function dayAndClock(ms) {
        try { return new Date(ms).toLocaleString([], { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }); } catch (e) { return ''; }
    }

    function prune(ls) {
        try {
            var now = Date.now();
            for (var i = ls.length - 1; i >= 0; i--) {
                var k = ls.key(i);
                if (!k || k.indexOf(PREFIX) !== 0) continue;
                var v = JSON.parse(ls.getItem(k) || 'null');
                if (!v || !v.t || now - v.t > MAX_AGE_MS) ls.removeItem(k);
            }
        } catch (e) { /* ignore */ }
    }

    function offer(form, key, saved, ls) {
        var bar = document.createElement('div');
        bar.setAttribute('role', 'status');
        bar.className = 'rounded-lg border p-3 pill-warn text-sm flex flex-wrap items-center gap-3 m-3';
        var text = document.createElement('span');
        text.textContent = 'Unsaved changes from ' + dayAndClock(saved.t) + ' were kept in this browser.';
        var restore = document.createElement('button');
        restore.type = 'button'; restore.className = 'desk-btn desk-btn-primary desk-btn-sm'; restore.textContent = 'Restore them';
        var discard = document.createElement('button');
        discard.type = 'button'; discard.className = 'desk-btn desk-btn-outline desk-btn-sm'; discard.textContent = 'Discard the copy';
        bar.appendChild(text); bar.appendChild(restore); bar.appendChild(discard);
        form.insertBefore(bar, form.firstChild);

        restore.addEventListener('click', function () {
            try { writeEditor(form, saved.data); } catch (e) { /* leave the editor as it is */ }
            bar.remove();
            if (window.InkwellDialog) InkwellDialog.toast('info', 'Restored. Nothing is saved until you save or publish.');
        });
        discard.addEventListener('click', function () {
            try { ls.removeItem(key); } catch (e) { /* ignore */ }
            bar.remove();
        });
    }

    function init(form) {
        var ls = store();
        if (!ls) return;
        var key = PREFIX + form.getAttribute('data-local-backup');
        var serverSavedAt = parseInt(form.getAttribute('data-saved-at') || '0', 10) || 0;
        var status = form.querySelector('[data-backup-status]') || document.querySelector('[data-backup-status]');

        prune(ls);

        var saved = null;
        try { saved = JSON.parse(ls.getItem(key) || 'null'); } catch (e) { saved = null; }
        // A copy older than what the server holds was already saved (here or in another tab): drop it.
        if (saved && saved.t && saved.data && saved.t > serverSavedAt) {
            // Wait for the editor scripts further down the page to finish setting up before offering.
            window.addEventListener('load', function () { offer(form, key, saved, ls); });
        } else if (saved) {
            try { ls.removeItem(key); } catch (e) { /* ignore */ }
        }

        var last = '';
        setInterval(function () {
            if (!document.body.classList.contains('desk-has-unsaved')) return;
            try {
                var data = readEditor(form);
                var body = JSON.stringify(data);
                if (body === last || body.length > MAX_CHARS) return;
                var now = Date.now();
                ls.setItem(key, JSON.stringify({ t: now, data: data }));
                last = body;
                if (status) status.textContent = 'Copy kept in this browser at ' + clock(now) + ' (not saved)';
            } catch (e) { /* quota or private mode: the editor carries on without a copy */ }
        }, INTERVAL_MS);

        // Only a submit that really goes through removes the copy (a cancelled confirmation does not).
        form.addEventListener('submit', function (e) {
            setTimeout(function () {
                if (e.defaultPrevented) return;
                try { ls.removeItem(key); } catch (err) { /* ignore */ }
            }, 0);
        }, true);
    }

    function start() { document.querySelectorAll('form[data-local-backup]').forEach(init); }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();

    window.InkwellAutosave = { prefix: PREFIX, interval: INTERVAL_MS };
})();
