using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using AzureChat.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureChat.Services;

/// <summary>
/// Generates short-lived (1-hour) read-only SAS URLs for blobs in the configured
/// Azure Blob Storage container so users can download source files referenced in
/// search results directly from the browser.
/// </summary>
public sealed class BlobDownloadService : IBlobDownloadService
{
    private readonly BlobStorageOptions _options;
    private readonly ILogger<BlobDownloadService> _logger;

    // Lazily initialised — avoids URI exceptions when credentials are not yet configured.
    private BlobContainerClient? _containerClient;

    public BlobDownloadService(
        IOptions<BlobStorageOptions> options,
        ILogger<BlobDownloadService> logger)
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
    public string? GenerateDownloadUrl(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return null;

        // Try to decode base64 (handles Azure AI Search metadata_storage_path field).
        // If the decoded value is an HTTPS URL, redirect directly to it.
        string? decoded = TryDecodeBase64(sourcePath);
        string effective = decoded?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true
            ? decoded
            : sourcePath;

        // If we have a full HTTPS URL, extract the blob name from it so we can generate
        // a SAS using our own configured credentials (works even without a public container).
        if (effective.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            string? blobName = ExtractBlobNameFromUrl(effective, _options.ContainerName);
            if (string.IsNullOrWhiteSpace(blobName))
            {
                // Unknown storage account or unrecognised URL shape — return as-is.
                _logger.LogDebug("Returning raw URL for source path '{Path}'", sourcePath);
                return effective;
            }

            effective = blobName; // fall through to SAS generation below
        }

        // Strip container name prefix if the path includes it (e.g. "documents/path/to/file.pdf").
        string blobPath = StripContainerPrefix(effective, _options.ContainerName);

        if (string.IsNullOrWhiteSpace(blobPath))
            return null;

        BlobClient blobClient = GetContainerClient().GetBlobClient(blobPath);

        if (blobClient.CanGenerateSasUri)
        {
            Uri sasUri = blobClient.GenerateSasUri(
                BlobSasPermissions.Read,
                DateTimeOffset.UtcNow.AddHours(1));
            _logger.LogDebug("Generated SAS URL for blob '{Blob}'", blobPath);
            return sasUri.ToString();
        }

        // SAS generation requires a key credential — fall back to the plain blob URI
        // which works for publicly accessible containers.
        _logger.LogDebug(
            "Cannot generate SAS (no key credential) — returning plain URI for blob '{Blob}'", blobPath);
        return blobClient.Uri.ToString();
    }

    // ── Internal helpers (internal for unit testing) ──────────────────────────

    /// <summary>
    /// Strips the container name prefix from a blob path if present.
    /// E.g. "documents/path/to/file.pdf" → "path/to/file.pdf" when container is "documents".
    /// </summary>
    internal static string StripContainerPrefix(string blobPath, string containerName)
    {
        if (!string.IsNullOrWhiteSpace(containerName) &&
            blobPath.StartsWith(containerName + "/", StringComparison.OrdinalIgnoreCase))
        {
            return blobPath[(containerName.Length + 1)..];
        }

        return blobPath;
    }

    /// <summary>
    /// Extracts the blob name from a full Azure Blob Storage URL.
    /// Returns <see langword="null"/> if the URL cannot be parsed.
    /// </summary>
    internal static string? ExtractBlobNameFromUrl(string url, string containerName)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            return null;

        // AbsolutePath = /{containerName}/{blobName}  (percent-encoded)
        // Decode so BlobContainerClient.GetBlobClient receives a plain path.
        string uriPath = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        return StripContainerPrefix(uriPath, containerName);
    }

    /// <summary>
    /// Tries to decode <paramref name="s"/> as base64 (standard or URL-safe).
    /// Returns <see langword="null"/> if the string is not valid base64.
    /// </summary>
    internal static string? TryDecodeBase64(string s)
    {
        if (s.Length < 4)
            return null;

        try
        {
            // Normalise base64url (- → +, _ → /) then add padding.
            string normalised = s.Replace('-', '+').Replace('_', '/');
            int mod = normalised.Length % 4;
            if (mod == 2) normalised += "==";
            else if (mod == 3) normalised += "=";
            else if (mod == 1) return null; // Invalid base64 length

            byte[] bytes = Convert.FromBase64String(normalised);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }
}
