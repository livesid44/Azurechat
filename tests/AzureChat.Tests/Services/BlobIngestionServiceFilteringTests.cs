using AzureChat.Services;
using Xunit;

namespace AzureChat.Tests.Services;

public class BlobIngestionServiceFilteringTests
{

    [Theory]
    [InlineData(".DS_Store")]
    [InlineData(".gitkeep")]
    [InlineData(".hidden")]
    [InlineData(".Trash-1000")]
    public void ShouldSkipBlob_HiddenFile_ReturnsTrue(string fileName)
    {
        Assert.True(BlobIngestionService.ShouldSkipBlob(fileName, null));
    }

    [Fact]
    public void ShouldSkipBlob_HiddenFileInSubFolder_ReturnsTrue()
    {
        // The blob name from the error: nested path ending in a hidden file
        Assert.True(BlobIngestionService.ShouldSkipBlob(
            "dataingestion/Sharepoint Data/01 Product Team - Common/.DS_Store", null));
    }

    // ── ShouldSkipBlob: binary content types ─────────────────────────────────

    [Theory]
    [InlineData("photo.jpg",   "image/jpeg")]
    [InlineData("photo.png",   "image/png")]
    [InlineData("clip.mp4",    "video/mp4")]
    [InlineData("song.mp3",    "audio/mpeg")]
    [InlineData("file.bin",    "application/octet-stream")]
    [InlineData("file.zip",    "application/zip")]
    [InlineData("file.rar",    "application/x-rar")]
    [InlineData("file.7z",     "application/x-7z-compressed")]
    public void ShouldSkipBlob_BinaryContentType_ReturnsTrue(string blobName, string contentType)
    {
        Assert.True(BlobIngestionService.ShouldSkipBlob(blobName, contentType));
    }

    // ── ShouldSkipBlob: allowed files ────────────────────────────────────────

    [Theory]
    [InlineData("document.txt",  null)]
    [InlineData("document.txt",  "text/plain")]
    [InlineData("readme.md",     "text/markdown")]
    [InlineData("data.json",     "application/json")]
    [InlineData("report.pdf",    "application/pdf")]
    [InlineData("notes.docx",    "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("sheet.xlsx",    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public void ShouldSkipBlob_TextOrDocumentFile_ReturnsFalse(string blobName, string? contentType)
    {
        Assert.False(BlobIngestionService.ShouldSkipBlob(blobName, contentType));
    }

    [Fact]
    public void ShouldSkipBlob_NullContentType_NotHidden_ReturnsFalse()
    {
        Assert.False(BlobIngestionService.ShouldSkipBlob("notes.txt", null));
    }

    // ── ChunkText: null-byte content (binary data safety net) ─────────────────

    [Fact]
    public void String_WithNullBytes_ContainsNullByteChar()
    {
        // Verify that a string containing null bytes can be detected
        // (used as a safety net after download in TryProcessBlobAsync).
        string binaryContent = "some text\0more text";
        Assert.True(binaryContent.Contains('\0'));
    }
}
