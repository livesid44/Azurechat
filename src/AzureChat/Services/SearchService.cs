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
        // Log active configuration once per search so a stale/wrong config is immediately
        // visible in the console without needing to look at appsettings.json.
        _logger.LogInformation(
            "Search → index: '{Index}', KeyField: '{Key}', ContentField: '{Content}', TitleField: '{Title}'",
            _options.IndexName, _options.KeyField, _options.ContentField, _options.TitleField);

        var searchOptions = new SearchOptions
        {
            Size = _options.TopK,
            IncludeTotalCount = false,
        };

        // Only add field names to $select that are explicitly configured and non-empty.
        var fieldsToSelect = new[] { _options.KeyField, _options.ContentField, _options.TitleField }
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct()
            .ToList();

        foreach (string field in fieldsToSelect)
            searchOptions.Select.Add(field);

        Response<SearchResults<SearchDocument>> response;
        try
        {
            response = await GetClient().SearchAsync<SearchDocument>(query, searchOptions, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 400)
        {
            // Azure AI Search returns 400 when a field in $select doesn't exist in the index.
            // Recover by retrying without $select (returns all fields) so the user can still chat,
            // and log a clear message that tells them exactly how to fix the config.
            _logger.LogWarning(
                ex,
                "Azure AI Search returned 400 — one or more $select fields [{Fields}] do not exist " +
                "in index '{Index}'. Retrying without $select (all fields returned). " +
                "Fix: open the ⚙ config panel → 'Discover index fields' and update " +
                "ContentField / TitleField / KeyField in appsettings.json.",
                string.Join(", ", fieldsToSelect),
                _options.IndexName);

            searchOptions.Select.Clear();
            response = await GetClient().SearchAsync<SearchDocument>(query, searchOptions, cancellationToken);
        }

        var results = new List<SearchResult>();
        await foreach (SearchResult<SearchDocument> hit in response.Value.GetResultsAsync())
        {
            string id      = hit.Document.TryGetValue(_options.KeyField,      out object? idVal)      ? idVal?.ToString()      ?? string.Empty : string.Empty;
            string title   = hit.Document.TryGetValue(_options.TitleField,    out object? titleVal)   ? titleVal?.ToString()   ?? string.Empty : string.Empty;
            string content = hit.Document.TryGetValue(_options.ContentField,  out object? contentVal) ? contentVal?.ToString() ?? string.Empty : string.Empty;
            double score   = hit.Score ?? 0;

            results.Add(new SearchResult(id, title, content, score));
        }

        _logger.LogDebug("Search returned {Count} result(s)", results.Count);
        return results;
    }
}
