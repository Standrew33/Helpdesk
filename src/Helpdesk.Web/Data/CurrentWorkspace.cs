using Helpdesk.Web.Security;

namespace Helpdesk.Web.Data;

/// <summary>The workspace of the user the current request is made by; null outside a request or when signed out.</summary>
public sealed class CurrentWorkspace(IHttpContextAccessor accessor)
{
    public int? Id => accessor.HttpContext?.User.WorkspaceId();
}
