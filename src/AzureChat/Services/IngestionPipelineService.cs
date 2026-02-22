using AzureChat.Models;
using Microsoft.Extensions.Logging;

namespace AzureChat.Services;

/// <summary>
/// Orchestrates the end-to-end ingestion pipeline:
/// Azure Blob Storage → <see cref="BlobDocument"/> JSON → Azure Cosmos DB.
/// </summary>
public sealed class IngestionPipelineService : IIngestionPipelineService
{
    private readonly IBlobIngestionService _blobIngestion;
    private readonly ICosmosDbService _cosmosDb;
    private readonly ILogger<IngestionPipelineService> _logger;

    public IngestionPipelineService(
        IBlobIngestionService blobIngestion,
        ICosmosDbService cosmosDb,
        ILogger<IngestionPipelineService> logger)
    {
        _blobIngestion = blobIngestion;
        _cosmosDb = cosmosDb;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IngestionSummary> RunAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Ingestion pipeline started.");

        int succeeded = 0;
        int failed = 0;
        var errors = new List<string>();

        await foreach (BlobDocument doc in _blobIngestion.IngestBlobsAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _cosmosDb.UpsertDocumentAsync(doc, cancellationToken);
                succeeded++;
                _logger.LogInformation(
                    "✔ Ingested '{SourceBlob}' ({ChunkCount} chunk(s))",
                    doc.SourceBlob, doc.Chunks.Count);
            }
            catch (Exception ex)
            {
                failed++;
                string error = $"Failed to upsert '{doc.SourceBlob}': {ex.Message}";
                errors.Add(error);
                _logger.LogError(ex, "Failed to upsert document '{SourceBlob}'", doc.SourceBlob);
            }
        }

        _logger.LogInformation(
            "Ingestion pipeline complete — succeeded: {Succeeded}, failed: {Failed}",
            succeeded, failed);

        return new IngestionSummary(succeeded, failed, errors);
    }
}
