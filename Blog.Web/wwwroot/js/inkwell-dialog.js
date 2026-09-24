/*
 * Inkwell admin dialogs — replaces window.confirm / alert / prompt in the Desk.
 *
 * One <dialog id="inkwell-dialog"> lives in _AdminLayout. This script:
 *   • InkwellDialog.confirm({...}) → Promise<boolean>   styled confirmation, optional typed guard
 *   • InkwellDialog.prompt({...})  → Promise<string|null> single-input dialog with live validation
 *   • InkwellDialog.toast(type, message, ms)            success | error | warning | info toast
 *   • declarative forms: any <form data-confirm-title="…"> (or a submit button carrying the same
 *     attributes) is intercepted on submit, confirmed, then submitted for real.
 *
 * Attributes: data-confirm-title, data-confirm-body, data-confirm-action, data-confirm-cancel,
 *             data-confirm-tone (danger | warning | neutral), data-confirm-typed (word to type).
 *
 * Self-hosted, no dependencies. The native <dialog> element gives focus trapping, Escape and a
 * backdrop; this file only adds the content and the promise plumbing.
 */
(function () {
    'use strict';

    const TONES = {
        danger:  { icon: '⚠', ring: 'text-red-600 bg-red-50',        btn: 'bg-red-600 hover:bg-red-700 text-white' },
        warning: { icon: '!', ring: 'text-amber-700 bg-amber-50',    btn: 'bg-amber-600 hover:bg-amber-700 text-white' },
        neutral: { icon: '?', ring: 'text-primary bg-primary/10',    btn: 'bg-primary hover:bg-primary/90 text-primary-foreground' },
    };

    function el(id) { return document.getElementById(id); }

    function dialog() {
        const d = el('inkwell-dialog');
        if (!d) throw new Error('inkwell-dialog element missing from layout');
        return d;
    }

    let active = null; // { resolve, mode }

    function close(result) {
        const d = dialog();
        if (d.open) d.close();
        if (active) { const r = active.resolve; active = null; r(result); }
    }

    function open(opts, mode) {
        const d = dialog();
        const tone = TONES[opts.tone] || TONES.neutral;

        el('ikd-icon').textContent = tone.icon;
        el('ikd-icon').className = 'w-9 h-9 rounded-full flex items-center justify-center text-base font-bold shrink-0 ' + tone.ring;
        el('ikd-title').textContent = opts.title || (mode === 'prompt' ? 'Enter a value' : 'Confirm this action?');
        el('ikd-body').textContent = opts.body || '';
        el('ikd-body').hidden = !opts.body;

        const input = el('ikd-input'), inputWrap = el('ikd-input-wrap'), inputLabel = el('ikd-input-label'), inputHint = el('ikd-input-hint');
        const typedWrap = el('ikd-typed-wrap'), typed = el('ikd-typed'), typedWord = el('ikd-typed-word');
        const ok = el('ikd-ok'), cancel = el('ikd-cancel');

        ok.textContent = opts.action || (mode === 'prompt' ? 'Insert' : 'Confirm');
        ok.className = 'h-9 px-4 rounded-md text-sm font-medium transition-colors disabled:opacity-50 disabled:cursor-not-allowed ' + tone.btn;
        cancel.textContent = opts.cancel || 'Cancel';

        // Prompt input
        inputWrap.hidden = mode !== 'prompt';
        inputHint.textContent = '';
        if (mode === 'prompt') {
            inputLabel.textContent = opts.label || '';
            input.placeholder = opts.placeholder || '';
            input.value = opts.value || '';
        }

        // Typed guard
        const word = opts.typed ? String(opts.typed) : '';
        typedWrap.hidden = !word;
        typed.value = '';
        typedWord.textContent = word;

        function validate() {
            if (mode === 'prompt') {
                const msg = opts.validate ? opts.validate(input.value) : null;
                inputHint.textContent = msg || '';
                inputHint.className = 'text-xs mt-1 min-h-[1rem] ' + (msg ? 'text-red-600' : 'text-emerald-700');
                ok.disabled = !!msg || !input.value.trim();
                if (!msg && input.value.trim() && opts.valid) inputHint.textContent = opts.valid;
            } else if (word) {
                ok.disabled = typed.value.trim() !== word;
            } else {
                ok.disabled = false;
            }
        }
        input.oninput = validate;
        typed.oninput = validate;
        validate();

        active = { resolve: null, mode };
        const promise = new Promise(resolve => { active.resolve = resolve; });

        ok.onclick = () => close(mode === 'prompt' ? input.value.trim() : true);
        cancel.onclick = () => close(mode === 'prompt' ? null : false);
        d.oncancel = (e) => { e.preventDefault(); close(mode === 'prompt' ? null : false); }; // Escape
        d.onclick = (e) => { if (e.target === d) close(mode === 'prompt' ? null : false); }; // backdrop
        el('ikd-form').onsubmit = (e) => { e.preventDefault(); if (!ok.disabled) ok.click(); };

        d.showModal();
        setTimeout(() => { (mode === 'prompt' ? input : (word ? typed : ok)).focus(); }, 0);
        return promise;
    }

    function toast(type, message, ms) {
        const container = el('toast-container');
        if (!container) return;
        const styles = {
            success: 'bg-green-50 border-green-200 text-green-800',
            error:   'bg-red-50 border-red-200 text-red-800',
            warning: 'bg-yellow-50 border-yellow-200 text-yellow-800',
            info:    'bg-blue-50 border-blue-200 text-blue-800',
        };
        const t = document.createElement('div');
        t.className = 'p-4 rounded-lg border flex items-center gap-2 shadow-lg animate-slide-in-right pointer-events-auto ' + (styles[type] || styles.info);
        t.setAttribute('role', type === 'error' ? 'alert' : 'status');
        const span = document.createElement('span');
        span.textContent = message;
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'ml-auto pl-2 opacity-70 hover:opacity-100';
        btn.setAttribute('aria-label', 'Dismiss');
        btn.textContent = '✕';
        btn.onclick = () => t.remove();
        t.append(span, btn);
        container.appendChild(t);
        setTimeout(() => t.remove(), ms || 4000);
    }

    // Declarative forms: confirm on submit, then submit for real.
    document.addEventListener('submit', function (e) {
        const form = e.target;
        if (!(form instanceof HTMLFormElement)) return;
        if (form.dataset.ikdConfirmed === '1') { delete form.dataset.ikdConfirmed; return; }

        const submitter = e.submitter;
        const src = form.hasAttribute('data-confirm-title') ? form
                  : (submitter && submitter.hasAttribute && submitter.hasAttribute('data-confirm-title')) ? submitter
                  : null;
        if (!src) return;

        e.preventDefault();
        const ds = src.dataset;
        InkwellDialog.confirm({
            title: ds.confirmTitle, body: ds.confirmBody, action: ds.confirmAction,
            cancel: ds.confirmCancel, tone: ds.confirmTone || 'danger', typed: ds.confirmTyped,
        }).then(ok => {
            if (!ok) return;
            form.dataset.ikdConfirmed = '1';
            if (typeof form.requestSubmit === 'function') form.requestSubmit(submitter && submitter.form === form ? submitter : undefined);
            else form.submit();
        });
    }, true);

    window.InkwellDialog = {
        confirm: (opts) => open(opts || {}, 'confirm'),
        prompt:  (opts) => open(opts || {}, 'prompt'),
        toast,
    };
})();
