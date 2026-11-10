namespace Helpdesk.Core;

/// <summary>Raised when an operation violates a business rule.</summary>
public sealed class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }
}
