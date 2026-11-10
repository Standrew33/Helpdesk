namespace Helpdesk.Web.Features.Tickets;

public enum ServiceOutcome
{
    Ok,
    NotFound,
    Invalid,
    Conflict,
}

public sealed record ServiceResult(ServiceOutcome Outcome, string? Message = null)
{
    public bool Succeeded => Outcome == ServiceOutcome.Ok;

    public static ServiceResult Success() => new(ServiceOutcome.Ok);

    public static ServiceResult NotFound() => new(ServiceOutcome.NotFound);

    public static ServiceResult Invalid(string message) => new(ServiceOutcome.Invalid, message);

    public static ServiceResult Conflict(string message) => new(ServiceOutcome.Conflict, message);
}
