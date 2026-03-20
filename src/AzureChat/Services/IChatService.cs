using AzureChat.Models;

namespace AzureChat.Services;

/// <summary>Abstraction over Azure OpenAI chat completions.</summary>
public interface IChatService
{
    /// <summary>
    /// Sends the conversation <paramref name="history"/> to the model and returns the assistant reply.
    /// </summary>
    Task<string> GetCompletionAsync(IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes a minimal 1-token test call and returns a diagnostic result without throwing.
    /// Use this to verify credentials and deployment name before a real chat request.
    /// </summary>
    Task<ChatPingResult> PingAsync(CancellationToken cancellationToken = default);
}

/// <summary>Result of a connection ping to Azure OpenAI.</summary>
public sealed record ChatPingResult(
    bool Ok,
    string AuthType,
    string Endpoint,
    string DeploymentName,
    string Message);
