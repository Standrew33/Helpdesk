// reCAPTCHA v3: just before a protected form is sent, ask Google for a token for this form's action and send it
// along. The server checks the token; nothing is shown to the person unless the check fails.
(function () {
  'use strict';

  document.querySelectorAll('input[data-captcha-action]').forEach(function (field) {
    var form = field.closest('form');
    var sending = false;

    if (!form) {
      return;
    }

    form.addEventListener('submit', function (event) {
      if (sending) {
        return;
      }

      // Let client-side validation speak first; there is no point in a token for a form that will not be sent.
      if (window.jQuery && window.jQuery(form).valid && !window.jQuery(form).valid()) {
        return;
      }

      event.preventDefault();

      if (!window.grecaptcha) {
        // The script did not load (blocked or offline); send the form and let the server answer.
        sending = true;
        form.submit();
        return;
      }

      window.grecaptcha.ready(function () {
        window.grecaptcha
          .execute(field.getAttribute('data-captcha-site-key'), { action: field.getAttribute('data-captcha-action') })
          .then(function (token) {
            field.value = token;
            sending = true;
            form.submit();
          });
      });
    });
  });
})();
