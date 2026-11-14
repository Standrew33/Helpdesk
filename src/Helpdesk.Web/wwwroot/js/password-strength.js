// Password strength meter: a bar that fills and changes colour, and a short verdict in words.
// It works on every <input data-strength> and mirrors the server's rules (PasswordPolicyValidator), which stay
// the ones that decide. Inputs named in data-strength-related (CSS selectors) hold the user's e-mail and name.
(function () {
  'use strict';

  // Keep in step with PasswordPolicyValidator.CommonWords; a test compares the two lists.
  var commonWords = [
    'password', 'passwort', 'passphrase', 'qwerty', 'qwertyuiop', 'qwertyuiopasdf', 'asdfghjkl', 'zxcvbnm', 'letmein', 'welcome',
    'administrator', 'admin', 'adminadmin', 'iloveyou', 'helpdesk', 'changeme', 'default', 'secret', 'football', 'baseball',
    'basketball', 'monkey', 'dragon', 'master', 'superman', 'batman', 'sunshine', 'princess', 'shadow', 'freedom', 'whatever',
    'trustno', 'trustnoone', 'abcdefghijkl', 'abcdefghijklmnop', 'heslo', 'zdravim', 'praha', 'company',
    'mypassword', 'yourpassword', 'newpassword', 'userpassword', 'loginlogin', 'internet', 'computer'
  ];

  var minimumLength = 8;
  var minimumPartLength = 4;
  var lookAlikes = { '0': 'o', '1': 'i', '3': 'e', '4': 'a', '5': 's', '7': 't', '@': 'a', '$': 's', '!': 'i' };

  var verdicts = [
    'Too easy to guess',
    'Weak: add more words or characters',
    'Fair',
    'Good',
    'Strong'
  ];

  function isLetter(c) {
    return c.toLowerCase() !== c.toUpperCase();
  }

  function lettersOnly(text, undoLookAlikes) {
    var result = '';
    for (var i = 0; i < text.length; i++) {
      var c = undoLookAlikes && lookAlikes[text[i]] ? lookAlikes[text[i]] : text[i];
      if (isLetter(c)) {
        result += c;
      }
    }
    return result;
  }

  function isBuiltOnCommonWord(lower) {
    var end = lower.length;
    while (end > 0 && !isLetter(lower[end - 1])) {
      end--;
    }
    var core = lower.slice(0, end);
    return commonWords.indexOf(lettersOnly(core, false)) >= 0 || commonWords.indexOf(lettersOnly(core, true)) >= 0;
  }

  function isSequence(lower) {
    if (lower.length < 2) {
      return false;
    }
    var step = lower.charCodeAt(1) - lower.charCodeAt(0);
    if (step !== 1 && step !== -1) {
      return false;
    }
    for (var i = 1; i < lower.length; i++) {
      if (lower.charCodeAt(i) - lower.charCodeAt(i - 1) !== step) {
        return false;
      }
    }
    return true;
  }

  function personalParts(input) {
    var selectors = (input.getAttribute('data-strength-related') || '').split(',');
    var parts = [];
    selectors.forEach(function (selector) {
      var field = selector.trim() && document.querySelector(selector.trim());
      var value = field ? (field.value || field.textContent || '') : '';
      value.split('@')[0].split(/[\s.\-_]+/).concat(value.split('@')[0]).forEach(function (part) {
        if (part.length >= minimumPartLength) {
          parts.push(part.toLowerCase());
        }
      });
    });
    return parts;
  }

  // 0: will be refused, 1: weak, 2: fair, 3: good, 4: strong.
  function score(password, input) {
    var lower = password.toLowerCase();

    if (password.length < minimumLength) {
      return { score: 0, text: 'Too short: at least ' + minimumLength + ' characters' };
    }

    if (/^\d+$/.test(password) || isSequence(lower) || isBuiltOnCommonWord(lower)) {
      return { score: 0, text: verdicts[0] };
    }

    if (personalParts(input).some(function (part) { return lower.indexOf(part) >= 0; })) {
      return { score: 0, text: 'Must not contain your name or e-mail' };
    }

    if (new Set(password).size < 5) {
      return { score: 0, text: 'Too many repeated characters' };
    }

    var pool = 0;
    if (/[a-z]/.test(password)) { pool += 26; }
    if (/[A-Z]/.test(password)) { pool += 26; }
    if (/\d/.test(password)) { pool += 10; }
    if (/[^a-zA-Z\d]/.test(password)) { pool += 33; }

    // Rough guessing work in bits, discounted when characters repeat.
    var bits = password.length * Math.log2(Math.max(pool, 10)) * Math.min(1, new Set(password).size / password.length + 0.35);
    var result = bits < 40 ? 1 : bits < 60 ? 2 : bits < 80 ? 3 : 4;
    return { score: result, text: verdicts[result] };
  }

  function attach(input) {
    var meter = document.createElement('div');
    meter.className = 'strength';
    meter.hidden = true;

    var bar = document.createElement('div');
    bar.className = 'strength-bar';
    bar.setAttribute('aria-hidden', 'true');
    bar.appendChild(document.createElement('span'));

    var text = document.createElement('div');
    text.className = 'strength-text';
    text.setAttribute('aria-live', 'polite');
    text.id = (input.id || 'password') + '-strength';

    meter.appendChild(bar);
    meter.appendChild(text);
    input.insertAdjacentElement('afterend', meter);
    input.setAttribute('aria-describedby', ((input.getAttribute('aria-describedby') || '') + ' ' + text.id).trim());

    function update() {
      if (!input.value) {
        meter.hidden = true;
        return;
      }
      var result = score(input.value, input);
      meter.hidden = false;
      meter.setAttribute('data-score', String(result.score));
      text.textContent = result.text;
    }

    input.addEventListener('input', update);
    update();
  }

  document.querySelectorAll('input[data-strength]').forEach(attach);
})();
