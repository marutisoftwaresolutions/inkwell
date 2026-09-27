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
