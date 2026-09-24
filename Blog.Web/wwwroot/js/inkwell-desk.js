/*
 * Inkwell Desk behaviours shared by every admin page.
 *
 * 1. Unsaved-changes guard — any <form data-track-changes> is snapshotted on load; once a field,
 *    a contenteditable region or a Quill editor inside it changes, the form is "dirty":
 *      • navigating away or closing the tab asks for confirmation (beforeunload);
 *      • an element with [data-dirty-indicator] inside the form becomes visible ("Unsaved changes").
 *    A real submit disarms the guard. Autosave does not: autosave stores the body, not the form.
 *
 * 2. Busy states — every form POST and every htmx request shows the top progress bar
 *    (#desk-busy) and, for forms, disables the button that submitted after the entry list has
 *    been built (a button disabled synchronously would drop its name/value from the POST).
 *    InkwellDesk.busy(true|false) lets scripts that use fetch() drive the same bar.
 *
 * Self-hosted, no dependencies.
 */
(function () {
    'use strict';

    // ── 1. Unsaved changes ────────────────────────────────────────────────────────────
    const tracked = new Map(); // form → { dirty, baseline }

    function serialize(form) {
        try {
            const fd = new FormData(form);
            const parts = [];
            for (const [k, v] of fd.entries()) if (k !== '__RequestVerificationToken') parts.push(k + '=' + (typeof v === 'string' ? v : '[file]'));
            // contenteditable regions and editors keep their state outside form fields
            form.querySelectorAll('[contenteditable="true"], .ql-editor').forEach(el => parts.push('ce=' + el.innerHTML));
            return parts.join('');
        } catch { return ''; }
    }

    function indicatorsOf(form) { return form.querySelectorAll('[data-dirty-indicator]'); }

    function setDirty(form, dirty) {
        const state = tracked.get(form);
        if (!state || state.dirty === dirty) return;
        state.dirty = dirty;
        indicatorsOf(form).forEach(el => { el.hidden = !dirty; });
        document.body.classList.toggle('desk-has-unsaved', Array.from(tracked.values()).some(s => s.dirty));
    }

    function recheck(form) {
        const state = tracked.get(form);
        if (!state) return;
        setDirty(form, serialize(form) !== state.baseline);
    }

    function track(form) {
        if (tracked.has(form)) return;
        tracked.set(form, { dirty: false, baseline: serialize(form) });
        indicatorsOf(form).forEach(el => { el.hidden = true; });

        let timer = null;
        const schedule = () => { clearTimeout(timer); timer = setTimeout(() => recheck(form), 150); };
        form.addEventListener('input', schedule, true);
        form.addEventListener('change', schedule, true);
        // Quill fires its own event; the editor root is inside the form on every Inkwell editor page.
        document.addEventListener('inkwell:content-changed', schedule);

        form.addEventListener('submit', () => { setDirty(form, false); tracked.get(form).submitted = true; }, true);
    }

    window.addEventListener('beforeunload', function (e) {
        const dirty = Array.from(tracked.entries()).some(([, s]) => s.dirty && !s.submitted);
        if (!dirty) return;
        e.preventDefault();
        e.returnValue = ''; // browsers show their own generic wording; custom text is ignored
    });

    // If a page is restored from bfcache after a cancelled navigation, re-arm cleanly.
    window.addEventListener('pageshow', function (e) {
        if (e.persisted) tracked.forEach(s => { s.submitted = false; });
        busy(false);
    });

    // ── 2. Busy states ────────────────────────────────────────────────────────────────
    let busyCount = 0;
    function busy(on) {
        busyCount = Math.max(0, busyCount + (on ? 1 : -1));
        const bar = document.getElementById('desk-busy');
        if (bar) bar.classList.toggle('is-active', busyCount > 0);
        document.body.setAttribute('aria-busy', busyCount > 0 ? 'true' : 'false');
    }

    document.addEventListener('submit', function (e) {
        const form = e.target;
        if (!(form instanceof HTMLFormElement)) return;
        if (e.defaultPrevented) return;                 // a dialog or validator stopped it
        if (form.hasAttribute('hx-post') || form.hasAttribute('hx-get')) return; // htmx handles below
        if (form.method && form.method.toLowerCase() === 'get') return;         // searches are instant
        if (form.target && form.target !== '_self') return;

        busy(true);
        const btn = e.submitter;
        // Defer: the form's entry list is built synchronously right after this event, and a
        // disabled submitter would be left out of it (losing e.g. submitAction=publish).
        setTimeout(() => {
            if (btn && !btn.disabled) {
                btn.disabled = true;
                btn.setAttribute('aria-busy', 'true');
                if (!btn.querySelector('.desk-spinner')) {
                    const s = document.createElement('span');
                    s.className = 'desk-spinner';
                    s.setAttribute('aria-hidden', 'true');
                    btn.prepend(s);
                }
            }
        }, 0);
    });

    document.addEventListener('htmx:beforeRequest', () => busy(true));
    document.addEventListener('htmx:afterRequest', () => busy(false));
    document.addEventListener('htmx:sendError', () => busy(false));

    // ── boot ──────────────────────────────────────────────────────────────────────────
    function boot() { document.querySelectorAll('form[data-track-changes]').forEach(track); }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot); else boot();
    // Editors may build their form contents after load (Quill, page builder); re-baseline once they are ready.
    window.addEventListener('load', () => setTimeout(() => tracked.forEach((s, f) => { s.baseline = serialize(f); setDirty(f, false); }), 300));

    window.InkwellDesk = { busy, track, isDirty: () => Array.from(tracked.values()).some(s => s.dirty) };
})();

/*
 * 3. Mobile navigation — the sidebar is always in the DOM and merely slid off-screen under 768 px.
 *    [data-nav-open] slides it in (and focuses its first link), [data-nav-close] and Escape slide it
 *    out, the overlay closes it, and aria-expanded on the opener tracks the state.
 */
(function () {
    'use strict';
    const sidebar = document.getElementById('sidebar');
    const overlay = document.getElementById('sidebar-overlay');
    if (!sidebar) return;
    const openers = Array.from(document.querySelectorAll('[data-nav-open]'));
    let lastOpener = null;

    function setOpen(open) {
        sidebar.classList.toggle('-translate-x-full', !open);
        sidebar.classList.toggle('translate-x-0', open);
        if (overlay) overlay.classList.toggle('hidden', !open);
        openers.forEach(b => b.setAttribute('aria-expanded', open ? 'true' : 'false'));
        if (open) {
            const first = sidebar.querySelector('a[href], button');
            if (first) first.focus();
        } else if (lastOpener) {
            lastOpener.focus();
        }
    }
    openers.forEach(b => b.addEventListener('click', e => { lastOpener = e.currentTarget; setOpen(true); }));
    document.querySelectorAll('[data-nav-close]').forEach(el => el.addEventListener('click', () => setOpen(false)));
    document.addEventListener('keydown', e => {
        if (e.key === 'Escape' && sidebar.classList.contains('translate-x-0') && window.innerWidth < 768) setOpen(false);
    });
    window.InkwellDesk = Object.assign(window.InkwellDesk || {}, { navOpen: () => setOpen(true), navClose: () => setOpen(false) });
})();
