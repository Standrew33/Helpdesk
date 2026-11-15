// An eye button in every password field shows or hides what was typed.
(function () {
  'use strict';

  var svg = 'http://www.w3.org/2000/svg';

  // Feather-style eye; the slash is shown while the password is visible.
  function icon() {
    var image = document.createElementNS(svg, 'svg');
    image.setAttribute('viewBox', '0 0 24 24');
    image.setAttribute('width', '20');
    image.setAttribute('height', '20');
    image.setAttribute('aria-hidden', 'true');
    image.setAttribute('focusable', 'false');

    [
      ['path', { d: 'M1 12s4-7 11-7 11 7 11 7-4 7-11 7S1 12 1 12z' }],
      ['circle', { cx: '12', cy: '12', r: '3' }],
      ['line', { x1: '3', y1: '3', x2: '21', y2: '21', class: 'password-toggle-slash' }],
    ].forEach(function (part) {
      var element = document.createElementNS(svg, part[0]);
      Object.keys(part[1]).forEach(function (name) {
        element.setAttribute(name, part[1][name]);
      });
      image.appendChild(element);
    });

    return image;
  }

  document.querySelectorAll('input[type="password"]').forEach(function (input) {
    var field = document.createElement('div');
    field.className = 'password-field';
    input.parentNode.insertBefore(field, input);
    field.appendChild(input);

    var button = document.createElement('button');
    button.type = 'button';
    button.className = 'password-toggle';
    button.setAttribute('aria-label', 'Show password');
    button.setAttribute('aria-pressed', 'false');
    button.appendChild(icon());
    field.appendChild(button);

    button.addEventListener('click', function () {
      var show = input.type === 'password';
      input.type = show ? 'text' : 'password';
      button.setAttribute('aria-pressed', show ? 'true' : 'false');
      button.setAttribute('aria-label', show ? 'Hide password' : 'Show password');
      input.focus();
    });
  });
})();
