/*
 * Reader-side lightbox for the gallery block. Without this script every gallery image is a plain link to
 * its file, so nothing is lost when it does not run. With it, a link opens the image in a native <dialog>:
 * Escape closes it (the browser's own behaviour), arrow keys and the buttons move between images of the
 * same gallery, and focus returns to the image that opened it. Self-hosted; no dependencies.
 */
(function () {
    'use strict';
    if (!('HTMLDialogElement' in window)) return;   // very old browsers keep the plain links

    let dialog, img, caption, count, prevBtn, nextBtn, links = [], index = 0, opener = null;

    function build() {
        dialog = document.createElement('dialog');
        dialog.className = 'ink-lightbox';
        dialog.setAttribute('aria-label', 'Image viewer');
        dialog.innerHTML =
            '<figure><img alt=""><figcaption></figcaption></figure>' +
            '<div class="ink-lightbox-bar">' +
            '<button type="button" data-step="-1" aria-label="Previous image">&larr;</button>' +
            '<span class="ink-lightbox-count" aria-live="polite"></span>' +
            '<button type="button" data-step="1" aria-label="Next image">&rarr;</button>' +
            '<button type="button" data-close aria-label="Close image viewer">Close</button>' +
            '</div>';
        document.body.appendChild(dialog);
        img = dialog.querySelector('img');
        caption = dialog.querySelector('figcaption');
        count = dialog.querySelector('.ink-lightbox-count');
        prevBtn = dialog.querySelector('[data-step="-1"]');
        nextBtn = dialog.querySelector('[data-step="1"]');

        dialog.addEventListener('click', function (e) {
            const step = e.target.closest('[data-step]');
            if (step) { show(index + Number(step.getAttribute('data-step'))); return; }
            if (e.target.closest('[data-close]') || e.target === dialog) dialog.close();   // a click on the backdrop closes
        });
        dialog.addEventListener('keydown', function (e) {
            if (e.key === 'ArrowLeft') { e.preventDefault(); show(index - 1); }
            else if (e.key === 'ArrowRight') { e.preventDefault(); show(index + 1); }
        });
        dialog.addEventListener('close', function () {
            img.removeAttribute('src');
            if (opener && opener.focus) opener.focus();
        });
    }

    function show(i) {
        if (i < 0 || i >= links.length) return;
        index = i;
        const a = links[i];
        const thumb = a.querySelector('img');
        img.src = a.getAttribute('href');
        img.alt = thumb ? thumb.getAttribute('alt') || '' : '';
        const cap = a.closest('.comp-gallery-item')?.querySelector('figcaption');
        caption.textContent = cap ? cap.textContent : '';
        caption.hidden = !cap;
        const many = links.length > 1;
        prevBtn.hidden = nextBtn.hidden = count.hidden = !many;
        const had = document.activeElement;
        prevBtn.disabled = i === 0;
        nextBtn.disabled = i === links.length - 1;
        count.textContent = (i + 1) + ' of ' + links.length;
        // Reaching the first or last image disables the button that was pressed; keep focus in the dialog.
        if (dialog.open && (had === prevBtn || had === nextBtn) && had.disabled)
            (prevBtn.disabled ? (nextBtn.disabled ? dialog.querySelector('[data-close]') : nextBtn) : prevBtn).focus();
    }

    document.addEventListener('click', function (e) {
        if (e.defaultPrevented || e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;   // new tab etc. still work
        const a = e.target.closest && e.target.closest('.comp-gallery .comp-gallery-item a');
        if (!a) return;
        e.preventDefault();
        if (!dialog) build();
        links = Array.from(a.closest('.comp-gallery').querySelectorAll('.comp-gallery-item a'));
        opener = a;
        show(links.indexOf(a));
        dialog.showModal();
        dialog.querySelector('[data-close]').focus();
    });
})();
