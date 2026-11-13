using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
[AllowAnonymous]
public class ErrorModel : PageModel
{
    public string? RequestId { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    /// <summary>The status code that brought the visitor here; 500 for unhandled errors.</summary>
    public int Code { get; private set; } = 500;

    public string Heading => Code switch
    {
        404 => "Page not found",
        403 => "Access denied",
        _ => "Something went wrong",
    };

    public string Message => Code switch
    {
        404 => "The page does not exist, or you do not have access to it.",
        403 => "Your account is not allowed to open this page.",
        _ => "An error occurred while processing your request. Please try again.",
    };

    public void OnGet(int? code) => Describe(code);

    // A refused form post is re-executed with its original method, so the page answers those too.
    public void OnPost(int? code) => Describe(code);

    private void Describe(int? code)
    {
        Code = code is >= 400 and <= 599 ? code.Value : 500;
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }
}
