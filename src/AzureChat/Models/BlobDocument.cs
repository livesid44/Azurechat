using System.Text.Json.Serialization;

namespace AzureChat.Models;

/// <summary>
/// A document stored in Azure Cosmos DB that was ingested from Azure Blob Storage.
/// The structure is designed to be indexed by Azure AI Search for RAG retrieval.
/// </summary>
public sealed class BlobDocument
{
    /// <summary>Unique document identifier (stable: derived from container + blob name).</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; }

    /// <summary>Partition key value — the full blob path (container/blobName).</summary>
    [JsonPropertyName("sourceBlob")]
    public string SourceBlob { get; init; }

    /// <summary>Human-readable title, typically the blob file name without extension.</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; }

    /// <summary>Full extracted text content of the source document.</summary>
    [JsonPropertyName("content")]
    public string Content { get; init; }

    /// <summary>Text broken into overlapping chunks suitable for vector embedding.</summary>
    [JsonPropertyName("chunks")]
    public IReadOnlyList<DocumentChunk> Chunks { get; init; }

    /// <summary>Additional metadata harvested from the blob.</summary>
    [JsonPropertyName("metadata")]
    public BlobMetadata Metadata { get; init; }

    /// <summary>UTC timestamp when this document was ingested.</summary>
    [JsonPropertyName("ingestedAt")]
    public DateTimeOffset IngestedAt { get; init; }

    public BlobDocument(
        string id,
        string sourceBlob,
        string title,
        string content,
        IReadOnlyList<DocumentChunk> chunks,
        BlobMetadata metadata,
        DateTimeOffset ingestedAt)
    {
        Id = id;
        SourceBlob = sourceBlob;
        Title = title;
        Content = content;
        Chunks = chunks;
        Metadata = metadata;
        IngestedAt = ingestedAt;
    }
}

/// <summary>A single text chunk of a <see cref="BlobDocument"/>.</summary>
public sealed class DocumentChunk
{
    /// <summary>Zero-based index of this chunk within its parent document.</summary>
    [JsonPropertyName("chunkIndex")]
    public int ChunkIndex { get; init; }

    /// <summary>Text content of the chunk.</summary>
    [JsonPropertyName("content")]
    public string Content { get; init; }

    public DocumentChunk(int chunkIndex, string content)
    {
        ChunkIndex = chunkIndex;
        Content = content;
    }
}

/// <summary>Metadata harvested from the source Azure Blob.</summary>
public sealed class BlobMetadata
{
    [JsonPropertyName("containerName")]
    public string ContainerName { get; init; }

    [JsonPropertyName("blobName")]
    public string BlobName { get; init; }

    [JsonPropertyName("contentType")]
    public string ContentType { get; init; }

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("lastModified")]
    public DateTimeOffset? LastModified { get; init; }

    public BlobMetadata(
        string containerName,
        string blobName,
        string contentType,
        long size,
        DateTimeOffset? lastModified)
    {
        ContainerName = containerName;
        BlobName = blobName;
        ContentType = contentType;
        Size = size;
        LastModified = lastModified;
    }
}
