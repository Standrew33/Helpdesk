using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace Helpdesk.Web.Features.Attachments;

public static class AttachmentStoreRegistration
{
    /// <summary>
    /// Registers the attachment store: Blob Storage when a connection string or account URL is configured
    /// (an account URL signs in with the app's managed identity), otherwise a local folder.
    /// </summary>
    public static IServiceCollection AddAttachmentStore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BlobStorageOptions>(configuration.GetSection(BlobStorageOptions.SectionName));

        services.AddSingleton<IAttachmentStore>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<BlobStorageOptions>>().Value;

            if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                var client = new BlobServiceClient(options.ConnectionString);
                return new BlobAttachmentStore(client.GetBlobContainerClient(options.Container));
            }

            if (!string.IsNullOrWhiteSpace(options.AccountUrl))
            {
                var client = new BlobServiceClient(new Uri(options.AccountUrl), new DefaultAzureCredential());
                return new BlobAttachmentStore(client.GetBlobContainerClient(options.Container));
            }

            var path = options.LocalPath
                ?? Path.Combine(sp.GetRequiredService<IHostEnvironment>().ContentRootPath, "App_Data", "attachments");
            return new LocalAttachmentStore(path);
        });

        return services;
    }
}
