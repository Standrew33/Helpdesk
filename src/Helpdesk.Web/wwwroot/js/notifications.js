// The bell in the sidebar. Opening it reads the notifications page and shows its list in place, so the list is
// always fresh; "Mark all as read" is sent in the background. While the page is open, the server says over a
// SignalR connection when something new arrived: the count and an open list are brought up to date, and a short
// message names the newest notification.
(function () {
    'use strict';

    var bell = document.querySelector('[data-bell]');

    if (!bell || !window.fetch || !window.DOMParser) {
        return;
    }

    var toggle = bell.querySelector('[data-bs-toggle="dropdown"]');
    var menu = bell.querySelector('[data-bell-menu]');
    var count = bell.querySelector('[data-unread-count]');
    var address = toggle.getAttribute('href');
    var newestShown = null;

    function setCount(text) {
        var unread = text && text !== '0' ? text : '';
        count.textContent = unread;
        count.hidden = !unread;
        toggle.setAttribute('aria-label', unread ? 'Notifications, ' + unread + ' unread' : 'Notifications');
    }

    function parse(html) {
        var page = new DOMParser().parseFromString(html, 'text/html');
        var list = page.getElementById('notification-list');
        return list ? { page: page, list: list } : null;
    }

    function countOf(read) {
        var fresh = read.page.querySelector('[data-unread-count]');
        return fresh && !fresh.hidden ? fresh.textContent.trim() : '';
    }

    function fill(read) {
        var node = document.importNode(read.list, true);
        node.removeAttribute('id');
        node.className = 'bell-list';
        menu.textContent = '';
        menu.appendChild(node);

        if (window.helpdeskLocalTime) {
            window.helpdeskLocalTime(menu);
        }
    }

    function show(html) {
        var read = parse(html);

        // Signed out or anything unexpected: the page itself tells what is going on.
        if (!read) {
            window.location.href = address;
            return;
        }

        fill(read);
        setCount(countOf(read));
    }

    function get() {
        return fetch(address, { credentials: 'same-origin' }).then(function (response) {
            return response.ok && !response.redirected ? response.text() : Promise.reject(new Error('Unexpected answer'));
        });
    }

    bell.addEventListener('show.bs.dropdown', function () {
        get().then(show).catch(function () {
            window.location.href = address;
        });
    });

    menu.addEventListener('submit', function (event) {
        var form = event.target.closest('form[data-read-all]');

        if (!form) {
            return;
        }

        event.preventDefault();

        // The answer is the notifications page again, now with everything read.
        fetch(form.action, { method: 'POST', body: new FormData(form), credentials: 'same-origin' })
            .then(function (response) {
                return response.ok ? response.text() : Promise.reject(new Error('Not saved'));
            })
            .then(show)
            .catch(function () {
                form.submit();
            });
    });

    // ----- Live: the server says something new is there; the page reads it like the bell does. -----

    var hub = bell.getAttribute('data-hub');

    if (!hub || !window.signalR) {
        return;
    }

    function toast(read) {
        var newest = read.list.querySelector('.notification.is-unread');

        if (!newest || newest.getAttribute('href') + newest.textContent === newestShown) {
            return;
        }

        newestShown = newest.getAttribute('href') + newest.textContent;

        var area = document.querySelector('.toast-area');

        if (!area) {
            area = document.createElement('div');
            area.className = 'toast-area toast-container';
            area.setAttribute('aria-live', 'polite');
            document.body.appendChild(area);
        }

        var message = document.createElement('a');
        message.className = 'toast notification-toast';
        message.href = newest.getAttribute('href');
        message.setAttribute('role', 'status');

        var text = newest.querySelector('.notification-text').cloneNode(true);
        text.className = 'notification-text';
        message.appendChild(text);
        area.appendChild(message);

        message.addEventListener('hidden.bs.toast', function () {
            message.remove();
        });

        bootstrap.Toast.getOrCreateInstance(message, { delay: 6000 }).show();
    }

    function arrived() {
        get().then(function (html) {
            var read = parse(html);

            if (!read) {
                return;
            }

            setCount(countOf(read));

            if (menu.classList.contains('show')) {
                fill(read);
            }

            toast(read);
        }).catch(function () {
            // The next page drawn shows the count anyway.
        });
    }

    var connection = new signalR.HubConnectionBuilder()
        .withUrl(hub)
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.None)
        .build();

    connection.on('notified', arrived);

    // A connection that was down may have missed something.
    connection.onreconnected(arrived);

    connection.start().catch(function () {
        // Without the connection the bell still shows what the pages bring.
    });
}());
