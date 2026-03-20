using AzureChat.Models;

namespace AzureChat.Services;

/// <summary>Writes <see cref="BlobDocument"/> records to Azure Cosmos DB.</summary>
public interface ICosmosDbService
{
    /// <summary>
    /// Upserts <paramref name="document"/> into the configured Cosmos DB container.
    /// Existing documents with the same <c>id</c> are replaced.
    /// </summary>
    Task UpsertDocumentAsync(BlobDocument document, CancellationToken cancellationToken = default);
}
