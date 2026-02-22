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
    public string PartitionKeyPath { get; set; } = "/sourceBlob";
}
