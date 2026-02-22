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

    public SearchResult(string id, string title, string content, double score)
    {
        Id = id;
        Title = title;
        Content = content;
        Score = score;
    }
}
