using System.Net;
using Helpdesk.IntegrationTests.Infrastructure;
using Helpdesk.Web.Security;

namespace Helpdesk.IntegrationTests;

public class PasswordStrengthMeterTests(SqlServerFixture sql) : WebTestBase(sql)
{
    private static string ScriptSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Helpdesk.slnx")))
        {
            directory = directory.Parent;
        }

        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "Helpdesk.Web", "wwwroot", "js", "password-strength.js"));
    }

    [Fact]
    public void The_meter_knows_the_same_common_words_as_the_server()
    {
        var script = ScriptSource();

        foreach (var word in PasswordPolicyValidator.KnownWords)
        {
            Assert.Contains($"'{word}'", script, StringComparison.Ordinal);
        }

        Assert.Contains($"minimumPartLength = {PasswordPolicyValidator.MinimumPartLength}", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fields_for_a_new_password_show_the_meter()
    {
        var client = NewClient();
        await SignInWithCodeAsync(client, "nina@example.test");

        var welcome = await client.GetHtmlAsync("/Account/Welcome");
        Assert.Contains("data-strength", welcome);
        Assert.Contains("/js/password-strength", welcome);

        await client.PostFormAsync("/Account/Welcome", "/Account/Welcome", new Dictionary<string, string> { ["Input.DisplayName"] = "Nina" });
        var settings = await client.GetHtmlAsync("/Account/Settings");
        Assert.Contains("data-strength", settings);
        Assert.Contains("/js/password-strength", settings);
    }

    [Fact]
    public async Task The_script_is_served_from_the_site_itself()
    {
        var html = await NewClient().GetHtmlAsync("/Account/Login");
        var response = await NewClient().GetAsync("/js/password-strength.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("password-strength", html);
    }
}
