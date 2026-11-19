namespace Helpdesk.Web.Features.Users;

/// <summary>The delete button of one account: whom the confirmation dialog will ask about.</summary>
public sealed record DeleteAccountButton(string UserId, string Name);
