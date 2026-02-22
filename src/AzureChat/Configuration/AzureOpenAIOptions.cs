namespace AzureChat.Configuration;

/// <summary>
/// Configuration options for Azure OpenAI service.
/// </summary>
public sealed class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";

    /// <summary>Azure OpenAI endpoint URL (e.g. https://&lt;resource&gt;.openai.azure.com/).</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Azure OpenAI API key.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Name of the deployed chat model (e.g. gpt-4o).</summary>
    public string DeploymentName { get; set; } = "gpt-4o";

    /// <summary>Maximum tokens to generate in a single response.</summary>
    public int MaxTokens { get; set; } = 1024;

    /// <summary>Sampling temperature (0–2). Lower values are more deterministic.</summary>
    public float Temperature { get; set; } = 0.7f;
}
