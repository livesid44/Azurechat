using AzureChat.Configuration;
using AzureChat.Models;
using AzureChat.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AzureChat.Tests.Services;

public class BlobIngestionServiceChunkingTests
{
    private static BlobIngestionService BuildService(int chunkSize, int overlap = 0) =>
        new(Options.Create(new BlobStorageOptions
        {
            ContainerName = "test",
            ChunkSize = chunkSize,
            ChunkOverlap = overlap,
        }), NullLogger<BlobIngestionService>.Instance);

    [Fact]
    public void ChunkText_ShortText_ReturnsOneChunk()
    {
        var svc = BuildService(chunkSize: 500);
        IReadOnlyList<DocumentChunk> chunks = svc.ChunkText("Hello world");

        Assert.Single(chunks);
        Assert.Equal("Hello world", chunks[0].Content);
        Assert.Equal(0, chunks[0].ChunkIndex);
    }

    [Fact]
    public void ChunkText_ZeroChunkSize_ReturnsWholeText()
    {
        var svc = BuildService(chunkSize: 0);
        IReadOnlyList<DocumentChunk> chunks = svc.ChunkText("Some text");

        Assert.Single(chunks);
        Assert.Equal("Some text", chunks[0].Content);
    }

    [Fact]
    public void ChunkText_ExactlyChunkSize_ReturnsOneChunk()
    {
        var text = new string('a', 100);
        var svc = BuildService(chunkSize: 100);
        IReadOnlyList<DocumentChunk> chunks = svc.ChunkText(text);

        Assert.Single(chunks);
        Assert.Equal(100, chunks[0].Content.Length);
    }

    [Fact]
    public void ChunkText_NoOverlap_ChunksAreContiguous()
    {
        string text = new string('a', 250);
        var svc = BuildService(chunkSize: 100, overlap: 0);
        IReadOnlyList<DocumentChunk> chunks = svc.ChunkText(text);

        // 250 chars / 100 = 3 chunks (100 + 100 + 50)
        Assert.Equal(3, chunks.Count);
        Assert.Equal(100, chunks[0].Content.Length);
        Assert.Equal(100, chunks[1].Content.Length);
        Assert.Equal(50, chunks[2].Content.Length);

        // Indices are sequential
        for (int i = 0; i < chunks.Count; i++)
            Assert.Equal(i, chunks[i].ChunkIndex);
    }

    [Fact]
    public void ChunkText_WithOverlap_ChunksOverlap()
    {
        // 10 chars, chunkSize=6, overlap=2
        // chunk 0: [0..6) = "012345"   (start=0)
        // chunk 1: [4..10) = "456789"  (start=4, reaches end → stop)
        // The overlap region "45" appears in both chunks.
        string text = "0123456789";
        var svc = BuildService(chunkSize: 6, overlap: 2);
        IReadOnlyList<DocumentChunk> chunks = svc.ChunkText(text);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("012345", chunks[0].Content);
        Assert.Equal("456789", chunks[1].Content);
        // Confirm overlap: "45" is the tail of chunk 0 and head of chunk 1
        Assert.EndsWith("45", chunks[0].Content);
        Assert.StartsWith("45", chunks[1].Content);
    }

    [Fact]
    public void ChunkText_EmptyString_ReturnsOneEmptyChunk()
    {
        var svc = BuildService(chunkSize: 100);
        IReadOnlyList<DocumentChunk> chunks = svc.ChunkText(string.Empty);

        Assert.Single(chunks);
        Assert.Equal(string.Empty, chunks[0].Content);
    }
}
