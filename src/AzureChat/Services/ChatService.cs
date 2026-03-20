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
public sealed class ChatService : IChatService, IDisposable
{
    private readonly IOptionsMonitor<AzureOpenAIOptions> _monitor;
    private readonly ILogger<ChatService> _logger;
    // Keep the IDisposable returned by OnChange alive for the service lifetime so the
    // callback is never garbage-collected and _chatClient is always reset on config change.
    private readonly IDisposable? _changeToken;

    // Lazily initialised — avoids URI exceptions when credentials are not yet configured.
    private ChatClient? _chatClient;

    public ChatService(IOptionsMonitor<AzureOpenAIOptions> options, ILogger<ChatService> logger)
    {
        _monitor = options;
        _logger = logger;
        // Reset the cached client whenever configuration changes (e.g. after /api/settings save).
        _changeToken = _monitor.OnChange(_ => _chatClient = null);
    }

    public void Dispose() => _changeToken?.Dispose();

    private AzureOpenAIOptions Options => _monitor.CurrentValue;

    private ChatClient GetChatClient()
    {
        if (_chatClient is not null) return _chatClient;

        _logger.LogInformation(
            "Creating ChatClient → AuthType: '{AuthType}', endpoint: '{Endpoint}', deployment: '{Deployment}'",
            Options.AuthType, Options.Endpoint, Options.DeploymentName);

        AzureOpenAIClient azureClient = Options.AuthType.Equals("Bearer", StringComparison.OrdinalIgnoreCase)
            ? new AzureOpenAIClient(new Uri(Options.Endpoint), new StaticBearerTokenCredential(Options.ApiKey))
            : new AzureOpenAIClient(new Uri(Options.Endpoint), new ApiKeyCredential(Options.ApiKey));

        _chatClient = azureClient.GetChatClient(Options.DeploymentName);
        return _chatClient;
    }

    /// <summary>
    /// Makes a minimal 1-token test call and returns a diagnostic result indicating
    /// exactly what went wrong without throwing.
    /// </summary>
    public async Task<ChatPingResult> PingAsync(CancellationToken cancellationToken = default)
    {
        var opts = Options;
        _logger.LogInformation(
            "Ping → AuthType: '{AuthType}', endpoint: '{Endpoint}', deployment: '{Deployment}'",
            opts.AuthType, opts.Endpoint, opts.DeploymentName);

        if (string.IsNullOrWhiteSpace(opts.Endpoint))
            return new ChatPingResult(false, opts.AuthType, opts.Endpoint, opts.DeploymentName,
                "Endpoint is not configured. Set AzureOpenAI__Endpoint in appsettings.json.");

        if (string.IsNullOrWhiteSpace(opts.ApiKey))
            return new ChatPingResult(false, opts.AuthType, opts.Endpoint, opts.DeploymentName,
                "ApiKey / Bearer token is not configured. Set AzureOpenAI__ApiKey in appsettings.json.");

        if (string.IsNullOrWhiteSpace(opts.DeploymentName))
            return new ChatPingResult(false, opts.AuthType, opts.Endpoint, opts.DeploymentName,
                "DeploymentName is not configured. Set AzureOpenAI__DeploymentName in appsettings.json.");

        try
        {
            // Minimal call: single user message, 1 output token.
            var pingOptions = new ChatCompletionOptions { MaxOutputTokenCount = 1 };
            ClientResult<ChatCompletion> result = await GetChatClient().CompleteChatAsync(
                [new UserChatMessage("ping")], pingOptions, cancellationToken);

            string reply = result.Value.Content.Count > 0 ? result.Value.Content[0].Text : "(empty)";
            return new ChatPingResult(true, opts.AuthType, opts.Endpoint, opts.DeploymentName,
                $"OK — model replied: \"{reply}\"");
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            _chatClient = null; // reset so corrected config is picked up
            return new ChatPingResult(false, opts.AuthType, opts.Endpoint, opts.DeploymentName,
                $"HTTP 404 — Deployment '{opts.DeploymentName}' was NOT found. " +
                "Check the exact deployment name in Azure portal → your OpenAI resource → Deployments.");
        }
        catch (ClientResultException ex) when (ex.Status == 401)
        {
            _chatClient = null;
            return new ChatPingResult(false, opts.AuthType, opts.Endpoint, opts.DeploymentName,
                $"HTTP 401 — Authentication failed (AuthType={opts.AuthType}). " +
                (opts.AuthType.Equals("Bearer", StringComparison.OrdinalIgnoreCase)
                    ? "Bearer token may be expired. Get a new one with: az account get-access-token --resource https://cognitiveservices.azure.com/ --query accessToken -o tsv   then paste it into the Credential field in the settings editor and Save."
                    : "API key is invalid. Check portal → OpenAI resource → Keys and Endpoint."));
        }
        catch (ClientResultException ex) when (ex.Status == 403)
        {
            _chatClient = null;
            return new ChatPingResult(false, opts.AuthType, opts.Endpoint, opts.DeploymentName,
                $"HTTP 403 — Access denied. Your key/token may lack permission to call this resource.");
        }
        catch (Exception ex)
        {
            _chatClient = null;
            return new ChatPingResult(false, opts.AuthType, opts.Endpoint, opts.DeploymentName,
                $"Error ({ex.GetType().Name}): {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<string> GetCompletionAsync(
        IReadOnlyList<Models.ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        // Log active deployment and auth type so a wrong config is immediately visible in the console.
        _logger.LogInformation(
            "Chat completion → AuthType: '{AuthType}', endpoint: '{Endpoint}', deployment: '{Deployment}'",
            Options.AuthType, Options.Endpoint, Options.DeploymentName);

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
            MaxOutputTokenCount = Options.MaxTokens,
            Temperature = Options.Temperature,
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
            // Reset the cached client so the corrected deployment name is picked up immediately.
            _chatClient = null;

            throw new AzureDeploymentNotFoundException(
                Options.DeploymentName,
                $"Azure OpenAI deployment '{Options.DeploymentName}' was not found (HTTP 404). " +
                "Check that the DeploymentName in appsettings.json exactly matches the deployment " +
                "name shown in Azure portal → your Azure OpenAI resource → Deployments. " +
                $"Set it via: AzureOpenAI__DeploymentName=<your-deployment-name>",
                ex);
        }
    }
}
