// The bell in the sidebar. Opening it reads the notifications page and shows its list in place, so the list is
// always fresh; "Mark all as read" is sent in the background. The count on the bell comes with every page drawn.
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

    function setCount(text) {
        var unread = text && text !== '0' ? text : '';
        count.textContent = unread;
        count.hidden = !unread;
        toggle.setAttribute('aria-label', unread ? 'Notifications, ' + unread + ' unread' : 'Notifications');
    }

    function show(html) {
        var page = new DOMParser().parseFromString(html, 'text/html');
        var list = page.getElementById('notification-list');

        // Signed out or anything unexpected: the page itself tells what is going on.
        if (!list) {
            window.location.href = address;
            return;
        }

        var node = document.importNode(list, true);
        node.removeAttribute('id');
        node.className = 'bell-list';
        menu.textContent = '';
        menu.appendChild(node);

        var fresh = page.querySelector('[data-unread-count]');
        setCount(fresh && !fresh.hidden ? fresh.textContent.trim() : '');

        if (window.helpdeskLocalTime) {
            window.helpdeskLocalTime(menu);
        }
    }

    function read(response) {
        return response.ok && !response.redirected ? response.text() : Promise.reject(new Error('Unexpected answer'));
    }

    function load() {
        return fetch(address, { credentials: 'same-origin' })
            .then(read)
            .then(show)
            .catch(function () {
                window.location.href = address;
            });
    }

    bell.addEventListener('show.bs.dropdown', load);

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
}());
