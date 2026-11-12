namespace Helpdesk.Web.Features.Attachments;

public class BlobStorageOptions
{
    public const string SectionName = "BlobStorage";

    /// <summary>Address of the storage account, for example https://account.blob.core.windows.net. Uses the app's managed identity.</summary>
    public string? AccountUrl { get; set; }

    /// <summary>Connection string, for local emulators or when managed identity is not available. Takes precedence over <see cref="AccountUrl"/>.</summary>
    public string? ConnectionString { get; set; }

    public string Container { get; set; } = "attachments";

    /// <summary>Folder used when no Blob Storage is configured (development and tests).</summary>
    public string? LocalPath { get; set; }
}
