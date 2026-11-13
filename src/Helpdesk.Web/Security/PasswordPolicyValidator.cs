using Helpdesk.Web.Data;
using Microsoft.AspNetCore.Identity;

namespace Helpdesk.Web.Security;

/// <summary>
/// Rules on top of the length requirement, following current NIST guidance (SP 800-63B): no composition rules
/// ("one capital, one symbol"), which push people to predictable patterns, but a refusal of passwords that are
/// well known or built from the account's own details.
/// </summary>
public sealed class PasswordPolicyValidator : IPasswordValidator<ApplicationUser>
{
    private const int MinimumPartLength = 4;

    // Words that appear at the heart of most guessable passwords. Digits, symbols and letter swaps ("P@ssw0rd!") are looked through.
    private static readonly HashSet<string> CommonWords = new(StringComparer.Ordinal)
    {
        "password", "passwort", "passphrase", "qwerty", "qwertyuiop", "qwertyuiopasdf", "asdfghjkl", "zxcvbnm", "letmein", "welcome",
        "administrator", "admin", "adminadmin", "iloveyou", "helpdesk", "changeme", "default", "secret", "football", "baseball",
        "basketball", "monkey", "dragon", "master", "superman", "batman", "sunshine", "princess", "shadow", "freedom", "whatever",
        "trustno", "trustnoone", "abcdefghijkl", "abcdefghijklmnop", "heslo", "zdravim", "praha", "company",
        "mypassword", "yourpassword", "newpassword", "userpassword", "loginlogin", "internet", "computer",
    };

    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrEmpty(password))
        {
            return Task.FromResult(IdentityResult.Success);
        }

        var errors = new List<IdentityError>();
        var lower = password.ToLowerInvariant();

        if (IsOnlyDigits(password) || IsSequence(lower) || IsBuiltOnCommonWord(lower))
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordTooCommon",
                Description = "This password is too easy to guess. Use a longer phrase of unrelated words.",
            });
        }

        if (ContainsPersonalDetails(lower, user))
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordContainsPersonalInfo",
                Description = "The password must not contain your name or e-mail address.",
            });
        }

        return Task.FromResult(errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]));
    }

    private static bool IsOnlyDigits(string value) => value.All(char.IsAsciiDigit);

    /// <summary>"abcdefghijkl", "123456789012", "987654321098": every character follows the previous one.</summary>
    private static bool IsSequence(string value)
    {
        if (value.Length < 2)
        {
            return false;
        }

        var step = value[1] - value[0];

        if (step is not (1 or -1))
        {
            return false;
        }

        for (var i = 1; i < value.Length; i++)
        {
            if (value[i] - value[i - 1] != step)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A common word with only digits and symbols around it or letters swapped for look-alikes:
    /// "Password-2024!", "password123456", "P@ssw0rd".
    /// </summary>
    private static bool IsBuiltOnCommonWord(string lower)
    {
        // Years and counters are added at the end; leave them off before looking at the word itself.
        var core = lower.TrimEnd(lower.Where(c => !char.IsLetter(c)).Distinct().ToArray());

        return CommonWords.Contains(LettersOnly(core, undoSubstitutions: false))
            || CommonWords.Contains(LettersOnly(core, undoSubstitutions: true));
    }

    /// <summary>The letters of the text, optionally with common substitutions undone: "p@55w0rd" becomes "password".</summary>
    private static string LettersOnly(string lower, bool undoSubstitutions)
    {
        var letters = new System.Text.StringBuilder(lower.Length);

        foreach (var c in lower)
        {
            var mapped = !undoSubstitutions ? c : c switch
            {
                '0' => 'o',
                '1' => 'i',
                '3' => 'e',
                '4' => 'a',
                '5' => 's',
                '7' => 't',
                '@' => 'a',
                '$' => 's',
                '!' => 'i',
                _ => c,
            };

            if (char.IsLetter(mapped))
            {
                letters.Append(mapped);
            }
        }

        return letters.ToString();
    }

    private static bool ContainsPersonalDetails(string lowerPassword, ApplicationUser user)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            parts.Add(user.Email.Split('@')[0]);
        }

        if (!string.IsNullOrWhiteSpace(user.DisplayName))
        {
            parts.AddRange(user.DisplayName.Split([' ', '-', '.', '_'], StringSplitOptions.RemoveEmptyEntries));
        }

        return parts.Any(p => p.Length >= MinimumPartLength && lowerPassword.Contains(p.ToLowerInvariant(), StringComparison.Ordinal));
    }
}
