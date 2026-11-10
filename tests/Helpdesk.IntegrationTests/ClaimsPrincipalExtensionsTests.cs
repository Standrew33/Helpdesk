using System.Security.Claims;
using Helpdesk.Core.Users;
using Helpdesk.Web.Data;
using Helpdesk.Web.Security;

namespace Helpdesk.IntegrationTests;

public class ClaimsPrincipalExtensionsTests
{
    private static ClaimsPrincipal Principal(string? id, params string[] roles)
    {
        var claims = roles.Select(r => new Claim(ClaimTypes.Role, r)).ToList();

        if (id is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, id));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Theory]
    [InlineData(RoleNames.Customer, UserRole.Customer)]
    [InlineData(RoleNames.Operator, UserRole.Operator)]
    [InlineData(RoleNames.Admin, UserRole.Admin)]
    public void ToActor_maps_the_role(string roleName, UserRole expected)
    {
        var actor = Principal("user-1", roleName).ToActor();

        Assert.Equal(new Actor("user-1", expected), actor);
    }

    [Fact]
    public void ToActor_prefers_the_most_powerful_role()
    {
        var actor = Principal("user-1", RoleNames.Customer, RoleNames.Admin).ToActor();

        Assert.Equal(UserRole.Admin, actor.Role);
    }

    [Fact]
    public void ToActor_requires_a_user_id()
    {
        Assert.Throws<InvalidOperationException>(() => Principal(null, RoleNames.Customer).ToActor());
    }

    [Fact]
    public void ToActor_requires_a_role()
    {
        Assert.Throws<InvalidOperationException>(() => Principal("user-1").ToActor());
    }
}
