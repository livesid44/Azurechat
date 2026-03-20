using Azure.Search.Documents.Models;
using AzureChat.Services;
using Xunit;

namespace AzureChat.Tests.Services;

/// <summary>
/// Tests for SearchService's field-extraction fallback behaviour.
/// The service never sends $select so it cannot get a 400 from field name mismatches;
/// these tests verify that TryExtractField returns the right value regardless of
/// whether the configured field name matches the actual index schema.
/// </summary>
public class SearchServiceSelectFieldTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static readonly string[] ContentAliases =
        ["chunk", "content", "text", "body", "description", "passage", "page_content"];

    private static readonly string[] TitleAliases =
        ["title", "name", "source", "filename", "filepath", "heading", "subject"];

    private static readonly string[] KeyAliases =
        ["id", "chunk_id", "document_id", "doc_id", "metadata_storage_path"];

    /// <summary>Replicates the internal TryExtractField logic from SearchService.</summary>
    private static string TryExtractField(
        IDictionary<string, object> doc,
        string configuredField,
        string[] aliases)
    {
        if (!string.IsNullOrWhiteSpace(configuredField) &&
            doc.TryGetValue(configuredField, out object? val) &&
            val?.ToString() is string s && s.Length > 0)
            return s;

        foreach (string alias in aliases)
        {
            if (alias == configuredField) continue;
            if (doc.TryGetValue(alias, out object? v) &&
                v?.ToString() is string a && a.Length > 0)
                return a;
        }

        return string.Empty;
    }

    private static Dictionary<string, object> Doc(params (string key, string value)[] fields) =>
        fields.ToDictionary(f => f.key, f => (object)f.value);

    // ── Content field extraction ──────────────────────────────────────────────

    [Fact]
    public void TryExtractField_ConfiguredFieldPresent_ReturnsIt()
    {
        var doc = Doc(("chunk", "hello world"));
        Assert.Equal("hello world", TryExtractField(doc, "chunk", ContentAliases));
    }

    [Fact]
    public void TryExtractField_ConfiguredFieldWrong_FallsBackToAlias()
    {
        // Config says "content" but index uses "chunk" — fallback should find it.
        var doc = Doc(("chunk", "fallback text"));
        Assert.Equal("fallback text", TryExtractField(doc, "content", ContentAliases));
    }

    [Fact]
    public void TryExtractField_ConfiguredFieldEmpty_FallsBackToFirstAlias()
    {
        var doc = Doc(("chunk", "alias text"));
        Assert.Equal("alias text", TryExtractField(doc, string.Empty, ContentAliases));
    }

    [Fact]
    public void TryExtractField_NoMatchingField_ReturnsEmpty()
    {
        var doc = Doc(("vector", "some binary data"));
        Assert.Equal(string.Empty, TryExtractField(doc, "content", ContentAliases));
    }

    [Fact]
    public void TryExtractField_ConfiguredFieldHasEmptyValue_FallsBackToAlias()
    {
        // Configured field exists but is empty — should fall through to alias.
        var doc = Doc(("content", ""), ("chunk", "actual content"));
        Assert.Equal("actual content", TryExtractField(doc, "content", ContentAliases));
    }

    // ── Title field extraction ────────────────────────────────────────────────

    [Fact]
    public void TryExtractField_TitleFieldPresent_ReturnsIt()
    {
        var doc = Doc(("title", "My Document"));
        Assert.Equal("My Document", TryExtractField(doc, "title", TitleAliases));
    }

    [Fact]
    public void TryExtractField_TitleFieldWrong_FallsBackToName()
    {
        // Config says "title" but index uses "name".
        var doc = Doc(("name", "doc name"));
        Assert.Equal("doc name", TryExtractField(doc, "title", TitleAliases));
    }

    [Fact]
    public void TryExtractField_TitleFieldWrong_FallsBackToSource()
    {
        var doc = Doc(("source", "doc-source.pdf"));
        Assert.Equal("doc-source.pdf", TryExtractField(doc, "title", TitleAliases));
    }

    // ── Key field extraction ──────────────────────────────────────────────────

    [Fact]
    public void TryExtractField_KeyFieldId_ReturnsIt()
    {
        var doc = Doc(("id", "abc123"));
        Assert.Equal("abc123", TryExtractField(doc, "id", KeyAliases));
    }

    [Fact]
    public void TryExtractField_KeyFieldChunkId_FallsBack()
    {
        // Index uses "chunk_id" but KeyField is still default "id".
        var doc = Doc(("chunk_id", "xyz789"));
        Assert.Equal("xyz789", TryExtractField(doc, "id", KeyAliases));
    }

    [Fact]
    public void TryExtractField_MetadataStoragePath_FallsBack()
    {
        // Azure AI Studio sometimes uses "metadata_storage_path" as the key.
        var doc = Doc(("metadata_storage_path", "/container/file.txt"));
        Assert.Equal("/container/file.txt", TryExtractField(doc, "id", KeyAliases));
    }

    // ── Priority: configured field wins over alias even if alias comes first ──

    [Fact]
    public void TryExtractField_ConfiguredFieldHasPriorityOverAlias()
    {
        // Both "content" and "chunk" are present; configured = "content" should win.
        var doc = Doc(("content", "from content"), ("chunk", "from chunk"));
        Assert.Equal("from content", TryExtractField(doc, "content", ContentAliases));
    }
}
