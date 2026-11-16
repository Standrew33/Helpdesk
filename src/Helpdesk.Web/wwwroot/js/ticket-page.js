// The ticket page saves a change as soon as it is made: the form is posted in the background, the page the
// server answers with is read, and the parts marked data-live are swapped in. A part where the reader has typed
// something not yet sent, such as a comment, is left alone so nothing is lost.
(function () {
    'use strict';

    if (!window.fetch || !window.DOMParser) {
        return;
    }

    // One save at a time: each one needs the version of the ticket the previous one produced.
    var queue = Promise.resolve();
    var version = null;

    function hasUnsentText(part) {
        return Array.prototype.some.call(part.querySelectorAll('textarea, input[type="text"], input[type="file"]'), function (field) {
            return field.value !== field.defaultValue;
        });
    }

    function refresh(html, fallbackUrl) {
        var next = new DOMParser().parseFromString(html, 'text/html');

        // Signed out, or anything else that is not this ticket: show it as a whole page.
        if (!next.getElementById('ticket-header')) {
            window.location.href = fallbackUrl;
            return;
        }

        // The control that was used is drawn anew; the keyboard should stay on it.
        var focusedId = document.activeElement ? document.activeElement.id : '';
        var freshVersion = next.querySelector('input[name="rowVersion"]');
        version = freshVersion ? freshVersion.value : version;

        document.querySelectorAll('[data-live]').forEach(function (part) {
            var fresh = next.getElementById(part.id);

            if (!fresh || hasUnsentText(part)) {
                return;
            }

            var node = document.importNode(fresh, true);
            part.replaceWith(node);

            if (window.helpdeskChoice) {
                window.helpdeskChoice(node);
            }

            if (window.helpdeskLocalTime) {
                window.helpdeskLocalTime(node);
            }

            document.dispatchEvent(new CustomEvent('helpdesk:updated', { detail: node }));
        });

        if (focusedId && !document.activeElement.closest('main')) {
            var again = document.getElementById(focusedId);

            if (again) {
                again.focus();
            }
        }

        // Parts that were kept still post the version they were drawn with; bring them up to date.
        if (version) {
            document.querySelectorAll('input[name="rowVersion"]').forEach(function (field) {
                field.value = version;
            });
        }
    }

    function send(form) {
        queue = queue.then(function () {
            var current = form.querySelector('input[name="rowVersion"]');

            if (current && version) {
                current.value = version;
            }

            form.setAttribute('aria-busy', 'true');

            return fetch(form.action, { method: 'POST', body: new FormData(form), credentials: 'same-origin' })
                .then(function (response) {
                    if (!response.ok) {
                        window.location.href = response.url;
                        return;
                    }

                    return response.text().then(function (html) { refresh(html, response.url); });
                })
                .catch(function () {
                    form.submit();
                })
                .then(function () {
                    form.removeAttribute('aria-busy');
                });
        });

        return queue;
    }

    window.helpdeskSend = send;

    // Lists and choices that save on their own: no button to press.
    document.addEventListener('change', function (event) {
        var form = event.target.closest('form[data-instant]');

        if (form) {
            send(form);
        }
    });
}());
