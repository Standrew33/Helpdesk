// Dropdowns whose options keep their colours: a Bootstrap dropdown holding radio buttons (one choice) or
// check boxes (several). The button shows what is chosen, drawn the same way as in the list.
(function () {
    'use strict';

    function render(choice) {
        var label = choice.querySelector('[data-choice-label]');
        var chosen = Array.prototype.slice.call(choice.querySelectorAll('input:checked'));

        label.textContent = '';

        if (chosen.length === 0) {
            var placeholder = document.createElement('span');
            placeholder.className = 'choice-placeholder';
            placeholder.textContent = choice.getAttribute('data-placeholder') || '';
            label.appendChild(placeholder);
            return;
        }

        label.appendChild(chosen[0].parentNode.querySelector('[data-choice-view]').cloneNode(true));

        if (chosen.length > 1) {
            var more = document.createElement('span');
            more.className = 'choice-more';
            more.textContent = '+' + (chosen.length - 1);
            label.appendChild(more);
        }
    }

    // Listening on the document keeps working for dropdowns a page puts in later.
    document.addEventListener('change', function (event) {
        var choice = event.target.closest('[data-choice]');

        if (!choice) {
            return;
        }

        render(choice);

        // One choice: done once it is made.
        if (!choice.hasAttribute('data-choice-multiple')) {
            var toggle = choice.querySelector('[data-bs-toggle="dropdown"]');
            bootstrap.Dropdown.getOrCreateInstance(toggle).hide();
            toggle.focus();
        }
    });

    function renderAll(root) {
        (root || document).querySelectorAll('[data-choice]').forEach(render);
    }

    window.helpdeskChoice = renderAll;
    renderAll(document);
}());
