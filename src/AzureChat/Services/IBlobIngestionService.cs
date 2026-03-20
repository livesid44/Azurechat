using AzureChat.Models;

namespace AzureChat.Services;

/// <summary>
/// Reads blobs from Azure Blob Storage and converts them into <see cref="BlobDocument"/>
/// instances ready for storage in Cosmos DB.
/// </summary>
public interface IBlobIngestionService
{
    /// <summary>
    /// Enumerates all blobs in the configured container (and optional prefix),
    /// downloads their text content, chunks it, and yields one <see cref="BlobDocument"/>
    /// per blob.
    /// </summary>
    IAsyncEnumerable<BlobDocument> IngestBlobsAsync(CancellationToken cancellationToken = default);
}
