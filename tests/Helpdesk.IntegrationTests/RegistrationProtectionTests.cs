using System.Net;
using Helpdesk.IntegrationTests.Infrastructure;

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

    [Fact]
    public async Task The_spam_trap_sends_no_e_mail()
    {
        var response = await NewClient().PostFormAsync("/Account/Login", "/Account/Login", new Dictionary<string, string>
        {
            ["Input.Email"] = "bot-target@example.test",
            ["Input.Website"] = "https://spam.example",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(Factory.Mail.Messages);
    }

    [Theory]
    [MemberData(nameof(UnsafeNames))]
    public async Task Names_with_invisible_or_reordering_characters_are_refused(string name)
    {
        var response = await SignUpAsync(NewClient(), "eve@example.test", name);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("contains characters that are not allowed", await response.Content.ReadAsStringAsync());
    }

}
