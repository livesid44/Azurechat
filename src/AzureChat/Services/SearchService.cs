using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
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
    private readonly AzureSearchOptions _options;
    private readonly ILogger<SearchService> _logger;

    // Lazily initialised — avoids URI exceptions when credentials are not yet configured.
    private SearchClient? _client;

    public SearchService(IOptions<AzureSearchOptions> options, ILogger<SearchService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    private SearchClient GetClient() =>
        _client ??= new SearchClient(
            new Uri(_options.Endpoint),
            _options.IndexName,
            new AzureKeyCredential(_options.ApiKey));

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IndexField>> GetIndexFieldsAsync(
        CancellationToken cancellationToken = default)
    {
        var indexClient = new SearchIndexClient(
            new Uri(_options.Endpoint),
            new AzureKeyCredential(_options.ApiKey));

        Response<SearchIndex> response =
            await indexClient.GetIndexAsync(_options.IndexName, cancellationToken);

        var rawFields = response.Value.Fields.Select(f => new IndexField(
            name:          f.Name,
            type:          f.Type.ToString(),
            isSearchable:  f.IsSearchable ?? false,
            isRetrievable: f.IsHidden != true))   // IsHidden=true means NOT retrievable
            .ToList();

        _logger.LogDebug(
            "Index '{Index}' has {Count} field(s).", _options.IndexName, rawFields.Count);

        return IndexFieldRecommender.Annotate(rawFields);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var searchOptions = new SearchOptions
        {
            Size = _options.TopK,
            IncludeTotalCount = false,
        };

        // Only add field names to $select that are explicitly configured and non-empty.
        // Omitting $select entirely causes Azure AI Search to return all fields, which
        // avoids a 400 "property not found" error when the index schema uses different
        // field names than the defaults.
        var fieldsToSelect = new[] { "id", _options.ContentField, _options.TitleField }
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct()
            .ToList();

        foreach (string field in fieldsToSelect)
            searchOptions.Select.Add(field);

        _logger.LogDebug("Searching index '{Index}' for: {Query}", _options.IndexName, query);

        Response<SearchResults<SearchDocument>> response =
            await GetClient().SearchAsync<SearchDocument>(query, searchOptions, cancellationToken);

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
