// Dates come from the server in UTC; they are shown in the reader's own time zone, without the year when it is
// this year: "15 Nov, 14:30". The full date stays in the tooltip.
(function () {
    'use strict';

    var thisYear = new Date().getFullYear();
    var short = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });
    var long = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
    var full = new Intl.DateTimeFormat('en-GB', { dateStyle: 'full', timeStyle: 'short' });

    function show(root) {
        (root || document).querySelectorAll('time[data-local-time]').forEach(function (time) {
            var moment = new Date(time.getAttribute('datetime'));

            if (isNaN(moment.getTime())) {
                return;
            }

            time.textContent = (moment.getFullYear() === thisYear ? short : long).format(moment);
            time.title = full.format(moment);
        });
    }

    window.helpdeskLocalTime = show;
    show(document);
}());
