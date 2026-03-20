using AzureChat.Models;

namespace AzureChat.Services;

/// <summary>Abstraction over Azure AI Search for document retrieval.</summary>
public interface ISearchService
{
    /// <summary>
    /// Retrieves the top-K document passages most relevant to <paramref name="query"/>.
    /// </summary>
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the list of fields defined in the configured search index, annotated
    /// with heuristic role suggestions ("content" / "title") to help users identify
    /// the correct <c>ContentField</c> and <c>TitleField</c> configuration values.
    /// </summary>
    Task<IReadOnlyList<IndexField>> GetIndexFieldsAsync(CancellationToken cancellationToken = default);
}
