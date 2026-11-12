namespace Helpdesk.Web.Features.Attachments;

/// <summary>Where attachment content is kept. Ticket data only stores the name under which a file was saved.</summary>
public interface IAttachmentStore
{
    Task SaveAsync(string name, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>Opens the stored file, or returns null when it does not exist.</summary>
    Task<Stream?> OpenReadAsync(string name, CancellationToken ct = default);

    Task DeleteAsync(string name, CancellationToken ct = default);
}
