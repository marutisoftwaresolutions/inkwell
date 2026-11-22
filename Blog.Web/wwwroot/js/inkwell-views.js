/* Inkwell page-view beacon. Sends the page's signed token, path, query and referrer to /pv once,
   after the page has loaded. Self-hosted, no cookies, no identifiers stored in the browser. A page
   that never runs this script is never counted as a view. */
(function () {
	var script = document.currentScript;
	var token = script && script.getAttribute('data-pv');
	if (!token) return;

	var body = new URLSearchParams();
	body.append('t', token);
	body.append('p', location.pathname);
	body.append('q', location.search.replace(/^\?/, ''));
	body.append('r', document.referrer || '');

	try {
		if (navigator.sendBeacon && navigator.sendBeacon('/pv', body)) return;
	} catch (e) { /* fall through to fetch */ }

	try {
		fetch('/pv', { method: 'POST', body: body, keepalive: true, credentials: 'same-origin' });
	} catch (e) { /* analytics never breaks the page */ }
})();

/* Link-click goals (Admin, Analytics, Goals). The page lists the links the site watches as
   [{k: key, u: start of the address}]; following one sends the key and nothing about the reader.
   The link itself is never delayed or changed. */
(function () {
	var script = document.querySelector('script[data-pv][data-goals]');
	if (!script) return;
	var goals;
	try { goals = JSON.parse(script.getAttribute('data-goals')) || []; } catch (e) { return; }
	if (!goals.length) return;
	var token = script.getAttribute('data-pv');

	function matches(link, start) {
		var raw = link.getAttribute('href') || '';
		return raw.indexOf(start) === 0 || link.href.indexOf(start) === 0
			|| (start.charAt(0) === '/' && link.origin === location.origin && link.pathname.indexOf(start) === 0);
	}

	document.addEventListener('click', function (event) {
		var link = event.target && event.target.closest ? event.target.closest('a[href]') : null;
		if (!link) return;
		for (var i = 0; i < goals.length; i++) {
			if (!matches(link, goals[i].u)) continue;
			var body = new URLSearchParams();
			body.append('t', token);
			body.append('p', location.pathname);
			body.append('g', goals[i].k);
			try {
				if (!(navigator.sendBeacon && navigator.sendBeacon('/pv/goal', body)))
					fetch('/pv/goal', { method: 'POST', body: body, keepalive: true, credentials: 'same-origin' });
			} catch (e) { /* analytics never breaks the page */ }
		}
	}, true);
})();
