using Helpdesk.Web.Data;
using Helpdesk.Web.Security;

namespace Helpdesk.IntegrationTests;

public class PasswordPolicyTests
{
    private static readonly ApplicationUser Nina = new() { Email = "nina.novak@example.test", UserName = "nina.novak@example.test", DisplayName = "Nina Novak" };

    private static async Task<IReadOnlyList<string>> ErrorsAsync(string password)
    {
        var result = await new PasswordPolicyValidator().ValidateAsync(null!, Nina, password);
        return result.Errors.Select(e => e.Code).ToList();
    }

    [Theory]
    [InlineData("Brand-new-pass-42")]
    [InlineData("correct horse battery staple")]
    [InlineData("purple-Kettle-27-sings")]
    [InlineData("tram7Lamp")]
    public async Task Unrelated_passwords_are_accepted(string password)
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
}
