using Helpdesk.Core.Workspaces;

namespace Helpdesk.Core.Tests;

public class WorkspaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_workspace_has_a_working_link_with_a_code_nobody_can_guess()
    {
        var workspace = Workspace.Create("  Acme support  ", Now);

        Assert.Equal("Acme support", workspace.Name);
        Assert.True(workspace.IsJoinOpen);
        Assert.Matches("^[0-9a-f]{32}$", workspace.JoinCode);
        Assert.NotEqual(workspace.JoinCode, Workspace.Create("Acme support", Now).JoinCode);
    }

    [Fact]
    public void A_new_code_replaces_the_old_one()
    {
        var workspace = Workspace.Create("Acme support", Now);
        var old = workspace.JoinCode;

        workspace.RenewJoinCode();

        Assert.NotEqual(old, workspace.JoinCode);
        Assert.Matches("^[0-9a-f]{32}$", workspace.JoinCode);
    }

    [Fact]
    public void The_link_can_be_turned_off_and_on()
    {
        var workspace = Workspace.Create("Acme support", Now);

        workspace.SetJoinOpen(false);
        Assert.False(workspace.IsJoinOpen);

        workspace.SetJoinOpen(true);
        Assert.True(workspace.IsJoinOpen);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_workspace_needs_a_name(string name)
        => Assert.Throws<DomainException>(() => Workspace.Create(name, Now));
}
