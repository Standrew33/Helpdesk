using Helpdesk.Web.Data;
using Helpdesk.Web.Security;

namespace Helpdesk.IntegrationTests;

public class PasswordPolicyTests
{
    private const char Rlo = (char)0x202E;
    private const char ZeroWidthSpace = (char)0x200B;
    private const char PrivateUse = (char)0xE000;

    private static readonly ApplicationUser Nina = new() { Email = "nina.novak@example.test", UserName = "nina.novak@example.test", DisplayName = "Nina Novak" };

    public static TheoryData<string, bool> NameCases => new()
    {
        { "Anna Novak", true },
        { Text(0x5A, 0x6F, 0xEB, 0x20, 0x4D, 0xFC, 0x6C, 0x6C, 0x65, 0x72, 0x2D, 0x160, 0x69, 0x6D, 0x6B, 0x6F, 0x76, 0xE1), true },
        { Text(0x41E, 0x43B, 0x44C, 0x433, 0x430, 0x20, 0x41F, 0x435, 0x442, 0x440, 0x43E, 0x432, 0x430), true },
        { Text(0x5C71, 0x7530, 0x20, 0x592A, 0x90CE), true },
        { Text(0x4A, 0x6F, 0x20, 0x1F600), true },
        { "Eve" + Rlo + "Evil", false },
        { "Zero" + ZeroWidthSpace + "width", false },
        { "Line" + '\n' + "break", false },
        { "Tab" + '\t' + "here", false },
        { "Bell" + (char)7, false },
        { "Private" + PrivateUse + "use", false },
        { "Lone" + (char)0xD800 + "surrogate", false },
    };

    private static string Text(params int[] codePoints) => string.Concat(codePoints.Select(char.ConvertFromUtf32));

    private static async Task<IReadOnlyList<string>> ErrorsAsync(string password)
    {
        var result = await new PasswordPolicyValidator().ValidateAsync(null!, Nina, password);
        return result.Errors.Select(e => e.Code).ToList();
    }

    [Theory]
    [InlineData("Brand-new-pass-42")]
    [InlineData("correct horse battery staple")]
    [InlineData("purple-Kettle-27-sings")]
    public async Task Long_unrelated_passwords_are_accepted(string password)
        => Assert.Empty(await ErrorsAsync(password));

    [Theory]
    [InlineData("password123456")]
    [InlineData("Password-2024!!")]
    [InlineData("P@ssw0rd-2024!")]
    [InlineData("qwertyuiop-9876")]
    [InlineData("ILoveYou1234567")]
    [InlineData("123456789012")]
    [InlineData("abcdefghijkl")]
    [InlineData("987654321098")]
    public async Task Well_known_and_patterned_passwords_are_refused(string password)
        => Assert.Contains("PasswordTooCommon", await ErrorsAsync(password));

    [Theory]
    [InlineData("nina.novak-2024-xyz")]
    [InlineData("My-name-is-NOVAK-77")]
    [InlineData("Nina's secret phrase")]
    public async Task Passwords_built_from_the_account_details_are_refused(string password)
        => Assert.Contains("PasswordContainsPersonalInfo", await ErrorsAsync(password));

    [Fact]
    public async Task Short_name_parts_do_not_cause_false_alarms()
    {
        var user = new ApplicationUser { Email = "al@example.test", UserName = "al@example.test", DisplayName = "Al Li" };

        var result = await new PasswordPolicyValidator().ValidateAsync(null!, user, "Alpha-line-bridge-12");

        Assert.True(result.Succeeded);
    }

    [Theory]
    [MemberData(nameof(NameCases))]
    public void Names_are_checked_for_invisible_and_reordering_characters(string name, bool safe)
        => Assert.Equal(safe, SafeTextAttribute.IsSafe(name));
}
