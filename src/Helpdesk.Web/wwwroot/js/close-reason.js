// A customer who closes a ticket says why first. Choosing "Closed" in a status form marked
// data-close-reason="required" opens a dialog that asks for the reason; the status goes back to what it was until
// the reason is sent, and stays so when the dialog is cancelled.
(function () {
    'use strict';

    var dialog = document.getElementById('close-reason');

    if (!dialog || !window.bootstrap) {
        return;
    }

    var form = dialog.querySelector('form');
    var reason = form.querySelector('textarea[name="reason"]');
    var version = form.querySelector('input[name="rowVersion"]');
    var modal = bootstrap.Modal.getOrCreateInstance(dialog);
    var source = null;

    function toggleOf(statusForm) {
        return statusForm.querySelector('[data-bs-toggle="dropdown"]');
    }

    // Listening while the event goes down, so the status form never sees the choice and is not sent.
    document.addEventListener('change', function (event) {
        var statusForm = event.target.closest('form[data-close-reason="required"]');

        if (!statusForm || event.target.name !== 'status' || event.target.value !== 'Closed') {
            return;
        }

        event.stopPropagation();

        statusForm.querySelectorAll('input[name="status"]').forEach(function (option) {
            option.checked = option.defaultChecked;
        });

        if (window.helpdeskChoice) {
            window.helpdeskChoice(statusForm);
        }

        bootstrap.Dropdown.getOrCreateInstance(toggleOf(statusForm)).hide();

        source = statusForm;
        form.action = statusForm.action;
        version.value = statusForm.querySelector('input[name="rowVersion"]').value;
        reason.value = '';
        modal.show();
    }, true);

    dialog.addEventListener('shown.bs.modal', function () {
        reason.focus();
    });

    dialog.addEventListener('hidden.bs.modal', function () {
        if (source && document.body.contains(toggleOf(source))) {
            toggleOf(source).focus();
        }
    });

    // A page that saves in the background (window.helpdeskSend) sends the reason the same way; otherwise the form
    // is posted as it is.
    form.addEventListener('submit', function (event) {
        if (!window.helpdeskSend) {
            return;
        }

        event.preventDefault();
        modal.hide();
        window.helpdeskSend(form);
    });
}());
