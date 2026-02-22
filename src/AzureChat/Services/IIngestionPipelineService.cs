namespace AzureChat.Services;

/// <summary>
/// Orchestrates the end-to-end ingestion pipeline:
/// Azure Blob Storage → JSON document generation → Azure Cosmos DB.
/// </summary>
public interface IIngestionPipelineService
{
    /// <summary>
    /// Reads all eligible blobs, converts them to <c>BlobDocument</c> JSON records,
    /// and upserts them into Cosmos DB.
    /// </summary>
    /// <returns>A summary of the ingestion run.</returns>
    Task<IngestionSummary> RunAsync(CancellationToken cancellationToken = default);
}

/// <summary>Summary of a completed ingestion pipeline run.</summary>
public sealed class IngestionSummary
{
    /// <summary>Number of blobs successfully processed and written to Cosmos DB.</summary>
    public int Succeeded { get; init; }

    /// <summary>Number of blobs that failed during processing.</summary>
    public int Failed { get; init; }

    /// <summary>Individual error messages for failed blobs.</summary>
    public IReadOnlyList<string> Errors { get; init; }

    public IngestionSummary(int succeeded, int failed, IReadOnlyList<string> errors)
    {
        Succeeded = succeeded;
        Failed = failed;
        Errors = errors;
    }
}
