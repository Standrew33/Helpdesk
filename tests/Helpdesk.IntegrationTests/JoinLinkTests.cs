using System.Net;
using System.Text.RegularExpressions;
using Helpdesk.IntegrationTests.Infrastructure;
using Helpdesk.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk.IntegrationTests;

/// <summary>Anyone with the public link of a help desk can sign up there as a customer, while the link works.</summary>
public partial class JoinLinkTests(SqlServerFixture sql) : WebTestBase(sql)
{
    private const string Boss = "boss@acme.test";

    private async Task<AppClient> AcmeAdminAsync()
    {
        var client = NewClient();
        await SignUpAsync(client, Boss, "Bea Boss", workspace: "Acme support");
        return client;
    }

    private static async Task<string> JoinPathAsync(AppClient staff)
        => new Uri(JoinLinkPattern().Match(await staff.GetHtmlAsync("/Invitations")).Groups[1].Value).AbsolutePath;

    private static Task<HttpResponseMessage> SetLinkOpenAsync(AppClient admin, bool open)
        => admin.PostFormAsync("/Invitations", "/Invitations?handler=JoinLink", new Dictionary<string, string> { ["open"] = open ? "true" : "false" });

    private async Task<ApplicationUser?> UserAsync(string email)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
    }

    private async Task<IList<string>> RolesOfAsync(string email)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.GetRolesAsync((await users.FindByEmailAsync(email))!);
    }

    [Fact]
    public async Task A_visitor_signs_up_through_the_link_as_a_customer_of_the_help_desk()
    {
        var boss = await AcmeAdminAsync();
        var link = await JoinPathAsync(boss);
        var visitor = NewClient();

        var opened = await visitor.GetAsync(link);
        Assert.Equal(HttpStatusCode.Redirect, opened.StatusCode);
        Assert.Equal("/Account/Login", opened.Headers.Location!.OriginalString);
        Assert.Contains("Contact Acme support", await visitor.GetHtmlAsync("/Account/Login"));

        await SignInWithCodeAsync(visitor, "carl@client.test");
        var welcome = await visitor.GetHtmlAsync("/Account/Welcome");
        Assert.Contains("Join Acme support", welcome);
        Assert.DoesNotContain("Name of your help desk", welcome);

        await visitor.PostFormAsync("/Account/Welcome", "/Account/Welcome", new Dictionary<string, string> { ["Input.DisplayName"] = "Carl Client" });

        Assert.Equal((await UserAsync(Boss))!.WorkspaceId, (await UserAsync("carl@client.test"))!.WorkspaceId);
        Assert.Equal(["Customer"], await RolesOfAsync("carl@client.test"));
        await visitor.CreateTicketAsync("Where is my order?");
        Assert.Contains("Where is my order?", await boss.GetHtmlAsync("/Tickets"));
    }

    [Fact]
    public async Task A_new_help_desk_has_a_working_link_and_the_shared_one_does_not()
    {
        var boss = await AcmeAdminAsync();
        Assert.Contains("Anyone with this link", await boss.GetHtmlAsync("/Invitations"));

        var demo = await SignedInAsync("Admin");
        var demoPage = await demo.GetHtmlAsync("/Invitations");
        Assert.Contains("The link is turned off", demoPage);
        Assert.DoesNotContain("Turn the link on", demoPage);
    }

    [Fact]
    public async Task A_turned_off_link_does_not_work_until_it_is_turned_on()
    {
        var boss = await AcmeAdminAsync();
        var link = await JoinPathAsync(boss);

        await SetLinkOpenAsync(boss, open: false);
        var visitor = NewClient();
        Assert.Contains("This link does not work", await visitor.GetHtmlAsync(link));
        Assert.DoesNotContain("Contact Acme support", await visitor.GetHtmlAsync("/Account/Login"));
        Assert.Contains("The link is turned off", await boss.GetHtmlAsync("/Invitations"));

        await SetLinkOpenAsync(boss, open: true);
        Assert.Equal(HttpStatusCode.Redirect, (await NewClient().GetAsync(link)).StatusCode);
    }

    [Fact]
    public async Task A_new_link_stops_the_old_one()
    {
        var boss = await AcmeAdminAsync();
        var old = await JoinPathAsync(boss);

        await boss.PostFormAsync("/Invitations", "/Invitations?handler=NewJoinLink", new Dictionary<string, string>());
        var renewed = await JoinPathAsync(boss);

        Assert.NotEqual(old, renewed);
        Assert.Contains("This link does not work", await NewClient().GetHtmlAsync(old));
        Assert.Equal(HttpStatusCode.Redirect, (await NewClient().GetAsync(renewed)).StatusCode);
    }

    [Fact]
    public async Task A_link_turned_off_during_the_sign_up_creates_no_account()
    {
        var boss = await AcmeAdminAsync();
        var visitor = NewClient();
        await visitor.GetAsync(await JoinPathAsync(boss));

        await SetLinkOpenAsync(boss, open: false);
        await SignInWithCodeAsync(visitor, "carl@client.test");

        Assert.Contains("This link no longer works", await visitor.GetHtmlAsync("/Account/Welcome"));
        var posted = await visitor.PostFormAsync("/Account/Login", "/Account/Welcome", new Dictionary<string, string> { ["Input.DisplayName"] = "Carl" });
        Assert.Contains("This link no longer works", await posted.Content.ReadAsStringAsync());
        Assert.Null(await UserAsync("carl@client.test"));
    }

    [Fact]
    public async Task Operators_pass_the_link_on_but_cannot_change_it()
    {
        var boss = await AcmeAdminAsync();
        var acme = (await UserAsync(Boss))!.WorkspaceId!.Value;
        var olga = await SignedInAsUserAsync("Operator", "olga@acme.test", acme);

        var page = await olga.GetHtmlAsync("/Invitations");
        Assert.Equal(await JoinPathAsync(boss), await JoinPathAsync(olga));
        Assert.DoesNotContain("Turn the link off", page);

        var refused = await (await olga.FollowAsync(await SetLinkOpenAsync(olga, open: false))).Content.ReadAsStringAsync();
        Assert.Contains("Only the administrators of this help desk can change its link", refused);
        Assert.Contains("Anyone with this link", await boss.GetHtmlAsync("/Invitations"));
    }

    [Fact]
    public async Task An_existing_account_coming_through_the_link_stays_where_it_is()
    {
        var boss = await AcmeAdminAsync();
        await CreateUserAsync("Customer", "taken@example.test");
        var visitor = NewClient();
        await visitor.GetAsync(await JoinPathAsync(boss));

        var signedIn = await SignInWithCodeAsync(visitor, "taken@example.test");

        Assert.Equal("/Tickets", signedIn.Headers.Location!.OriginalString);
        Assert.Equal(SharedWorkspace.Id, (await UserAsync("taken@example.test"))!.WorkspaceId);
    }

    [Theory]
    [InlineData("/Join/0123456789abcdef0123456789abcdef")]
    [InlineData("/Join/short")]
    public async Task An_unknown_link_does_not_work(string link)
        => Assert.Contains("This link does not work", await NewClient().GetHtmlAsync(link));

    [Fact]
    public async Task A_signed_in_person_opening_a_link_stays_in_their_help_desk()
    {
        var boss = await AcmeAdminAsync();
        var other = NewClient();
        await SignUpAsync(other, "eve@other.test", "Eve", workspace: "Other desk");

        var response = await other.GetAsync(await JoinPathAsync(boss));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Tickets", response.Headers.Location!.OriginalString);
    }

    [GeneratedRegex("id=\"join-link\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex JoinLinkPattern();
}
