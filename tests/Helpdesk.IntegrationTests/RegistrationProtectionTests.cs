using System.Net;
using Helpdesk.IntegrationTests.Infrastructure;
using Helpdesk.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk.IntegrationTests;

public class RegistrationProtectionTests(SqlServerFixture sql) : WebTestBase(sql)
{
    private const char Rlo = (char)0x202E;
    private const char ZeroWidthSpace = (char)0x200B;

    public static TheoryData<string> UnsafeNames => new()
    {
        "Eve" + Rlo + "Evil",
        "Zero" + ZeroWidthSpace + "Width",
        "Bell" + (char)7,
    };

    private static Dictionary<string, string> Form(string email) => new()
    {
        ["Input.DisplayName"] = "Pat Example",
        ["Input.Email"] = email,
        ["Input.Password"] = "Brand-new-pass-42",
        ["Input.ConfirmPassword"] = "Brand-new-pass-42",
    };

    [Fact]
    public async Task The_spam_trap_creates_no_account()
    {
        var form = Form("bot@example.test");
        form["Input.Website"] = "https://spam.example";

        var response = await NewClient().PostFormAsync("/Account/Register", "/Account/Register", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.OriginalString);
        await using var scope = Factory.Services.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync("bot@example.test"));
    }

    [Theory]
    [MemberData(nameof(UnsafeNames))]
    public async Task Names_with_invisible_or_reordering_characters_are_refused(string name)
    {
        var form = Form("eve@example.test");
        form["Input.DisplayName"] = name;

        var response = await NewClient().PostFormAsync("/Account/Register", "/Account/Register", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("contains characters that are not allowed", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_demo_address_with_stray_spaces_is_still_reserved()
    {
        var response = await NewClient().PostFormAsync("/Account/Register", "/Account/Register", Form(" admin@helpdesk.test "));

        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
    }
}
