using AzureChat.Configuration;
using AzureChat.Models;
using AzureChat.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AzureChat.Tests.Services;

public class RagServiceTests
{
    private static IOptions<RagOptions> DefaultRagOptions(bool enabledByDefault = true) =>
        Options.Create(new RagOptions
        {
            EnabledByDefault = enabledByDefault,
            SystemPrompt = "You are a helpful assistant.",
            ContextTemplate = "Context:\n{context}\n\nQuestion: {question}",
        });

    private static RagService BuildService(
        ISearchService? search = null,
        IChatService? chat = null,
        IOptions<RagOptions>? opts = null)
    {
        search ??= Mock.Of<ISearchService>(s =>
            s.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()) ==
            Task.FromResult<IReadOnlyList<SearchResult>>(Array.Empty<SearchResult>()));

        chat ??= Mock.Of<IChatService>(c =>
            c.GetCompletionAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<CancellationToken>()) ==
            Task.FromResult("Mock answer"));

        return new RagService(search, chat, opts ?? DefaultRagOptions(), NullLogger<RagService>.Instance);
    }

    [Fact]
    public void IsEnabled_DefaultsFromOptions_WhenTrue()
    {
        var svc = BuildService(opts: DefaultRagOptions(enabledByDefault: true));
        Assert.True(svc.IsEnabled);
    }

    [Fact]
    public void IsEnabled_DefaultsFromOptions_WhenFalse()
    {
        var svc = BuildService(opts: DefaultRagOptions(enabledByDefault: false));
        Assert.False(svc.IsEnabled);
    }

    [Fact]
    public void IsEnabled_CanBeToggled()
    {
        var svc = BuildService();
        svc.IsEnabled = false;
        Assert.False(svc.IsEnabled);
        svc.IsEnabled = true;
        Assert.True(svc.IsEnabled);
    }

    [Fact]
    public async Task AskAsync_WhenRagDisabled_DoesNotCallSearch()
    {
        var searchMock = new Mock<ISearchService>();
        var chatMock = new Mock<IChatService>();
        chatMock.Setup(c => c.GetCompletionAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("Answer without RAG");

        var svc = BuildService(searchMock.Object, chatMock.Object, DefaultRagOptions(false));

        RagResponse result = await svc.AskAsync("What is Azure?", [], CancellationToken.None);

        searchMock.Verify(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("Answer without RAG", result.Answer);
        Assert.Empty(result.Sources);
    }

    [Fact]
    public async Task AskAsync_WhenRagEnabled_CallsSearchAndInjectsContext()
    {
        var sources = new List<SearchResult>
        {
            new("1", "Doc A", "Azure is a cloud platform.", 0.9),
        };

        var searchMock = new Mock<ISearchService>();
        searchMock.Setup(s => s.SearchAsync("What is Azure?", It.IsAny<CancellationToken>()))
                  .ReturnsAsync((IReadOnlyList<SearchResult>)sources);

        string? capturedUserContent = null;
        var chatMock = new Mock<IChatService>();
        chatMock.Setup(c => c.GetCompletionAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyList<ChatMessage>, CancellationToken>((msgs, _) =>
                {
                    capturedUserContent = msgs.Last().Content;
                })
                .ReturnsAsync("Azure is Microsoft's cloud.");

        var svc = BuildService(searchMock.Object, chatMock.Object, DefaultRagOptions(true));

        RagResponse result = await svc.AskAsync("What is Azure?", [], CancellationToken.None);

        searchMock.Verify(s => s.SearchAsync("What is Azure?", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Azure is Microsoft's cloud.", result.Answer);
        Assert.Single(result.Sources);
        Assert.NotNull(capturedUserContent);
        Assert.Contains("Azure is a cloud platform.", capturedUserContent);
        Assert.Contains("What is Azure?", capturedUserContent);
    }

    [Fact]
    public async Task AskAsync_WhenRagEnabledButNoSources_UsesBareQuestion()
    {
        var searchMock = new Mock<ISearchService>();
        searchMock.Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((IReadOnlyList<SearchResult>)Array.Empty<SearchResult>());

        string? capturedUserContent = null;
        var chatMock = new Mock<IChatService>();
        chatMock.Setup(c => c.GetCompletionAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyList<ChatMessage>, CancellationToken>((msgs, _) =>
                {
                    capturedUserContent = msgs.Last().Content;
                })
                .ReturnsAsync("I don't know");

        var svc = BuildService(searchMock.Object, chatMock.Object, DefaultRagOptions(true));

        await svc.AskAsync("Tell me a secret", [], CancellationToken.None);

        Assert.Equal("Tell me a secret", capturedUserContent);
    }

    [Fact]
    public async Task AskAsync_IncludesSystemPromptAndHistory()
    {
        List<ChatMessage>? capturedMessages = null;
        var chatMock = new Mock<IChatService>();
        chatMock.Setup(c => c.GetCompletionAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyList<ChatMessage>, CancellationToken>((msgs, _) =>
                {
                    capturedMessages = msgs.ToList();
                })
                .ReturnsAsync("OK");

        var svc = BuildService(chat: chatMock.Object, opts: DefaultRagOptions(false));

        var history = new List<ChatMessage>
        {
            new("user", "Hello"),
            new("assistant", "Hi there"),
        };

        await svc.AskAsync("How are you?", history, CancellationToken.None);

        Assert.NotNull(capturedMessages);
        // First message must be system
        Assert.Equal("system", capturedMessages[0].Role);
        Assert.Equal("You are a helpful assistant.", capturedMessages[0].Content);
        // History comes next
        Assert.Equal("user", capturedMessages[1].Role);
        Assert.Equal("Hello", capturedMessages[1].Content);
        Assert.Equal("assistant", capturedMessages[2].Role);
        // Final message is the new question
        Assert.Equal("user", capturedMessages[3].Role);
        Assert.Equal("How are you?", capturedMessages[3].Content);
    }
}
