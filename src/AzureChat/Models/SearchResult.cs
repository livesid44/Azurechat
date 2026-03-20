namespace AzureChat.Models;

/// <summary>A single document passage retrieved from Azure AI Search.</summary>
public sealed class SearchResult
{
    /// <summary>Document or chunk identifier.</summary>
    public string Id { get; init; }

    /// <summary>Title or source reference for the document.</summary>
    public string Title { get; init; }

    /// <summary>The retrieved text passage.</summary>
    public string Content { get; init; }

    /// <summary>Relevance score returned by the search index.</summary>
    public double Score { get; init; }

    /// <summary>
    /// Blob path or URL for the source document — used to generate a download link.
    /// May be a plain blob name, a container-prefixed path, a full HTTPS URL,
    /// or a base64-encoded blob URL (Azure AI Search <c>metadata_storage_path</c> field).
    /// Empty when not available from the index.
    /// </summary>
    public string SourcePath { get; init; }

    public SearchResult(string id, string title, string content, double score, string sourcePath = "")
    {
        Id = id;
        Title = title;
        Content = content;
        Score = score;
        SourcePath = sourcePath;
    }
}
