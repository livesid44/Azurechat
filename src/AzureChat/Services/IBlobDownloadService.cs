namespace AzureChat.Services;

/// <summary>
/// Generates time-limited download URLs for blobs referenced by search results.
/// </summary>
public interface IBlobDownloadService
{
    /// <summary>
    /// Resolves a source path from a search result to a download URL.
    /// The path may be a plain blob name, a container-prefixed path, a full HTTPS URL,
    /// or a base64-encoded blob URL (Azure AI Search <c>metadata_storage_path</c> field).
    /// Returns <see langword="null"/> when the path cannot be resolved.
    /// </summary>
    string? GenerateDownloadUrl(string sourcePath);
}
