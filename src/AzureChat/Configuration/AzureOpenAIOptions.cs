namespace AzureChat.Configuration;

/// <summary>
/// Configuration options for Azure OpenAI service.
/// </summary>
public sealed class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";

    /// <summary>Azure OpenAI endpoint URL (e.g. https://&lt;resource&gt;.openai.azure.com/ or https://&lt;resource&gt;.cognitiveservices.azure.com/).</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Azure OpenAI credential value.
    /// When <see cref="AuthType"/> is <c>"ApiKey"</c> this is the API key shown under
    /// portal → OpenAI resource → Keys and Endpoint.
    /// When <see cref="AuthType"/> is <c>"Bearer"</c> this is an Entra ID access token
    /// obtained via <c>az account get-access-token --resource https://cognitiveservices.azure.com/</c>.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Authentication method.  Accepted values:
    /// <list type="bullet">
    ///   <item><c>"ApiKey"</c> (default) — sends <c>api-key</c> header; use the key from portal → Keys and Endpoint.</item>
    ///   <item><c>"Bearer"</c> — sends <c>Authorization: Bearer &lt;token&gt;</c>; paste an Entra ID access token into <see cref="ApiKey"/>.</item>
    /// </list>
    /// </summary>
    public string AuthType { get; set; } = "ApiKey";

    /// <summary>Name of the deployed chat model (e.g. gpt-4o).</summary>
    public string DeploymentName { get; set; } = "gpt-4o";

    /// <summary>Maximum tokens to generate in a single response.</summary>
    public int MaxTokens { get; set; } = 1024;

    /// <summary>Sampling temperature (0–2). Lower values are more deterministic.</summary>
    public float Temperature { get; set; } = 0.7f;
}
