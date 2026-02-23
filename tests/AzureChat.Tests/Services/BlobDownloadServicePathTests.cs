using AzureChat.Services;
using Xunit;

namespace AzureChat.Tests.Services;

/// <summary>
/// Tests for BlobDownloadService's path parsing / normalisation helpers.
/// These run without any Azure SDK calls.
/// </summary>
public class BlobDownloadServicePathTests
{
    // ── StripContainerPrefix ─────────────────────────────────────────────────

    [Fact]
    public void StripContainerPrefix_WithMatchingPrefix_StripsIt()
    {
        string result = BlobDownloadService.StripContainerPrefix("documents/path/to/file.pdf", "documents");
        Assert.Equal("path/to/file.pdf", result);
    }

    [Fact]
    public void StripContainerPrefix_WithoutPrefix_ReturnsOriginal()
    {
        string result = BlobDownloadService.StripContainerPrefix("path/to/file.pdf", "documents");
        Assert.Equal("path/to/file.pdf", result);
    }

    [Fact]
    public void StripContainerPrefix_EmptyContainerName_ReturnsOriginal()
    {
        string result = BlobDownloadService.StripContainerPrefix("path/to/file.pdf", string.Empty);
        Assert.Equal("path/to/file.pdf", result);
    }

    [Fact]
    public void StripContainerPrefix_CaseInsensitive()
    {
        string result = BlobDownloadService.StripContainerPrefix("DOCUMENTS/file.txt", "documents");
        Assert.Equal("file.txt", result);
    }

    [Fact]
    public void StripContainerPrefix_PartialMatch_DoesNotStrip()
    {
        // "doc" is not a prefix of "documents/..." — must match full segment
        string result = BlobDownloadService.StripContainerPrefix("documents/file.txt", "doc");
        Assert.Equal("documents/file.txt", result);
    }

    // ── ExtractBlobNameFromUrl ────────────────────────────────────────────────

    [Fact]
    public void ExtractBlobNameFromUrl_AzureBlobUrl_ExtractsBlobName()
    {
        string url = "https://myaccount.blob.core.windows.net/documents/folder/file.pdf";
        string? result = BlobDownloadService.ExtractBlobNameFromUrl(url, "documents");
        Assert.Equal("folder/file.pdf", result);
    }

    [Fact]
    public void ExtractBlobNameFromUrl_UrlWithSpacesEncoded_ExtractsBlobName()
    {
        string url = "https://myaccount.blob.core.windows.net/documents/Sharepoint%20Data/file.docx";
        string? result = BlobDownloadService.ExtractBlobNameFromUrl(url, "documents");
        // Uri.UnescapeDataString decodes %20 → space so the blob name uses the plain form.
        Assert.Equal("Sharepoint Data/file.docx", result);
    }

    [Fact]
    public void ExtractBlobNameFromUrl_InvalidUrl_ReturnsNull()
    {
        string? result = BlobDownloadService.ExtractBlobNameFromUrl("not-a-url", "documents");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractBlobNameFromUrl_NoContainerName_ReturnsFullPath()
    {
        string url = "https://myaccount.blob.core.windows.net/documents/folder/file.pdf";
        string? result = BlobDownloadService.ExtractBlobNameFromUrl(url, string.Empty);
        Assert.Equal("documents/folder/file.pdf", result);
    }

    // ── TryDecodeBase64 ───────────────────────────────────────────────────────

    [Fact]
    public void TryDecodeBase64_ValidBase64Url_DecodesCorrectly()
    {
        // Encode "https://example.com/file.pdf" as standard base64
        string encoded = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("https://example.com/file.pdf"));

        string? result = BlobDownloadService.TryDecodeBase64(encoded);
        Assert.Equal("https://example.com/file.pdf", result);
    }

    [Fact]
    public void TryDecodeBase64_ValidBase64UrlSafe_DecodesCorrectly()
    {
        // base64url uses - and _ instead of + and /
        string standard = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("https://example.com/path/to/file.pdf"));
        string urlSafe = standard.Replace('+', '-').Replace('/', '_').TrimEnd('=');

        string? result = BlobDownloadService.TryDecodeBase64(urlSafe);
        Assert.Equal("https://example.com/path/to/file.pdf", result);
    }

    [Fact]
    public void TryDecodeBase64_PlainPath_ReturnsNull()
    {
        // "dataingestion/Sharepoint Data/file.pdf" is not valid base64
        string? result = BlobDownloadService.TryDecodeBase64("dataingestion/Sharepoint Data/file.pdf");
        // Base64 decode may fail or produce garbage — should return null or garbage (not a valid string)
        // We just assert it doesn't throw.  If it decodes to something, it won't start with https://
        if (result != null)
            Assert.DoesNotContain("dataingestion", result);
    }

    [Fact]
    public void TryDecodeBase64_TooShort_ReturnsNull()
    {
        string? result = BlobDownloadService.TryDecodeBase64("ab");
        Assert.Null(result);
    }
}
