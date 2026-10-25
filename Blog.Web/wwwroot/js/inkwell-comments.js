/*
 * Comments on a cacheable post page (ADR-0031). The page carries empty token slots so every reader gets the same
 * copy; this script fetches the anti-forgery and timing tokens from /{slug}/comment-token once, when the comments
 * come near the screen or a comment field gets focus, then sends a comment in place and shows the outcome in the
 * form's status region. Without this script the form posts normally and the respond page keeps the reader's draft.
 */
(function () {
    'use strict';
    var section = document.querySelector('[data-comment-token-url]');
    if (!section || !window.fetch || !window.FormData) return;
    var url = section.getAttribute('data-comment-token-url');
    var pending = null;

    function tokens(force) {
        if (pending && !force) return pending;
        pending = fetch(url, { credentials: 'same-origin', headers: { 'Accept': 'application/json' }, cache: 'no-store' })
            .then(function (r) { if (!r.ok) throw new Error('token ' + r.status); return r.json(); })
            .then(function (t) {
                document.querySelectorAll('[data-comment-request-token]').forEach(function (el) { el.value = t.request; });
                document.querySelectorAll('[data-comment-form-token]').forEach(function (el) { el.value = t.form; });
                return t;
            })
            .catch(function (e) { pending = null; throw e; });
        return pending;
    }

    if ('IntersectionObserver' in window) {
        var seen = new IntersectionObserver(function (entries) {
            if (entries.some(function (e) { return e.isIntersecting; })) { seen.disconnect(); tokens().catch(function () { }); }
        }, { rootMargin: '600px 0px' });
        seen.observe(section);
    }
    section.addEventListener('focusin', function (e) { if (e.target.closest('[data-comment-form]')) tokens().catch(function () { }); });

    function statusOf(form) {
        var id = form.getAttribute('aria-describedby');
        return id ? document.getElementById(id) : null;
    }
    function show(form, text, ok) {
        var box = statusOf(form);
        if (!box) return;
        box.textContent = text;
        box.hidden = !text;
        box.setAttribute('role', ok ? 'status' : 'alert');
        box.setAttribute('tabindex', '-1');
        if (text) box.focus();
    }

    window.addEventListener('pageshow', function (e) {
        if (!e.persisted) return;
        document.querySelectorAll('form[data-comment-form]').forEach(function (form) {
            form.removeAttribute('aria-busy');
            var b = form.querySelector('button[type="submit"]');
            if (b) b.disabled = false;
        });
    });

    document.querySelectorAll('form[data-comment-form]').forEach(function (form) {
        var button = form.querySelector('button[type="submit"]');
        form.addEventListener('submit', function (e) {
            e.preventDefault();
            if (button) button.disabled = true;
            form.setAttribute('aria-busy', 'true');
            show(form, '', true);
            var ready = null;   // when set, the button stays disabled until this settles
            tokens()
                .catch(function () {
                    // No tokens (blocked request, rate limit, flaky network): send the form the way it works without
                    // JavaScript. The server answers with the respond page, which keeps the draft and issues fresh tokens.
                    HTMLFormElement.prototype.submit.call(form);
                    return new Promise(function () { });   // the page is leaving; nothing below runs
                })
                .then(function () {
                    return fetch(form.action, { method: 'POST', body: new FormData(form), credentials: 'same-origin', headers: { 'X-Requested-With': 'fetch', 'Accept': 'application/json' } });
                })
                .then(function (res) {
                    return res.json().catch(function () { return { ok: false, message: 'The comment could not be sent. Try again in a moment.' }; })
                        .then(function (data) { return { status: res.status, data: data }; });
                })
                .then(function (r) {
                    if (r.data.ok) {
                        var text = form.querySelector('textarea[name="content"]');
                        if (text) text.value = '';
                        if (window.grecaptcha && window.grecaptcha.reset) { try { window.grecaptcha.reset(); } catch (_) { } }
                        show(form, r.data.message, true);
                        return;
                    }
                    // Fresh tokens for the next press. They are stamped when they arrive, and a send sooner than the
                    // minimum writing time after that would be discarded as spam, so the button waits for the tokens
                    // and then that long (a slow phone can take longer to fetch them than the wait itself).
                    if (r.data.expired) {
                        ready = tokens(true).then(function () { return new Promise(function (ok) { setTimeout(ok, 4500); }); },
                                                  function () { });
                    }
                    show(form, r.data.message, false);
                })
                .catch(function () { show(form, 'The comment could not be sent. Check your connection and try again.', false); })
                .finally(function () {
                    form.removeAttribute('aria-busy');
                    if (button) Promise.resolve(ready).then(function () { button.disabled = false; });
                });
        });
    });
})();
