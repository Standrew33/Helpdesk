// The dialog that confirms deleting an account is shared by every delete button of the page: as it opens, it takes
// the id and the name of the account from the button that opened it.
(function () {
    'use strict';

    var dialog = document.getElementById('delete-account');

    if (!dialog) {
        return;
    }

    dialog.addEventListener('show.bs.modal', function (event) {
        var button = event.relatedTarget;

        if (!button) {
            event.preventDefault();
            return;
        }

        dialog.querySelector('[data-delete-id]').value = button.getAttribute('data-delete-id');
        dialog.querySelector('[data-delete-name]').textContent = button.getAttribute('data-delete-name');
    });
}());
