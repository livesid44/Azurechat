namespace AzureChat.Configuration;

/// <summary>
/// Configuration options for Azure Cosmos DB (document ingestion target).
/// </summary>
public sealed class CosmosDbOptions
{
    public const string SectionName = "CosmosDb";

    /// <summary>Cosmos DB account endpoint URL (e.g. https://&lt;account&gt;.documents.azure.com:443/).</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Cosmos DB account primary or secondary key.</summary>
    public string AccountKey { get; set; } = string.Empty;

    /// <summary>Name of the Cosmos DB database.</summary>
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>Name of the Cosmos DB container / collection that stores ingested documents.</summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>Partition key path (default: <c>/sourceBlob</c>).</summary>
    /// <remarks>
    /// When using an existing Cosmos DB container, set this to match the container's
    /// configured partition key path — e.g. <c>/vendorId</c>.
    /// For new containers (created by the app on first run) the default <c>/sourceBlob</c> works.
    /// </remarks>
    public string PartitionKeyPath { get; set; } = "/sourceBlob";

    /// <summary>
    /// Static value used as the partition key for every upserted document.
    /// When non-empty, this value is injected into the document JSON under the field
    /// derived from <see cref="PartitionKeyPath"/> (e.g. path <c>/vendorId</c> → field <c>vendorId</c>).
    /// </summary>
    /// <remarks>
    /// <para><b>New container (default)</b>: leave empty — the partition key is derived automatically
    /// from the document's <c>sourceBlob</c> field.</para>
    /// <para><b>Existing container with a fixed partition key</b>: set to any non-empty string
    /// (e.g. <c>"rag-docs"</c>) so all ingested documents land in the same partition.
    /// Make sure <see cref="PartitionKeyPath"/> matches the container's actual PK path.</para>
    /// </remarks>
    public string PartitionKeyValue { get; set; } = string.Empty;

    /// <summary>
    /// Derives the JSON field name from <see cref="PartitionKeyPath"/> by stripping the leading slash.
    /// E.g. <c>/vendorId</c> → <c>vendorId</c>.
    /// </summary>
    public string PartitionKeyField =>
        string.IsNullOrWhiteSpace(PartitionKeyPath)
            ? "sourceBlob"
            : PartitionKeyPath.TrimStart('/');
}
