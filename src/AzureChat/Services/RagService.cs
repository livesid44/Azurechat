using AzureChat.Configuration;
using AzureChat.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureChat.Services;

/// <summary>
/// Orchestrates Retrieval-Augmented Generation by optionally retrieving documents
/// from <see cref="ISearchService"/> and then asking <see cref="IChatService"/>
/// to generate a grounded response.
/// </summary>
public sealed class RagService : IRagService
{
    private readonly ISearchService _searchService;
    private readonly IChatService _chatService;
    private readonly RagOptions _options;
    private readonly ILogger<RagService> _logger;

    /// <inheritdoc/>
    public bool IsEnabled { get; set; }

    public RagService(
        ISearchService searchService,
        IChatService chatService,
        IOptions<RagOptions> options,
        ILogger<RagService> logger)
    {
        _searchService = searchService;
        _chatService = chatService;
        _options = options.Value;
        _logger = logger;
        IsEnabled = _options.EnabledByDefault;
    }

    /// <inheritdoc/>
    public async Task<RagResponse> AskAsync(
        string question,
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SearchResult> sources = [];

        // Build message list: start with system prompt then append history.
        var messages = new List<ChatMessage>
        {
            new("system", _options.SystemPrompt),
        };
        messages.AddRange(history);

        string userContent = question;

        if (IsEnabled)
        {
            sources = await _searchService.SearchAsync(question, cancellationToken);

            if (sources.Count > 0)
            {
                string context = BuildContext(sources);
                userContent = _options.ContextTemplate
                    .Replace("{context}", context, StringComparison.OrdinalIgnoreCase)
                    .Replace("{question}", question, StringComparison.OrdinalIgnoreCase);

                _logger.LogDebug("RAG: augmented prompt with {Count} source(s)", sources.Count);
            }
            else
            {
                _logger.LogDebug("RAG: no sources found, using bare question");
            }
        }

        messages.Add(new ChatMessage("user", userContent));

        string answer = await _chatService.GetCompletionAsync(messages, cancellationToken);
        return new RagResponse(answer, sources);
    }

    private static string BuildContext(IReadOnlyList<SearchResult> sources)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < sources.Count; i++)
        {
            SearchResult src = sources[i];
            if (!string.IsNullOrWhiteSpace(src.Title))
                sb.AppendLine($"[{i + 1}] {src.Title}");
            sb.AppendLine(src.Content);
            if (i < sources.Count - 1)
                sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}
