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

            showPencils(node);

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

    // ----- Text edited in place: the pencil turns it into a field, and leaving the field saves it. -----

    function showPencils(root) {
        root.querySelectorAll('[data-inline-start]').forEach(function (button) {
            button.hidden = false;
        });
    }

    function partsOf(form) {
        return {
            view: form.querySelector('[data-inline-view]'),
            field: form.querySelector('[data-inline-field]'),
            start: form.querySelector('[data-inline-start]'),
        };
    }

    // A text box grows with its text, up to the height the style sheet allows.
    function fit(field) {
        if (field.tagName === 'TEXTAREA') {
            field.style.height = 'auto';
            field.style.height = (field.scrollHeight + 2) + 'px';
        }
    }

    function open(form) {
        var parts = partsOf(form);
        parts.field.value = parts.field.defaultValue;
        parts.view.hidden = true;
        parts.start.hidden = true;
        parts.field.hidden = false;
        fit(parts.field);
        parts.field.focus();
        parts.field.setSelectionRange(parts.field.value.length, parts.field.value.length);
    }

    function close(parts) {
        parts.field.hidden = true;
        parts.view.hidden = false;
        parts.start.hidden = false;
    }

    function save(form) {
        var parts = partsOf(form);

        if (parts.field.hidden) {
            return;
        }

        var value = parts.field.value.trim();

        // Nothing changed, or everything erased: the text stays as it was.
        if (value === '' || value === parts.field.defaultValue.trim()) {
            parts.field.value = parts.field.defaultValue;
            close(parts);
            return;
        }

        parts.view.textContent = value;
        parts.field.defaultValue = parts.field.value;
        close(parts);
        send(form);
    }

    document.addEventListener('click', function (event) {
        var start = event.target.closest('[data-inline-start]');

        if (start) {
            open(start.closest('[data-inline-edit]'));
        }
    });

    document.addEventListener('focusout', function (event) {
        if (event.target.matches('[data-inline-field]')) {
            save(event.target.closest('[data-inline-edit]'));
        }
    });

    document.addEventListener('input', function (event) {
        if (event.target.matches('[data-inline-field]')) {
            fit(event.target);
        }
    });

    // Enter saves a one-line field, Ctrl+Enter a text box; Escape puts the text back as it was.
    document.addEventListener('keydown', function (event) {
        if (!event.target.matches('[data-inline-field]')) {
            return;
        }

        var form = event.target.closest('[data-inline-edit]');
        var parts = partsOf(form);

        if (event.key === 'Escape') {
            event.preventDefault();
            parts.field.value = parts.field.defaultValue;
            close(parts);
            parts.start.focus();
        } else if (event.key === 'Enter' && (event.target.tagName === 'INPUT' || event.ctrlKey || event.metaKey)) {
            event.preventDefault();
            save(form);
            parts.start.focus();
        }
    });

    showPencils(document);

    // Lists and choices that save on their own: no button to press.
    document.addEventListener('change', function (event) {
        var form = event.target.closest('form[data-instant]');

        if (form) {
            send(form);
        }
    });
}());
