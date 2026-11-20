using System.Globalization;
using System.Security.Claims;
using Helpdesk.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Web.Security;

/// <summary>
/// The owner of the platform belongs to no help desk, yet works on the tickets of any of them through the same pages
/// as its staff: the ticket list and the ticket page are also reached at /Platform/Workspace/{id}/Tickets. On those
/// addresses, and only for the owner, the request is made as an administrator of that help desk. Nothing is stored:
/// the next request outside them is the owner's own again.
/// </summary>
public sealed class PlatformVisit(IHttpContextAccessor accessor, HelpdeskDbContext db) : IClaimsTransformation
{
    public const string RouteKey = "workspaceId";

    private const string Prefix = "Platform/Workspace/{" + RouteKey + ":int}/Tickets";

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (!principal.IsPlatformOwner() || principal.WorkspaceId() is not null
            || accessor.HttpContext?.GetRouteValue(RouteKey) is not string value
            || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || !await db.Workspaces.AnyAsync(w => w.Id == id))
        {
            return principal;
        }

        var visit = new ClaimsIdentity(
        [
            new Claim(AppClaimTypes.Workspace, id.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Role, RoleNames.Admin),
            new Claim(AppClaimTypes.Visiting, "true"),
        ]);

        var visiting = principal.Clone();
        visiting.AddIdentity(visit);
        return visiting;
    }

    /// <summary>
    /// The second address of the ticket list and the ticket page. It comes first when links are made, so a link
    /// made with a help desk goes there, and one made without falls through to the usual address.
    /// </summary>
    public static void AddRoutes(PageConventionCollection conventions)
    {
        Add(conventions, "/Tickets/Index", Prefix);
        Add(conventions, "/Tickets/Details", Prefix + "/{number:int}");
    }

    private static void Add(PageConventionCollection conventions, string page, string template)
    {
        conventions.AddPageRouteModelConvention(page, model => model.Selectors.Add(new SelectorModel
        {
            AttributeRouteModel = new AttributeRouteModel { Template = template, Order = -1 },
        }));

        conventions.AddPageApplicationModelConvention(page, model => model.Filters.Add(new OwnerOnly()));
    }

    /// <summary>Anybody but the owner who comes to these addresses finds nothing there.</summary>
    private sealed class OwnerOnly : IAsyncPageFilter
    {
        public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

        public Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
        {
            if (context.RouteData.Values.ContainsKey(RouteKey) && context.HttpContext.User.VisitedWorkspaceId() is null)
            {
                context.Result = new NotFoundResult();
                return Task.CompletedTask;
            }

            return next();
        }
    }
}
