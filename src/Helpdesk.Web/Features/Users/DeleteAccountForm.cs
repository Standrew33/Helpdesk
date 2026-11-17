namespace Helpdesk.Web.Features.Users;

/// <summary>What the delete button of an account needs: whom it deletes and how the page's Delete handler expects it.</summary>
/// <param name="IdField">The name of the form field that carries the account id.</param>
/// <param name="RouteValues">Kept in the address the form posts to, such as the search or the help desk.</param>
public sealed record DeleteAccountForm(string UserId, string Name, string IdField, IDictionary<string, string?> RouteValues);
