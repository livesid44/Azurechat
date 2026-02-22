namespace AzureChat.Models;

/// <summary>Represents a single turn in the conversation history.</summary>
public sealed class ChatMessage
{
    /// <summary>Role of the message author: "system", "user", or "assistant".</summary>
    public string Role { get; init; }

    /// <summary>Text content of the message.</summary>
    public string Content { get; init; }

    public ChatMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }
}
