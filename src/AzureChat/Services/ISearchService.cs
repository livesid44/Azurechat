using AzureChat.Models;

namespace AzureChat.Services;

/// <summary>Abstraction over Azure AI Search for document retrieval.</summary>
public interface ISearchService
{
    /// <summary>
    /// Retrieves the top-K document passages most relevant to <paramref name="query"/>.
    /// </summary>
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
