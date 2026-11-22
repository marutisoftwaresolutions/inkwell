/* Inkwell share links. The links themselves are plain anchors and work without this file. This adds
   two buttons that need a browser API: copy the post's address, and the device's own share sheet
   where there is one. Self-hosted; nothing is sent anywhere until the reader chooses to share. */
(function () {
	document.querySelectorAll('[data-share]').forEach(function (nav) {
		var url = nav.getAttribute('data-share-url') || location.href;
		var title = nav.getAttribute('data-share-title') || document.title;
		var status = nav.querySelector('[data-share-status]');
		var say = function (text) {
			if (!status) return;
			status.textContent = text;
			setTimeout(function () { if (status.textContent === text) status.textContent = ''; }, 4000);
		};

		var copy = nav.querySelector('[data-share-copy]');
		if (copy && navigator.clipboard && navigator.clipboard.writeText) {
			copy.hidden = false;
			copy.addEventListener('click', function () {
				navigator.clipboard.writeText(url).then(
					function () { say('Link copied.'); },
					function () { say('The link could not be copied. Copy it from the address bar.'); });
			});
		}

		var native = nav.querySelector('[data-share-native]');
		if (native && typeof navigator.share === 'function') {
			native.hidden = false;
			native.addEventListener('click', function () {
				navigator.share({ title: title, url: url }).catch(function () { /* the reader closed the sheet */ });
			});
		}
	});

})();
