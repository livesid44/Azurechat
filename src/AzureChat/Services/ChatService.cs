using Azure;
using Azure.AI.OpenAI;
using AzureChat.Configuration;
using AzureChat.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using System.ClientModel;

namespace AzureChat.Services;

/// <summary>
/// Sends conversation history to Azure OpenAI and returns the assistant's reply.
/// Streaming is used so that the response can be rendered token-by-token in the UI.
/// </summary>
public sealed class ChatService : IChatService
{
    private readonly ChatClient _chatClient;
    private readonly AzureOpenAIOptions _options;
    private readonly ILogger<ChatService> _logger;

    public ChatService(IOptions<AzureOpenAIOptions> options, ILogger<ChatService> logger)
    {
        _options = options.Value;
        _logger = logger;

        AzureOpenAIClient azureClient = new(
            new Uri(_options.Endpoint),
            new ApiKeyCredential(_options.ApiKey));

        _chatClient = azureClient.GetChatClient(_options.DeploymentName);
    }

    /// <inheritdoc/>
    public async Task<string> GetCompletionAsync(
        IReadOnlyList<Models.ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<OpenAI.Chat.ChatMessage>(history.Count);

        foreach (var msg in history)
        {
            messages.Add(msg.Role switch
            {
                "system" => new SystemChatMessage(msg.Content),
                "assistant" => new AssistantChatMessage(msg.Content),
                _ => new UserChatMessage(msg.Content),
            });
        }

        var completionOptions = new ChatCompletionOptions
        {
            MaxOutputTokenCount = _options.MaxTokens,
            Temperature = _options.Temperature,
        };

        _logger.LogDebug("Requesting chat completion for {MessageCount} messages", messages.Count);

        ClientResult<ChatCompletion> result = await _chatClient.CompleteChatAsync(
            messages, completionOptions, cancellationToken);

        string reply = result.Value.Content[0].Text;
        _logger.LogDebug("Received completion of {Length} chars", reply.Length);
        return reply;
    }
}
