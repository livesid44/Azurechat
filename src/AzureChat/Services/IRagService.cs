using AzureChat.Models;

namespace AzureChat.Services;

/// <summary>
/// Orchestrates Retrieval-Augmented Generation: retrieves relevant documents then
/// forwards the enriched prompt to the chat model.
/// </summary>
public interface IRagService
{
    /// <summary>Whether RAG document retrieval is currently active.</summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Processes a user <paramref name="question"/> against the running conversation
    /// <paramref name="history"/> and returns the assistant's response together with
    /// any source documents that were used.
    /// </summary>
    Task<RagResponse> AskAsync(
        string question,
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default);
}

/// <summary>The result returned by <see cref="IRagService.AskAsync"/>.</summary>
public sealed class RagResponse
{
    /// <summary>The assistant's answer.</summary>
    public string Answer { get; init; }

    /// <summary>Source documents used to generate the answer (empty when RAG is disabled).</summary>
    public IReadOnlyList<SearchResult> Sources { get; init; }

    public RagResponse(string answer, IReadOnlyList<SearchResult> sources)
    {
        Answer = answer;
        Sources = sources;
    }
}
