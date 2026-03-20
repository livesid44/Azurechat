using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using AzureChat.Configuration;
using AzureChat.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureChat.Services;

/// <summary>
/// Downloads blobs from Azure Blob Storage, extracts their text content, chunks it,
/// and yields ready-to-store <see cref="BlobDocument"/> instances.
/// </summary>
public sealed class BlobIngestionService : IBlobIngestionService
{
    private readonly BlobStorageOptions _options;
    private readonly ILogger<BlobIngestionService> _logger;

    // Lazily initialised — avoids URI exceptions when credentials are not set (e.g. during tests).
    private BlobContainerClient? _containerClient;

    public BlobIngestionService(
        IOptions<BlobStorageOptions> options,
        ILogger<BlobIngestionService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    private BlobContainerClient GetContainerClient()
    {
        if (_containerClient is not null)
            return _containerClient;

        _containerClient = string.IsNullOrWhiteSpace(_options.ConnectionString)
            ? new BlobContainerClient(
                new Uri($"https://{_options.AccountName}.blob.core.windows.net/{_options.ContainerName}"),
                new Azure.Storage.StorageSharedKeyCredential(_options.AccountName, _options.AccountKey))
            : new BlobContainerClient(_options.ConnectionString, _options.ContainerName);

        return _containerClient;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<BlobDocument> IngestBlobsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting blob ingestion from container '{Container}' (prefix: '{Prefix}')",
            _options.ContainerName,
            _options.BlobPrefix);

        await foreach (BlobItem blobItem in GetContainerClient()
            .GetBlobsAsync(prefix: string.IsNullOrEmpty(_options.BlobPrefix) ? null : _options.BlobPrefix,
                           cancellationToken: cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            BlobDocument? doc = await TryProcessBlobAsync(blobItem, cancellationToken);
            if (doc is not null)
                yield return doc;
        }
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns <see langword="true"/> when a blob should be skipped before downloading.
    /// Skips hidden files (file name starts with '.', e.g. .DS_Store) and blobs whose
    /// declared content type is a known binary format.
    /// </summary>
    internal static bool ShouldSkipBlob(string blobName, string? contentType)
    {
        // Skip hidden files regardless of content type (e.g. .DS_Store, .gitkeep)
        string fileName = Path.GetFileName(blobName);
        if (fileName.StartsWith('.'))
            return true;

        // Skip blobs with binary content types
        if (contentType is not null)
        {
            foreach (string prefix in BinaryContentTypePrefixes)
            {
                if (contentType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    // Content-type prefixes/values that indicate non-text binary data.
    private static readonly string[] BinaryContentTypePrefixes =
    [
        "image/",
        "video/",
        "audio/",
        "application/octet-stream",
        "application/zip",
        "application/x-zip",
        "application/x-rar",
        "application/x-7z-compressed",
        "application/vnd.ms-excel.sheet.binary",
    ];

    private async Task<BlobDocument?> TryProcessBlobAsync(
        BlobItem blobItem,
        CancellationToken cancellationToken)
    {
        string blobName = blobItem.Name;
        string? contentType = blobItem.Properties.ContentType;

        // Skip hidden files and known binary content types before downloading.
        if (ShouldSkipBlob(blobName, contentType))
        {
            _logger.LogDebug(
                "Blob '{Blob}' skipped (hidden file or binary content type '{ContentType}').",
                blobName, contentType);
            return null;
        }

        try
        {
            BlobClient blobClient = GetContainerClient().GetBlobClient(blobName);

            // Download content as a stream and read as UTF-8 text.
            Azure.Response<BlobDownloadResult> download =
                await blobClient.DownloadContentAsync(cancellationToken);

            string content = download.Value.Content.ToString();

            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("Blob '{Blob}' is empty – skipping.", blobName);
                return null;
            }

            // Reject content that contains null bytes — a sign of binary data that
            // slipped past the content-type check (e.g. unlabelled binary blobs).
            if (content.Contains('\0'))
            {
                _logger.LogWarning(
                    "Blob '{Blob}' contains binary data (null bytes) – skipping.", blobName);
                return null;
            }

            var metadata = new BlobMetadata(
                containerName: _options.ContainerName,
                blobName: blobName,
                contentType: blobItem.Properties.ContentType ?? "application/octet-stream",
                size: blobItem.Properties.ContentLength ?? 0,
                lastModified: blobItem.Properties.LastModified);

            string title = Path.GetFileNameWithoutExtension(blobName);
            string sourceBlob = $"{_options.ContainerName}/{blobName}";
            string id = ComputeStableId(sourceBlob);

            IReadOnlyList<DocumentChunk> chunks = ChunkText(content);

            _logger.LogDebug(
                "Processed blob '{Blob}': {ChunkCount} chunk(s), {ContentLength} chars",
                blobName, chunks.Count, content.Length);

            return new BlobDocument(
                id: id,
                sourceBlob: sourceBlob,
                title: title,
                content: content,
                chunks: chunks,
                metadata: metadata,
                ingestedAt: DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process blob '{Blob}'", blobName);
            return null;
        }
    }

    /// <summary>Splits <paramref name="text"/> into overlapping chunks.</summary>
    internal IReadOnlyList<DocumentChunk> ChunkText(string text)
    {
        int chunkSize = _options.ChunkSize;
        int overlap = _options.ChunkOverlap;

        if (chunkSize <= 0 || text.Length <= chunkSize)
            return [new DocumentChunk(0, text)];

        var chunks = new List<DocumentChunk>();
        int start = 0;
        int index = 0;

        while (start < text.Length)
        {
            int end = Math.Min(start + chunkSize, text.Length);
            chunks.Add(new DocumentChunk(index++, text[start..end]));

            if (end == text.Length)
                break;

            start += chunkSize - Math.Max(0, overlap);
        }

        return chunks;
    }

    /// <summary>Generates a deterministic, URL-safe document id from the source blob path.</summary>
    private static string ComputeStableId(string sourceBlob)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sourceBlob));
        return Convert.ToHexString(hash).ToLowerInvariant()[..32];
    }
}
