namespace AzureChat.Configuration;

/// <summary>
/// Configuration options for Azure Blob Storage (document ingestion source).
/// </summary>
public sealed class BlobStorageOptions
{
    public const string SectionName = "BlobStorage";

    /// <summary>
    /// Azure Storage account connection string.
    /// Alternatively supply <see cref="AccountName"/> + <see cref="AccountKey"/>.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Storage account name (used when <see cref="ConnectionString"/> is empty).</summary>
    public string AccountName { get; set; } = string.Empty;

    /// <summary>Storage account key (used when <see cref="ConnectionString"/> is empty).</summary>
    public string AccountKey { get; set; } = string.Empty;

    /// <summary>Name of the blob container that holds source documents.</summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>
    /// Optional blob name prefix / virtual folder to restrict which blobs are ingested
    /// (e.g. "documents/"). Leave empty to ingest all blobs in the container.
    /// </summary>
    public string BlobPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Approximate maximum number of characters per content chunk.
    /// Smaller chunks improve embedding quality.  0 = do not chunk.
    /// </summary>
    public int ChunkSize { get; set; } = 2000;

    /// <summary>Number of overlapping characters between consecutive chunks.</summary>
    public int ChunkOverlap { get; set; } = 200;
}
