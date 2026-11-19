// Messages on how an operation went, at the top right. The server draws them into the next page; a page that
// saves in the background hands the page it read to window.helpdeskToasts, and its messages are moved here.
(function () {
    'use strict';

    var stack = document.querySelector('[data-toasts]');

    if (!stack || !window.bootstrap) {
        return;
    }

    // Older messages make room when many come at once.
    var mostShown = 4;

    function start(toast) {
        toast.addEventListener('hidden.bs.toast', function () {
            toast.remove();
        });

        // Drawn visible for pages without scripts; shown again here so that a success can fade away.
        toast.classList.remove('show');
        bootstrap.Toast.getOrCreateInstance(toast).show();

        var shown = stack.querySelectorAll('.toast');

        for (var i = 0; i < shown.length - mostShown; i++) {
            shown[i].remove();
        }
    }

    stack.querySelectorAll('.toast').forEach(start);

    window.helpdeskToasts = function (page) {
        var fresh = page.querySelector('[data-toasts]');

        if (!fresh) {
            return;
        }

        Array.prototype.forEach.call(fresh.querySelectorAll('.toast'), function (toast) {
            var node = document.importNode(toast, true);
            stack.appendChild(node);
            start(node);
        });
    };
}());
