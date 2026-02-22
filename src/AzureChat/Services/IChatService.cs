using AzureChat.Models;

namespace AzureChat.Services;

/// <summary>Abstraction over Azure OpenAI chat completions.</summary>
public interface IChatService
{
    /// <summary>
    /// Sends the conversation <paramref name="history"/> to the model and returns the assistant reply.
    /// </summary>
    Task<string> GetCompletionAsync(IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken = default);
}
