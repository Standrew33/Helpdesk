// The ticket list follows its filters as they change: no Apply button. The page asks the server for the same
// list with the new filters and swaps in the parts marked data-live, so the search box keeps its focus.
(function () {
    'use strict';

    var form = document.querySelector('[data-live-filters]');

    if (!form || !window.fetch || !window.DOMParser) {
        return;
    }

    var pending = null;
    var typing = null;

    function load(url) {
        if (pending) {
            pending.abort();
        }

        pending = new AbortController();

        fetch(url, { signal: pending.signal, credentials: 'same-origin' })
            .then(function (response) {
                // A signed-out session or an error page is shown as a whole page.
                if (!response.ok || response.redirected) {
                    window.location.href = url;
                    return null;
                }

                return response.text();
            })
            .then(function (html) {
                if (html === null) {
                    return;
                }

                var next = new DOMParser().parseFromString(html, 'text/html');

                document.querySelectorAll('[data-live]').forEach(function (part) {
                    var fresh = next.getElementById(part.id);

                    if (fresh) {
                        part.replaceWith(document.importNode(fresh, true));
                    }
                });

                if (window.helpdeskLocalTime) {
                    window.helpdeskLocalTime(document.getElementById('ticket-results'));
                }

                history.replaceState(null, '', url);
            })
            .catch(function (error) {
                if (error.name !== 'AbortError') {
                    window.location.href = url;
                }
            });
    }

    function urlOfForm() {
        var query = new URLSearchParams(new FormData(form));

        if (!(query.get('q') || '').trim()) {
            query.delete('q');
        }

        var text = query.toString();
        return window.location.pathname + (text ? '?' + text : '');
    }

    form.addEventListener('change', function (event) {
        if (event.target.name !== 'q') {
            load(urlOfForm());
        }
    });

    form.addEventListener('input', function (event) {
        if (event.target.name === 'q') {
            clearTimeout(typing);
            typing = setTimeout(function () { load(urlOfForm()); }, 300);
        }
    });

    form.addEventListener('submit', function (event) {
        event.preventDefault();
        clearTimeout(typing);
        load(urlOfForm());
    });

    document.addEventListener('click', function (event) {
        var link = event.target.closest('a[data-live-link]');

        if (link && !event.ctrlKey && !event.metaKey && !event.shiftKey && event.button === 0) {
            event.preventDefault();
            load(link.href);
        }
    });
}());
