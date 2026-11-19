// A date field marked data-date-picker becomes a button that opens a calendar in a Bootstrap dropdown: a click
// anywhere on the field opens it, and nothing is typed. The chosen day goes into a hidden field as yyyy-MM-dd, which
// raises a change event like any field would. Without scripts the browser's own date field stays.
(function () {
    'use strict';

    if (!window.bootstrap) {
        return;
    }

    var monthName = new Intl.DateTimeFormat('en-GB', { month: 'long', year: 'numeric', timeZone: 'UTC' });
    var dayName = new Intl.DateTimeFormat('en-GB', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' });
    var shortName = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' });
    var weekdays = ['Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su'];

    // Days are handled as UTC midnights, so no time zone can move them.
    function parse(text) {
        var parts = /^(\d{4})-(\d{2})-(\d{2})$/.exec(text || '');
        return parts ? new Date(Date.UTC(+parts[1], +parts[2] - 1, +parts[3])) : null;
    }

    function iso(day) {
        return day.toISOString().slice(0, 10);
    }

    function addDays(day, count) {
        return new Date(day.getTime() + count * 86400000);
    }

    // Due dates are counted in UTC on the server too.
    function todayUtc() {
        var now = new Date();
        return new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate()));
    }

    function element(tag, className, text) {
        var node = document.createElement(tag);

        if (className) {
            node.className = className;
        }

        if (text !== undefined) {
            node.textContent = text;
        }

        return node;
    }

    function setUp(input) {
        var value = element('input');
        value.type = 'hidden';
        value.name = input.name;
        value.value = input.value;

        var min = parse(input.getAttribute('min'));
        var placeholder = input.getAttribute('data-placeholder') || 'No date';

        var picker = element('div', 'dropdown date-picker');

        var toggle = element('button', input.className + ' date-picker-toggle');
        toggle.type = 'button';
        toggle.id = input.id;
        toggle.setAttribute('data-bs-toggle', 'dropdown');
        toggle.setAttribute('data-bs-auto-close', 'outside');
        toggle.setAttribute('aria-expanded', 'false');
        toggle.setAttribute('aria-haspopup', 'dialog');
        var text = element('span', 'date-picker-text');
        toggle.appendChild(text);

        var menu = element('div', 'dropdown-menu date-picker-menu');
        menu.setAttribute('role', 'dialog');
        menu.setAttribute('aria-label', 'Choose a day');

        var head = element('div', 'date-picker-head');
        var previous = element('button', 'icon-btn', '‹');
        previous.type = 'button';
        previous.setAttribute('aria-label', 'Previous month');
        var title = element('span', 'date-picker-title');
        title.setAttribute('aria-live', 'polite');
        var next = element('button', 'icon-btn', '›');
        next.type = 'button';
        next.setAttribute('aria-label', 'Next month');
        head.append(previous, title, next);

        var grid = element('div', 'date-picker-grid');
        grid.setAttribute('role', 'grid');

        var foot = element('div', 'date-picker-foot');
        var clear = element('button', 'btn btn-link btn-sm', 'Clear');
        clear.type = 'button';
        var today = element('button', 'btn btn-outline-primary btn-sm', 'Today');
        today.type = 'button';
        foot.append(clear, today);

        menu.append(head, grid, foot);
        picker.append(value, toggle, menu);
        input.replaceWith(picker);

        // The month on show and the day the keyboard is on.
        var shown = null;
        var focused = null;

        function chosen() {
            return parse(value.value);
        }

        function showText() {
            var day = chosen();
            text.textContent = day ? shortName.format(day) : placeholder;
            text.classList.toggle('date-picker-placeholder', !day);
        }

        function allowed(day) {
            return !min || day >= min;
        }

        function render() {
            title.textContent = monthName.format(shown);
            grid.textContent = '';

            weekdays.forEach(function (name) {
                grid.appendChild(element('span', 'date-picker-weekday', name));
            });

            var first = new Date(Date.UTC(shown.getUTCFullYear(), shown.getUTCMonth(), 1));
            var lead = (first.getUTCDay() + 6) % 7;

            for (var i = 0; i < lead; i++) {
                grid.appendChild(element('span'));
            }

            var current = chosen();
            var now = todayUtc();

            for (var day = first; day.getUTCMonth() === first.getUTCMonth(); day = addDays(day, 1)) {
                var button = element('button', 'date-picker-day', String(day.getUTCDate()));
                button.type = 'button';
                button.setAttribute('data-day', iso(day));
                button.setAttribute('aria-label', dayName.format(day));
                button.tabIndex = focused && iso(day) === iso(focused) ? 0 : -1;
                button.disabled = !allowed(day);
                button.classList.toggle('is-today', iso(day) === iso(now));

                if (current && iso(day) === iso(current)) {
                    button.classList.add('is-chosen');
                    button.setAttribute('aria-pressed', 'true');
                }

                grid.appendChild(button);
            }

            var earliest = min && new Date(Date.UTC(min.getUTCFullYear(), min.getUTCMonth(), 1));
            previous.disabled = !!earliest && first <= earliest;
        }

        function showMonthOf(day, focus) {
            shown = new Date(Date.UTC(day.getUTCFullYear(), day.getUTCMonth(), 1));
            focused = day;
            render();

            if (focus) {
                var target = grid.querySelector('[data-day="' + iso(day) + '"]');

                if (target && !target.disabled) {
                    target.focus();
                }
            }
        }

        function choose(day) {
            value.value = day ? iso(day) : '';
            showText();
            bootstrap.Dropdown.getOrCreateInstance(toggle).hide();
            toggle.focus();
            value.dispatchEvent(new Event('change', { bubbles: true }));
        }

        picker.addEventListener('show.bs.dropdown', function () {
            var start = chosen() || todayUtc();
            showMonthOf(allowed(start) ? start : min, false);
        });

        picker.addEventListener('shown.bs.dropdown', function () {
            showMonthOf(focused, true);
        });

        previous.addEventListener('click', function () {
            showMonthOf(new Date(Date.UTC(shown.getUTCFullYear(), shown.getUTCMonth() - 1, 1)), false);
        });

        next.addEventListener('click', function () {
            showMonthOf(new Date(Date.UTC(shown.getUTCFullYear(), shown.getUTCMonth() + 1, 1)), false);
        });

        grid.addEventListener('click', function (event) {
            var button = event.target.closest('[data-day]');

            if (button && !button.disabled) {
                choose(parse(button.getAttribute('data-day')));
            }
        });

        // Arrows move by a day or a week, also into the next or previous month.
        grid.addEventListener('keydown', function (event) {
            var steps = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 }[event.key];

            if (!steps || !focused) {
                return;
            }

            event.preventDefault();
            var target = addDays(focused, steps);

            if (allowed(target)) {
                showMonthOf(target, true);
            }
        });

        clear.addEventListener('click', function () {
            choose(null);
        });

        today.addEventListener('click', function () {
            choose(todayUtc());
        });

        showText();
    }

    function setUpAll(root) {
        (root || document).querySelectorAll('input[type="date"][data-date-picker]').forEach(setUp);
    }

    // Parts of the ticket page drawn anew bring their date fields back as they came from the server.
    document.addEventListener('helpdesk:updated', function (event) {
        setUpAll(event.detail);
    });

    setUpAll(document);
}());
