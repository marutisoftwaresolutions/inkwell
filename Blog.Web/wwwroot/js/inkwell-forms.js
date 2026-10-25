/*
 * Contact forms on public pages (ADR-0030): sends the form in place and shows the outcome without leaving the page.
 * Without this script the form posts normally and the server sends the reader back to it with the same messages.
 */
(function () {
    'use strict';

    function setError(form, key, message) {
        var field = form.querySelector('[data-field="' + key + '"]');
        if (!field) return null;
        var input = field.querySelector('input, textarea, select');
        var error = field.querySelector('.ink-form-field-error');
        field.classList.toggle('has-error', !!message);
        if (error) { error.textContent = message || ''; error.hidden = !message; }
        if (input) {
            if (message) {
                input.setAttribute('aria-invalid', 'true');
                var ids = (input.getAttribute('aria-describedby') || '').split(' ').filter(Boolean);
                if (error && ids.indexOf(error.id) < 0) { ids.push(error.id); input.setAttribute('aria-describedby', ids.join(' ')); }
            } else {
                input.removeAttribute('aria-invalid');
            }
        }
        return input;
    }

    function status(form, text, ok) {
        var box = form.querySelector('[data-ink-form-status]');
        if (!box) return;
        box.innerHTML = '';
        if (!text) return;
        var p = document.createElement('p');
        p.className = ok ? 'ink-form-sent' : 'ink-form-error';
        p.textContent = text;
        box.appendChild(p);
    }

    document.querySelectorAll('form[data-ink-form]').forEach(function (form) {
        var button = form.querySelector('button[type="submit"]');
        form.addEventListener('submit', function (e) {
            if (!window.fetch || !window.FormData) return;   // the normal post works without either
            e.preventDefault();
            form.querySelectorAll('[data-field]').forEach(function (f) { setError(form, f.getAttribute('data-field'), null); });
            status(form, '', true);
            if (button) button.disabled = true;
            form.setAttribute('aria-busy', 'true');
            fetch(form.action, { method: 'POST', body: new FormData(form), credentials: 'same-origin', headers: { 'X-Requested-With': 'fetch' } })
                .then(function (res) {
                    return res.json().catch(function () { return { ok: false, message: res.status === 400 ? 'This form has expired. Reload the page and send it again.' : 'The form could not be sent. Try again in a moment.' }; });
                })
                .then(function (data) {
                    if (data.ok) {
                        form.querySelectorAll('input:not([type=hidden]), textarea, select').forEach(function (el) {
                            if (el.type === 'checkbox') el.checked = false; else el.value = '';
                        });
                        status(form, data.message, true);
                        var box = form.querySelector('[data-ink-form-status]');
                        if (box) { box.setAttribute('tabindex', '-1'); box.focus(); }
                        return;
                    }
                    var first = null, count = 0;
                    Object.keys(data.errors || {}).forEach(function (key) {
                        count++;
                        var input = setError(form, key, data.errors[key]);
                        if (!first) first = input;
                    });
                    status(form, data.message || (count === 1 ? 'Check the field marked below.' : 'Check the ' + count + ' fields marked below.'), false);
                    if (first) first.focus();
                })
                .catch(function () { status(form, 'The form could not be sent. Check your connection and try again.', false); })
                .finally(function () { if (button) button.disabled = false; form.removeAttribute('aria-busy'); });
        });
    });
})();
