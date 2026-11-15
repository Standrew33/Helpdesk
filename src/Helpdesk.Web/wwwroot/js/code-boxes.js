// The six boxes of the sign-in code: one digit each, the cursor moves on by itself, and a code pasted or filled in
// by the phone's keyboard is spread over all boxes. Without this script the boxes still work, one digit at a time.
(function () {
  'use strict';

  document.querySelectorAll('[data-code-boxes]').forEach(function (group) {
    var boxes = Array.prototype.slice.call(group.querySelectorAll('input'));

    function digitsOf(text) {
      return (text || '').replace(/\D/g, '');
    }

    // Writes digits into the boxes from the given one on and puts the cursor after the last of them.
    function fill(from, digits) {
      var index = from;

      for (var i = 0; i < digits.length && index < boxes.length; i++, index++) {
        boxes[index].value = digits.charAt(i);
      }

      boxes[Math.min(index, boxes.length - 1)].focus();
    }

    boxes.forEach(function (box, index) {
      box.addEventListener('focus', function () {
        box.select();
      });

      box.addEventListener('keydown', function (event) {
        if (event.key === 'Backspace' && box.value === '' && index > 0) {
          event.preventDefault();
          boxes[index - 1].value = '';
          boxes[index - 1].focus();
        } else if (event.key === 'ArrowLeft' && index > 0) {
          event.preventDefault();
          boxes[index - 1].focus();
        } else if (event.key === 'ArrowRight' && index < boxes.length - 1) {
          event.preventDefault();
          boxes[index + 1].focus();
        } else if (/^[0-9]$/.test(event.key) && !event.ctrlKey && !event.metaKey && !event.altKey) {
          // A digit replaces whatever the box held.
          event.preventDefault();
          fill(index, event.key);
        }
      });

      // Phones that fill in the code from a message, and keyboards that send no key events, end up here.
      box.addEventListener('input', function () {
        var digits = digitsOf(box.value);
        box.value = '';

        if (digits.length > 0) {
          fill(digits.length >= boxes.length ? 0 : index, digits.slice(0, boxes.length));
        }
      });

      box.addEventListener('paste', function (event) {
        var digits = digitsOf((event.clipboardData || window.clipboardData).getData('text'));
        event.preventDefault();

        if (digits.length > 0) {
          // A whole code goes into all boxes, whichever box it was pasted into.
          fill(digits.length >= boxes.length ? 0 : index, digits.slice(0, boxes.length));
        }
      });
    });
  });
})();
