using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Helpdesk.Core.Users;
using Helpdesk.Web.Data;
using Helpdesk.Web.Features.Email;
using Helpdesk.Web.Features.SignIn;
using Helpdesk.Web.Features.Tickets;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Web.Features.Workspaces;

public sealed record InvitationRow(Guid Id, string Email, string Role, DateTimeOffset ExpiresAt, string InvitedBy, bool CanWithdraw);

/// <summary>An invitation that can still be accepted, with what the invited person is told about it.</summary>
public sealed record OpenInvitation(Guid Id, string Email, string Role, int WorkspaceId, string WorkspaceName, string InvitedBy, bool AddressHasAccount);

/// <summary>
/// Staff bring people into their help desk by e-mail: administrators with any role, operators as customers.
/// The link in the e-mail proves the address, so accepting it creates the account straight away.
/// </summary>
public partial class InvitationService(
    HelpdeskDbContext db,
    UserManager<ApplicationUser> userManager,
    IEmailSender email,
    TimeProvider time,
    ILogger<InvitationService> logger)
{
    /// <summary>How many invitations one help desk may send in a day, so it cannot be used to flood mailboxes.</summary>
    public const int MaxPerDay = 50;

    public const string GoneMessage = "This invitation is no longer valid. Ask for a new one.";

    public const string GoneCode = "InvitationGone";

    public static IReadOnlyList<string> RolesFor(UserRole role) => role switch
    {
        UserRole.Admin => [RoleNames.Customer, RoleNames.Operator, RoleNames.Admin],
        UserRole.Operator => [RoleNames.Customer],
        _ => [],
    };

    public async Task<ServiceResult> InviteAsync(Actor inviter, string? address, string? role, string baseUrl, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(inviter);

        if (inviter.IsDemo)
        {
            return ServiceResult.Invalid("The demo accounts cannot send invitations.");
        }

        if (role is null || !RolesFor(inviter.Role).Contains(role))
        {
            return ServiceResult.Invalid("You cannot invite people with this role.");
        }

        var to = address?.Trim().ToLowerInvariant() ?? string.Empty;

        if (to.Length is 0 or > 256 || !to.Contains('@', StringComparison.Ordinal))
        {
            return ServiceResult.Invalid("Enter the e-mail address of the person to invite.");
        }

        if (DatabaseSeeder.IsDemoAddress(to))
        {
            return ServiceResult.Invalid("This address is reserved for the demo.");
        }

        var now = time.GetUtcNow();
        var sentToday = await db.Invitations.CountAsync(i => i.WorkspaceId == inviter.WorkspaceId && i.CreatedAt > now.AddDays(-1), ct);

        if (sentToday >= MaxPerDay)
        {
            return ServiceResult.Invalid($"This help desk has sent {MaxPerDay} invitations in the last 24 hours. Try again later.");
        }

        // A new invitation to the same address ends the one still waiting; ended invitations still count for the limit.
        await db.Invitations
            .Where(i => i.WorkspaceId == inviter.WorkspaceId && i.Email == to && i.AcceptedAt == null && i.ExpiresAt > now)
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.ExpiresAt, now), ct);

        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var invitation = new Invitation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = inviter.WorkspaceId,
            Email = to,
            Role = role,
            TokenHash = Hash(token),
            InvitedById = inviter.Id,
            CreatedAt = now,
            ExpiresAt = now + Invitation.Lifetime,
        };

        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(ct);

        var workspaceName = await db.Workspaces.Where(w => w.Id == inviter.WorkspaceId).Select(w => w.Name).SingleAsync(ct);
        var inviterName = await db.Users.Where(u => u.Id == inviter.Id).Select(u => u.DisplayName).SingleAsync(ct);
        var link = $"{baseUrl}/Account/Invitation?token={Uri.EscapeDataString(token)}";

        try
        {
            await email.SendAsync(InvitationEmails.For(to, inviterName, workspaceName, role, link, Invitation.Lifetime), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SendFailed(logger, ex);
            db.Invitations.Remove(invitation);
            await db.SaveChangesAsync(CancellationToken.None);
            return ServiceResult.Invalid(CodeMailer.FailedMessage);
        }

        SecurityLog.Invited(logger, inviter.Id, inviter.WorkspaceId, role);
        return ServiceResult.Success();
    }

    /// <summary>The invitations of the viewer's help desk that are still waiting, newest first.</summary>
    public async Task<IReadOnlyList<InvitationRow>> ListWaitingAsync(Actor viewer, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var roles = RolesFor(viewer.Role);

        var rows = await (
            from i in db.Invitations
            where i.WorkspaceId == viewer.WorkspaceId && i.AcceptedAt == null && i.ExpiresAt > now
            join u in db.Users on i.InvitedById equals u.Id into inviters
            from u in inviters.DefaultIfEmpty()
            orderby i.CreatedAt descending
            select new { i.Id, i.Email, i.Role, i.ExpiresAt, InvitedBy = u == null ? null : u.DisplayName })
            .ToListAsync(ct);

        return rows
            .Select(r => new InvitationRow(r.Id, r.Email, r.Role, r.ExpiresAt, r.InvitedBy ?? "-", roles.Contains(r.Role)))
            .ToList();
    }

    /// <summary>Ends an invitation that is still waiting; staff may withdraw the ones they could have sent.</summary>
    public async Task<ServiceResult> WithdrawAsync(Actor actor, Guid id, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var invitation = await db.Invitations.FirstOrDefaultAsync(
            i => i.Id == id && i.WorkspaceId == actor.WorkspaceId && i.AcceptedAt == null && i.ExpiresAt > now, ct);

        if (invitation is null)
        {
            return ServiceResult.NotFound();
        }

        if (actor.IsDemo || !RolesFor(actor.Role).Contains(invitation.Role))
        {
            return ServiceResult.Invalid("You cannot withdraw this invitation.");
        }

        invitation.ExpiresAt = now;
        await db.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }

    public Task<OpenInvitation?> FindOpenAsync(string? token, CancellationToken ct = default)
    {
        var hash = Hash(token ?? string.Empty);
        return FindOpenAsync(i => i.TokenHash == hash, ct);
    }

    public Task<OpenInvitation?> FindOpenAsync(Guid id, CancellationToken ct = default)
        => FindOpenAsync(i => i.Id == id, ct);

    /// <summary>
    /// Creates the account of an invited person in the inviting help desk, with the role of the invitation.
    /// The invitation is used up only when the account exists, and only once even if accepted twice at the same moment.
    /// </summary>
    public async Task<IdentityResult> JoinAsync(Guid invitationId, ApplicationUser user, string? password)
    {
        ArgumentNullException.ThrowIfNull(user);
        var invitation = await FindOpenAsync(invitationId);

        if (invitation is null || invitation.AddressHasAccount)
        {
            return Gone();
        }

        user.Email = invitation.Email;
        user.UserName = invitation.Email;
        user.EmailConfirmed = true;
        user.WorkspaceId = invitation.WorkspaceId;

        var created = string.IsNullOrEmpty(password)
            ? await userManager.CreateAsync(user)
            : await userManager.CreateAsync(user, password);

        if (!created.Succeeded)
        {
            return created;
        }

        var now = time.GetUtcNow();
        var used = await db.Invitations
            .Where(i => i.Id == invitationId && i.AcceptedAt == null && i.ExpiresAt > now)
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.AcceptedAt, now));

        var inRole = used == 1 ? await userManager.AddToRoleAsync(user, invitation.Role) : Gone();

        if (!inRole.Succeeded)
        {
            await userManager.DeleteAsync(user);

            if (used == 1)
            {
                await db.Invitations.Where(i => i.Id == invitationId).ExecuteUpdateAsync(set => set.SetProperty(i => i.AcceptedAt, (DateTimeOffset?)null));
            }

            return inRole;
        }

        SecurityLog.JoinedByInvitation(logger, user.Id, invitation.WorkspaceId);
        return IdentityResult.Success;
    }

    private async Task<OpenInvitation?> FindOpenAsync(System.Linq.Expressions.Expression<Func<Invitation, bool>> which, CancellationToken ct)
    {
        var now = time.GetUtcNow();

        var found = await (
            from i in db.Invitations.Where(which)
            where i.AcceptedAt == null && i.ExpiresAt > now
            join w in db.Workspaces on i.WorkspaceId equals w.Id
            join u in db.Users on i.InvitedById equals u.Id into inviters
            from u in inviters.DefaultIfEmpty()
            select new { i.Id, i.Email, i.Role, i.WorkspaceId, WorkspaceName = w.Name, InvitedBy = u == null ? null : u.DisplayName })
            .FirstOrDefaultAsync(ct);

        if (found is null)
        {
            return null;
        }

        var hasAccount = await userManager.FindByEmailAsync(found.Email) is not null;
        return new OpenInvitation(found.Id, found.Email, found.Role, found.WorkspaceId, found.WorkspaceName, found.InvitedBy ?? "Someone", hasAccount);
    }

    private static IdentityResult Gone() => IdentityResult.Failed(new IdentityError { Code = GoneCode, Description = GoneMessage });

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not send an invitation e-mail")]
    private static partial void SendFailed(ILogger logger, Exception exception);
}
