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
    private readonly IOptionsMonitor<AzureSearchOptions> _monitor;
    private readonly ILogger<SearchService> _logger;

    // Lazily initialised — avoids URI exceptions when credentials are not yet configured.
    private SearchClient? _client;

    public SearchService(IOptionsMonitor<AzureSearchOptions> options, ILogger<SearchService> logger)
    {
        _monitor = options;
        _logger = logger;
        // Reset the cached client whenever configuration changes (e.g. after /api/settings save).
        _monitor.OnChange(_ => _client = null);
    }

    private AzureSearchOptions Options => _monitor.CurrentValue;

    private SearchClient GetClient() =>
        _client ??= new SearchClient(
            new Uri(Options.Endpoint),
            Options.IndexName,
            new AzureKeyCredential(Options.ApiKey));

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IndexField>> GetIndexFieldsAsync(
        CancellationToken cancellationToken = default)
    {
        var indexClient = new SearchIndexClient(
            new Uri(Options.Endpoint),
            new AzureKeyCredential(Options.ApiKey));

        Response<SearchIndex> response =
            await indexClient.GetIndexAsync(Options.IndexName, cancellationToken);

        var rawFields = response.Value.Fields.Select(f => new IndexField(
            name:          f.Name,
            type:          f.Type.ToString(),
            isSearchable:  f.IsSearchable ?? false,
            isRetrievable: f.IsHidden != true))   // IsHidden=true means NOT retrievable
            .ToList();

        _logger.LogDebug(
            "Index '{Index}' has {Count} field(s).", Options.IndexName, rawFields.Count);

        return IndexFieldRecommender.Annotate(rawFields);
    }

    // Ordered fallback aliases used when TryGetValue with the configured field name fails.
    // This lets search work even when the user hasn't updated appsettings.json yet.
    private static readonly string[] ContentAliases =
        ["chunk", "content", "text", "body", "description", "passage", "page_content"];

    private static readonly string[] TitleAliases =
        ["title", "name", "source", "filename", "filepath", "heading", "subject"];

    private static readonly string[] KeyAliases =
        ["id", "chunk_id", "document_id", "doc_id", "metadata_storage_path"];

    // Aliases for the blob source path — used to generate a download link for each result.
    // "source" is the standard field name in Azure AI Studio RAG indexes.
    // "sourceBlob" is used by the app's own ingestion pipeline.
    // "metadata_storage_path" is the base64-encoded blob URL emitted by the Azure AI Search indexer.
    private static readonly string[] SourcePathAliases =
        ["source", "sourceBlob", "metadata_storage_path", "filepath", "file_path", "url", "blobUrl"];

    /// <summary>
    /// Tries the configured field name first, then falls back through <paramref name="aliases"/>
    /// until a non-empty string value is found. Returns <see cref="string.Empty"/> if nothing matches.
    /// </summary>
    private static string TryExtractField(
        SearchDocument doc,
        string configuredField,
        string[] aliases)
    {
        // Try the explicitly configured field first.
        if (!string.IsNullOrWhiteSpace(configuredField) &&
            doc.TryGetValue(configuredField, out object? val) &&
            val?.ToString() is string s && s.Length > 0)
            return s;

        // Fall through the alias list in priority order.
        foreach (string alias in aliases)
        {
            if (alias == configuredField) continue; // already tried
            if (doc.TryGetValue(alias, out object? v) &&
                v?.ToString() is string a && a.Length > 0)
                return a;
        }

        return string.Empty;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        // Log active field configuration so stale settings are immediately visible in the console.
        _logger.LogInformation(
            "Search → index: '{Index}', KeyField: '{Key}', ContentField: '{Content}', TitleField: '{Title}'",
            Options.IndexName, Options.KeyField, Options.ContentField, Options.TitleField);

        // Do NOT populate searchOptions.Select — omitting $select tells Azure AI Search to
        // return all retrievable fields, which works with any index schema without any 400 errors.
        var searchOptions = new SearchOptions
        {
            Size = Options.TopK,
            IncludeTotalCount = false,
        };

        Response<SearchResults<SearchDocument>> response =
            await GetClient().SearchAsync<SearchDocument>(query, searchOptions, cancellationToken);

        var results = new List<SearchResult>();
        await foreach (SearchResult<SearchDocument> hit in response.Value.GetResultsAsync())
        {
            // Use configured field names with automatic fallback aliases so extraction
            // works even when appsettings.json hasn't been updated yet.
            string id         = TryExtractField(hit.Document, Options.KeyField,     KeyAliases);
            string title      = TryExtractField(hit.Document, Options.TitleField,   TitleAliases);
            string content    = TryExtractField(hit.Document, Options.ContentField, ContentAliases);
            string sourcePath = TryExtractField(hit.Document, string.Empty,          SourcePathAliases);
            double score      = hit.Score ?? 0;

            results.Add(new SearchResult(id, title, content, score, sourcePath));
        }

        _logger.LogDebug("Search returned {Count} result(s)", results.Count);
        return results;
    }
}
