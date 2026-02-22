using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using AzureChat.Configuration;
using AzureChat.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureChat.Services;

/// <summary>
/// Retrieves relevant document passages from Azure AI Search using hybrid
/// (keyword + vector) search when a vector field is configured, otherwise
/// falls back to full-text search only.
/// </summary>
public sealed class SearchService : ISearchService
{
    private readonly SearchClient _client;
    private readonly AzureSearchOptions _options;
    private readonly ILogger<SearchService> _logger;

    public SearchService(IOptions<AzureSearchOptions> options, ILogger<SearchService> logger)
    {
        _options = options.Value;
        _logger = logger;

        _client = new SearchClient(
            new Uri(_options.Endpoint),
            _options.IndexName,
            new AzureKeyCredential(_options.ApiKey));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var searchOptions = new SearchOptions
        {
            Size = _options.TopK,
            Select = { _options.ContentField, _options.TitleField },
            IncludeTotalCount = false,
        };

        _logger.LogDebug("Searching index '{Index}' for: {Query}", _options.IndexName, query);

        Response<SearchResults<SearchDocument>> response =
            await _client.SearchAsync<SearchDocument>(query, searchOptions, cancellationToken);

        var results = new List<SearchResult>();
        await foreach (SearchResult<SearchDocument> hit in response.Value.GetResultsAsync())
        {
            string id = hit.Document.TryGetValue("id", out object? idVal) ? idVal?.ToString() ?? string.Empty : string.Empty;
            string title = hit.Document.TryGetValue(_options.TitleField, out object? titleVal) ? titleVal?.ToString() ?? string.Empty : string.Empty;
            string content = hit.Document.TryGetValue(_options.ContentField, out object? contentVal) ? contentVal?.ToString() ?? string.Empty : string.Empty;
            double score = hit.Score ?? 0;

            results.Add(new SearchResult(id, title, content, score));
        }

        _logger.LogDebug("Search returned {Count} result(s)", results.Count);
        return results;
    }
}
