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
    private readonly AzureOpenAIOptions _options;
    private readonly ILogger<ChatService> _logger;

    // Lazily initialised — avoids URI exceptions when credentials are not yet configured.
    private ChatClient? _chatClient;

    public ChatService(IOptions<AzureOpenAIOptions> options, ILogger<ChatService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    private ChatClient GetChatClient()
    {
        if (_chatClient is not null) return _chatClient;
        AzureOpenAIClient azureClient = new(
            new Uri(_options.Endpoint),
            new ApiKeyCredential(_options.ApiKey));
        _chatClient = azureClient.GetChatClient(_options.DeploymentName);
        return _chatClient;
    }

    /// <inheritdoc/>
    public async Task<string> GetCompletionAsync(
        IReadOnlyList<Models.ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        // Log active deployment so a wrong name is immediately visible in the console.
        _logger.LogInformation(
            "Chat completion → endpoint: '{Endpoint}', deployment: '{Deployment}'",
            _options.Endpoint, _options.DeploymentName);

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

        try
        {
            ClientResult<ChatCompletion> result = await GetChatClient().CompleteChatAsync(
                messages, completionOptions, cancellationToken);

            string reply = result.Value.Content[0].Text;
            _logger.LogDebug("Received completion of {Length} chars", reply.Length);
            return reply;
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            // Azure OpenAI returns 404 when the deployment name does not exist.
            // Reset the cached client so a corrected deployment name (set via env var + restart)
            // will create a fresh client on the next request.
            _chatClient = null;

            throw new AzureDeploymentNotFoundException(
                _options.DeploymentName,
                $"Azure OpenAI deployment '{_options.DeploymentName}' was not found (HTTP 404). " +
                "Check that the DeploymentName in appsettings.json exactly matches the deployment " +
                "name shown in Azure portal → your Azure OpenAI resource → Deployments. " +
                $"Set it via: AzureOpenAI__DeploymentName=<your-deployment-name>",
                ex);
        }
    }
}
