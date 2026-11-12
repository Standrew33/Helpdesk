using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Helpdesk.Web.Features.Attachments;

/// <summary>Keeps attachments in a private Azure Blob Storage container.</summary>
public sealed class BlobAttachmentStore(BlobContainerClient container) : IAttachmentStore
{
    private volatile bool _containerReady;

    public async Task SaveAsync(string name, Stream content, string contentType, CancellationToken ct = default)
    {
        await EnsureContainerAsync(ct);

        var options = new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } };
        await container.GetBlobClient(name).UploadAsync(content, options, ct);
    }

    public async Task<Stream?> OpenReadAsync(string name, CancellationToken ct = default)
    {
        try
        {
            return await container.GetBlobClient(name).OpenReadAsync(cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string name, CancellationToken ct = default)
        => await container.GetBlobClient(name).DeleteIfExistsAsync(cancellationToken: ct);

    private async Task EnsureContainerAsync(CancellationToken ct)
    {
        if (_containerReady)
        {
            return;
        }

        // Private container: no anonymous access. Creating an existing container is harmless, so no locking is needed.
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        _containerReady = true;
    }
}
