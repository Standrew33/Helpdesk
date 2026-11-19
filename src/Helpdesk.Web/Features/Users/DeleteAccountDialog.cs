namespace Helpdesk.Web.Features.Users;

/// <summary>
/// The confirmation dialog a page with delete buttons shows once: how the page's Delete handler expects the account.
/// </summary>
/// <param name="IdField">The name of the form field that carries the account id.</param>
/// <param name="RouteValues">Kept in the address the form posts to, such as the search or the workspace.</param>
public sealed record DeleteAccountDialog(string IdField, IDictionary<string, string?> RouteValues);
