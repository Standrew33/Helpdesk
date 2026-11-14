using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;

namespace Helpdesk.Web.Security;

/// <summary>
/// Refuses text with characters that cannot be seen or that reorder what is shown: control characters, text-direction
/// overrides, zero-width marks. They let one person look like another in lists and comments.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class SafeTextAttribute : ValidationAttribute
{
    public SafeTextAttribute() : base("{0} contains characters that are not allowed.")
    {
    }

    public static bool IsSafe(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        foreach (var rune in value.EnumerateRunes())
        {
            if (rune == Rune.ReplacementChar)
            {
                return false;
            }

            var category = Rune.GetUnicodeCategory(rune);

            if (category is UnicodeCategory.Control
                or UnicodeCategory.Format
                or UnicodeCategory.Surrogate
                or UnicodeCategory.PrivateUse
                or UnicodeCategory.OtherNotAssigned
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator)
            {
                return false;
            }
        }

        return true;
    }

    public override bool IsValid(object? value) => value is null || IsSafe(value as string);
}
