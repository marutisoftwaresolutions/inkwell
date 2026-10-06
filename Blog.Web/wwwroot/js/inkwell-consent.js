/* Inkwell cookie consent (v1.0.8). Self-hosted, no dependencies.
 *
 * The server renders the same hidden banner for every reader (so HTTP caching is unaffected); this
 * script decides from the reader's own first-party cookie whether to show it and whether Google
 * Analytics may load. Nothing optional loads before a choice, and without JavaScript nothing loads
 * at all. Reject is exactly as easy as Accept, and "Cookie settings" reopens the banner at any time.
 */
(function () {
    'use strict';
    var banner = document.getElementById('ink-consent');
    if (!banner) return;

    var NAME = banner.getAttribute('data-cookie') || 'inkwell_consent';
    var DAYS = parseInt(banner.getAttribute('data-days'), 10) || 180;
    var gaId = banner.getAttribute('data-ga-id') || '';
    var gaLoaded = false;

    function read() {
        var m = document.cookie.match(new RegExp('(?:^|; )' + NAME + '=([^;]*)'));
        return m ? decodeURIComponent(m[1]) : null;
    }

    function write(value) {
        var secure = location.protocol === 'https:' ? '; Secure' : '';
        document.cookie = NAME + '=' + encodeURIComponent(value) + '; Max-Age=' + (DAYS * 86400) + '; Path=/; SameSite=Lax' + secure;
    }

    function loadAnalytics() {
        if (!gaId || gaLoaded) return;
        gaLoaded = true;
        window['ga-disable-' + gaId] = false;
        window.dataLayer = window.dataLayer || [];
        window.gtag = window.gtag || function () { window.dataLayer.push(arguments); };
        window.gtag('js', new Date());
        window.gtag('config', gaId);
        var s = document.createElement('script');
        s.async = true;
        s.src = 'https://www.googletagmanager.com/gtag/js?id=' + encodeURIComponent(gaId);
        document.head.appendChild(s);
    }

    // Withdrawing consent: stop the tag for this page and remove the cookies it set.
    function stopAnalytics() {
        if (gaId) window['ga-disable-' + gaId] = true;
        var host = location.hostname;
        var domains = ['', host, '.' + host.replace(/^www\./, '')];
        document.cookie.split('; ').forEach(function (c) {
            var name = c.split('=')[0];
            if (name !== '_ga' && name.indexOf('_ga_') !== 0 && name !== '_gid' && name !== '_gat') return;
            domains.forEach(function (d) {
                document.cookie = name + '=; Max-Age=0; Path=/' + (d ? '; Domain=' + d : '');
            });
        });
    }

    var lastFocus = null;
    function show() {
        lastFocus = document.activeElement;
        banner.hidden = false;
        var first = banner.querySelector('button');
        if (first) first.focus();
    }
    function hide() {
        banner.hidden = true;
        if (lastFocus && lastFocus !== document.body && document.contains(lastFocus)) lastFocus.focus();
    }

    banner.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-consent]');
        if (!btn) return;
        var choice = btn.getAttribute('data-consent');      // granted | denied | ack
        write(choice);
        if (choice === 'granted') loadAnalytics(); else stopAnalytics();
        hide();
    });
    banner.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && read() !== null) hide();         // only once a choice exists; no choice is not a choice
    });

    document.querySelectorAll('[data-cookie-settings]').forEach(function (el) {
        el.hidden = false;
        el.addEventListener('click', function (e) { e.preventDefault(); show(); });
    });

    // An "ack" of the essential-only notice is not consent to analytics added later: ask again then.
    var current = read();
    var asking = !!gaId;
    var decided = asking ? (current === 'granted' || current === 'denied') : current !== null;
    if (asking && current === 'granted') loadAnalytics();
    else if (!decided) { banner.hidden = false; }            // first visit: show without stealing focus
})();
