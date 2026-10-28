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

        // Deferred: a confirmation dialog (declarative data-confirm-* or a page's own handler) may cancel
        // this submit after this capture listener runs. Disarming the guard on a cancelled submit would
        // let the page be left without a prompt, so only a submit that actually proceeds counts.
        form.addEventListener('submit', e => setTimeout(() => {
            if (e.defaultPrevented) return;
            setDirty(form, false);
            tracked.get(form).submitted = true;
        }, 0), true);
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
    // Errors are placed at the field (UI rule 12): after a failed save, bring the first one into view and
    // focus it, opening any collapsed section it sits in, so the author never has to hunt for it.
    function revealFirstError() {
        const el = document.querySelector('form[data-track-changes] [aria-invalid="true"]');
        if (!el) return;
        for (let d = el.closest('details'); d; d = d.parentElement && d.parentElement.closest('details')) d.open = true;
        if (el.offsetParent === null) return;   // inside a hidden tab: the page's own tab logic reveals it
        el.scrollIntoView({ block: 'center' });
        el.focus({ preventScroll: true });
    }

    // Validation toasts repeat the messages already shown at the fields, and stay until dismissed so a failed save
    // is never missed. Once the person starts correcting the form they have seen them, and a toast left in the
    // bottom-right corner would sit over the Save button they need next, so they go; the field messages stay.
    // Only a person's own typing counts (editors and scripts dispatch synthetic input events), and only in the form
    // that has the errors, so a search box elsewhere on the page does not dismiss them.
    document.addEventListener('input', (e) => {
        if (!e.isTrusted || !e.target.closest) return;
        const form = e.target.closest('form');
        if (!form || !form.querySelector('[aria-invalid="true"], .input-validation-error')) return;
        document.querySelectorAll('.toast-model-error').forEach(t => t.remove());
    }, true);

    function boot() { document.querySelectorAll('form[data-track-changes]').forEach(track); revealFirstError(); }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot); else boot();
    // Editors may build their form contents after load (Quill, page builder); re-baseline once they are ready.
    window.addEventListener('load', () => setTimeout(() => tracked.forEach((s, f) => { s.baseline = serialize(f); setDirty(f, false); }), 300));

    // markClean: the page has already asked about the unsaved work (e.g. "Discard and switch") and is about to
    // leave on purpose, so the browser's own leave prompt would be a second, redundant question.
    const markClean = () => tracked.forEach((s, form) => setDirty(form, false));
    // submit(form): a page's own save path (Publish, Save draft) calls form.submit(), which fires no submit event, so
    // the guard still counted the page as dirty and the browser asked "Leave site?" in the middle of the save; pressing
    // Cancel there cancelled the save. The page is being saved, so it is clean by definition.
    const submit = (form) => { markClean(); form.submit(); };

    // leave(href): leaving a dirty page by one of the Desk's own links asks with the Desk's dialog, not the browser's.
    // (Closing the tab, refreshing and the Back button can only ever get the browser's prompt.)
    const isDirty = () => Array.from(tracked.values()).some(s => s.dirty && !s.submitted);
    const leave = (href) => {
        if (!isDirty() || !window.InkwellDialog) { window.location.href = href; return; }
        InkwellDialog.confirm({
            title: 'Leave without saving?',
            body: 'The changes on this page are not saved and are lost if you leave. Save first to keep them.',
            action: 'Leave page', cancel: 'Keep editing', tone: 'danger',
        }).then(ok => { if (ok) { markClean(); window.location.href = href; } });
    };
    document.addEventListener('click', (e) => {
        if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        const a = e.target.closest && e.target.closest('a[href]');
        if (!a || a.target === '_blank' || a.hasAttribute('download') || a.hasAttribute('data-editor-switch')) return;
        if (a.closest('form') && a.closest('form').matches('[data-track-changes]') && a.getAttribute('href').startsWith('#')) return;
        const href = a.getAttribute('href');
        if (!href || href.startsWith('#') || href.startsWith('javascript:')) return;
        if (a.origin !== window.location.origin) return;
        if (!isDirty()) return;
        e.preventDefault();
        leave(a.href);
    }, true);

    window.InkwellDesk = { busy, track, isDirty, markClean, submit, leave };
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

