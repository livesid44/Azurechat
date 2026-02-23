namespace AzureChat.Configuration;

/// <summary>
/// Configuration options for Azure AI Search (vector RAG).
/// </summary>
public sealed class AzureSearchOptions
{
    public const string SectionName = "AzureSearch";

    /// <summary>Azure AI Search service endpoint (e.g. https://&lt;resource&gt;.search.windows.net).</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Azure AI Search admin or query API key.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Name of the search index that holds document embeddings.</summary>
    public string IndexName { get; set; } = string.Empty;

    /// <summary>
    /// Name of the field that contains the document text content.
    /// Azure AI Studio indexes typically use "chunk"; custom indexes may use "content" or another name.
    /// Use the ⚙ config panel → "Discover index fields" button to find the correct value.
    /// </summary>
    public string ContentField { get; set; } = "chunk";

    /// <summary>Name of the field that contains the vector embedding.</summary>
    public string VectorField { get; set; } = "contentVector";

    /// <summary>Name of the field used as the document title / source reference.</summary>
    public string TitleField { get; set; } = "title";

    /// <summary>Number of nearest-neighbour results to retrieve from the vector search.</summary>
    public int TopK { get; set; } = 3;
}
