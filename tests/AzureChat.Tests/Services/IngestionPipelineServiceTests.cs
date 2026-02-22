using AzureChat.Configuration;
using AzureChat.Models;
using AzureChat.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AzureChat.Tests.Services;

public class IngestionPipelineServiceTests
{
    private static IngestionPipelineService BuildService(
        IBlobIngestionService blob,
        ICosmosDbService cosmos) =>
        new(blob, cosmos, NullLogger<IngestionPipelineService>.Instance);

    private static BlobDocument MakeDoc(string blobName, string content = "hello world") =>
        new(
            id: "abc123",
            sourceBlob: $"container/{blobName}",
            title: blobName,
            content: content,
            chunks: [new DocumentChunk(0, content)],
            metadata: new BlobMetadata("container", blobName, "text/plain", content.Length, DateTimeOffset.UtcNow),
            ingestedAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task RunAsync_EmptyBlobs_ReturnsZeroSucceeded()
    {
        var blobMock = new Mock<IBlobIngestionService>();
        blobMock.Setup(b => b.IngestBlobsAsync(It.IsAny<CancellationToken>()))
                .Returns(AsyncEnumerable.Empty<BlobDocument>());

        var cosmosMock = new Mock<ICosmosDbService>();
        var svc = BuildService(blobMock.Object, cosmosMock.Object);

        IngestionSummary summary = await svc.RunAsync(CancellationToken.None);

        Assert.Equal(0, summary.Succeeded);
        Assert.Equal(0, summary.Failed);
        Assert.Empty(summary.Errors);
        cosmosMock.Verify(c => c.UpsertDocumentAsync(It.IsAny<BlobDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_SingleBlob_UpsertsCosmos()
    {
        var doc = MakeDoc("report.txt");

        var blobMock = new Mock<IBlobIngestionService>();
        blobMock.Setup(b => b.IngestBlobsAsync(It.IsAny<CancellationToken>()))
                .Returns(new[] { doc }.ToAsyncEnumerable());

        var cosmosMock = new Mock<ICosmosDbService>();
        cosmosMock.Setup(c => c.UpsertDocumentAsync(doc, It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        var svc = BuildService(blobMock.Object, cosmosMock.Object);

        IngestionSummary summary = await svc.RunAsync(CancellationToken.None);

        Assert.Equal(1, summary.Succeeded);
        Assert.Equal(0, summary.Failed);
        cosmosMock.Verify(c => c.UpsertDocumentAsync(doc, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_MultipleBlobs_UpsertEach()
    {
        var docs = new[]
        {
            MakeDoc("a.txt"),
            MakeDoc("b.txt"),
            MakeDoc("c.txt"),
        };

        var blobMock = new Mock<IBlobIngestionService>();
        blobMock.Setup(b => b.IngestBlobsAsync(It.IsAny<CancellationToken>()))
                .Returns(docs.ToAsyncEnumerable());

        var cosmosMock = new Mock<ICosmosDbService>();
        cosmosMock.Setup(c => c.UpsertDocumentAsync(It.IsAny<BlobDocument>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        var svc = BuildService(blobMock.Object, cosmosMock.Object);

        IngestionSummary summary = await svc.RunAsync(CancellationToken.None);

        Assert.Equal(3, summary.Succeeded);
        Assert.Equal(0, summary.Failed);
    }

    [Fact]
    public async Task RunAsync_CosmosUpsertFails_CountsAsFailed()
    {
        var docs = new[]
        {
            MakeDoc("good.txt"),
            MakeDoc("bad.txt"),
        };

        var blobMock = new Mock<IBlobIngestionService>();
        blobMock.Setup(b => b.IngestBlobsAsync(It.IsAny<CancellationToken>()))
                .Returns(docs.ToAsyncEnumerable());

        var cosmosMock = new Mock<ICosmosDbService>();
        cosmosMock.Setup(c => c.UpsertDocumentAsync(docs[0], It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        cosmosMock.Setup(c => c.UpsertDocumentAsync(docs[1], It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new InvalidOperationException("Cosmos error"));

        var svc = BuildService(blobMock.Object, cosmosMock.Object);

        IngestionSummary summary = await svc.RunAsync(CancellationToken.None);

        Assert.Equal(1, summary.Succeeded);
        Assert.Equal(1, summary.Failed);
        Assert.Single(summary.Errors);
        Assert.Contains("Cosmos error", summary.Errors[0]);
    }

    [Fact]
    public async Task RunAsync_AllFail_ReturnsCorrectCounts()
    {
        var docs = new[] { MakeDoc("x.txt"), MakeDoc("y.txt") };

        var blobMock = new Mock<IBlobIngestionService>();
        blobMock.Setup(b => b.IngestBlobsAsync(It.IsAny<CancellationToken>()))
                .Returns(docs.ToAsyncEnumerable());

        var cosmosMock = new Mock<ICosmosDbService>();
        cosmosMock.Setup(c => c.UpsertDocumentAsync(It.IsAny<BlobDocument>(), It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new InvalidOperationException("fail"));

        var svc = BuildService(blobMock.Object, cosmosMock.Object);

        IngestionSummary summary = await svc.RunAsync(CancellationToken.None);

        Assert.Equal(0, summary.Succeeded);
        Assert.Equal(2, summary.Failed);
        Assert.Equal(2, summary.Errors.Count);
    }
}

// Helper to make async enumerables from arrays for testing.
file static class AsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            await Task.Yield();
            yield return item;
        }
    }
}

file static class AsyncEnumerable
{
    public static async IAsyncEnumerable<T> Empty<T>()
    {
        await Task.CompletedTask;
        yield break;
    }
}
