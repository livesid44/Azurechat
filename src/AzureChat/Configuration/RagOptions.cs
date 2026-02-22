namespace AzureChat.Configuration;

/// <summary>
/// Runtime configuration for Retrieval-Augmented Generation behaviour.
/// </summary>
public sealed class RagOptions
{
    public const string SectionName = "Rag";

    /// <summary>Whether RAG is enabled by default when the application starts.</summary>
    public bool EnabledByDefault { get; set; } = true;

    /// <summary>System prompt prepended to every conversation.</summary>
    public string SystemPrompt { get; set; } =
        "You are a helpful assistant. When context documents are provided, use them to answer the user's question accurately.";

    /// <summary>
    /// Template used to inject retrieved documents into the user message.
    /// Use {context} as the placeholder for the retrieved passages and
    /// {question} for the user question.
    /// </summary>
    public string ContextTemplate { get; set; } =
        "Use the following context to answer the question.\n\n---\n{context}\n---\n\nQuestion: {question}";
}