/*
 * 4. Shell — sidebar groups, the collapsed rail, the account menu, toast dismissal and the
 *    command palette (Ctrl/Cmd K). All state is per-browser (localStorage) and every piece
 *    degrades: without script the groups are open, the rail is expanded and the links work.
 */
(function () {
    'use strict';
    const store = {
        get(k) { try { return localStorage.getItem(k); } catch (e) { return null; } },
        set(k, v) { try { localStorage.setItem(k, v); } catch (e) { /* private mode: nothing to remember with */ } },
    };

    // Toasts: one delegated handler instead of inline onclick attributes.
    document.addEventListener('click', e => {
        const btn = e.target.closest && e.target.closest('[data-toast-dismiss]');
        if (btn) { const t = btn.closest('.desk-toast'); if (t) t.remove(); }
    });

    // Sidebar groups: collapsible, remembered per group; the group holding the current page never starts collapsed.
    document.querySelectorAll('.desk-nav-group[data-nav-group]').forEach(group => {
        const name = group.getAttribute('data-nav-group');
        const toggle = group.querySelector('[data-nav-group-toggle]');
        if (!toggle) return;
        const hasCurrent = !!group.querySelector('[aria-current="page"]');
        const set = (collapsed) => {
            group.setAttribute('data-collapsed', collapsed ? 'true' : 'false');
            toggle.setAttribute('aria-expanded', collapsed ? 'false' : 'true');
        };
        set(!hasCurrent && store.get('desk.nav.' + name) === '0');
        toggle.addEventListener('click', () => {
            const collapsed = group.getAttribute('data-collapsed') !== 'true';
            set(collapsed);
            store.set('desk.nav.' + name, collapsed ? '0' : '1');
        });
    });

    // Rail: icons only at >= md. The attribute is applied before first paint by an inline script in the layout.
    const rail = document.querySelector('[data-rail-toggle]');
    if (rail) {
        const sync = () => {
            const on = document.documentElement.getAttribute('data-desk-rail') === 'true';
            rail.setAttribute('aria-pressed', on ? 'true' : 'false');
            rail.setAttribute('aria-label', on ? 'Expand sidebar' : 'Collapse sidebar');
        };
        sync();
        rail.addEventListener('click', () => {
            const on = document.documentElement.getAttribute('data-desk-rail') !== 'true';
            if (on) document.documentElement.setAttribute('data-desk-rail', 'true'); else document.documentElement.removeAttribute('data-desk-rail');
            store.set('desk.rail', on ? '1' : '0');
            sync();
        });
    }

    // <details> menus close on outside click and Escape, and focus returns to the summary.
    document.addEventListener('click', e => {
        document.querySelectorAll('details.desk-menu[open]').forEach(d => { if (!d.contains(e.target)) d.removeAttribute('open'); });
    });
    document.addEventListener('keydown', e => {
        if (e.key !== 'Escape') return;
        document.querySelectorAll('details.desk-menu[open]').forEach(d => { d.removeAttribute('open'); const s = d.querySelector('summary'); if (s) s.focus(); });
    });

    // Command palette.
    const dlg = document.getElementById('desk-cmdk');
    const input = document.getElementById('desk-cmdk-input');
    const list = document.getElementById('desk-cmdk-list');
    const empty = document.getElementById('desk-cmdk-empty');
    const dataEl = document.getElementById('desk-cmdk-data');
    if (!dlg || !input || !list || !dataEl) return;
    let data = { items: [], commands: [], search: '/admin/posts?search=' };
    try { data = JSON.parse(dataEl.textContent); } catch (e) { /* palette stays empty */ }
    const all = [].concat(data.commands || [], data.items || []);
    const searchIcon = '<circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>';
    let results = [], selected = 0, lastTrigger = null;

    function score(entry, q) {
        if (!q) return 1;
        const hay = (entry.label + ' ' + entry.group + ' ' + (entry.keywords || '')).toLowerCase();
        if (entry.label.toLowerCase().startsWith(q)) return 3;
        if (hay.includes(q)) return 2;
        let i = 0; for (const ch of hay) { if (ch === q[i]) i++; if (i === q.length) return 1; } // subsequence ("cmt" finds Comments)
        return 0;
    }
    function render() {
        const raw = input.value.trim(), q = raw.toLowerCase();
        results = all.map(e => ({ e, s: score(e, q) })).filter(x => x.s > 0).sort((a, b) => b.s - a.s).map(x => x.e).slice(0, 12);
        if (q) results.push({ group: 'Search', label: 'Search posts for “' + raw + '”', href: data.search + encodeURIComponent(raw), icon: searchIcon });
        selected = 0;
        list.innerHTML = '';
        let lastGroup = null;
        results.forEach((r, i) => {
            if (r.group !== lastGroup) {
                const h = document.createElement('div');
                h.className = 'px-3 pt-2 pb-1 text-[11px] font-semibold uppercase tracking-wider text-muted-foreground';
                h.textContent = r.group; list.appendChild(h); lastGroup = r.group;
            }
            const a = document.createElement('a');
            a.href = r.href; a.className = 'desk-cmdk-item'; a.setAttribute('role', 'option'); a.id = 'desk-cmdk-opt-' + i;
            a.setAttribute('aria-selected', i === selected ? 'true' : 'false');
            const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
            svg.setAttribute('width', '16'); svg.setAttribute('height', '16'); svg.setAttribute('viewBox', '0 0 24 24'); svg.setAttribute('fill', 'none');
            svg.setAttribute('stroke', 'currentColor'); svg.setAttribute('stroke-width', '2'); svg.setAttribute('aria-hidden', 'true'); svg.classList.add('text-muted-foreground', 'shrink-0');
            svg.innerHTML = r.icon || '';
            const span = document.createElement('span'); span.textContent = r.label; span.className = 'truncate';
            a.append(svg, span);
            a.addEventListener('mousemove', () => select(i));
            list.appendChild(a);
        });
        empty.hidden = results.length > 0;
        input.setAttribute('aria-activedescendant', results.length ? 'desk-cmdk-opt-0' : '');
    }
    function select(i) {
        if (!results.length) return;
        selected = (i + results.length) % results.length;
        list.querySelectorAll('[role="option"]').forEach((el, j) => el.setAttribute('aria-selected', j === selected ? 'true' : 'false'));
        const el = document.getElementById('desk-cmdk-opt-' + selected);
        if (el) { el.scrollIntoView({ block: 'nearest' }); input.setAttribute('aria-activedescendant', el.id); }
    }
    function open(trigger) {
        lastTrigger = trigger || document.activeElement;
        input.value = ''; render();
        if (!dlg.open) dlg.showModal();
        input.focus();
    }
    function close() { if (dlg.open) dlg.close(); if (lastTrigger && lastTrigger.focus) lastTrigger.focus(); }

    document.querySelectorAll('[data-cmdk-open]').forEach(b => b.addEventListener('click', e => open(e.currentTarget)));
    document.addEventListener('keydown', e => {
        if ((e.ctrlKey || e.metaKey) && !e.shiftKey && !e.altKey && e.key.toLowerCase() === 'k') { e.preventDefault(); if (dlg.open) close(); else open(null); }
    });
    input.addEventListener('input', render);
    input.addEventListener('keydown', e => {
        if (e.key === 'ArrowDown') { e.preventDefault(); select(selected + 1); }
        else if (e.key === 'ArrowUp') { e.preventDefault(); select(selected - 1); }
        else if (e.key === 'Enter') { e.preventDefault(); const r = results[selected]; if (r) InkwellDesk.leave(r.href); }
    });
    dlg.addEventListener('cancel', e => { e.preventDefault(); close(); });
    dlg.addEventListener('click', e => { if (e.target === dlg) close(); });
    window.InkwellDesk = Object.assign(window.InkwellDesk || {}, { paletteOpen: () => open(null), paletteClose: close });
})();
