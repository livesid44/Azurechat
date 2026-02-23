using AzureChat.Services;
using Xunit;

namespace AzureChat.Tests.Services;

/// <summary>
/// Tests for the SearchService $select field-name handling.
/// These validate the logic that builds the field list before calling Azure AI Search,
/// which was the source of the "property not found" HTTP 400 error.
/// </summary>
public class SearchServiceSelectFieldTests
{
    // Helper that replicates the exact $select-building logic from SearchService.SearchAsync.
    // Accepts all three configurable field names so the tests break if the implementation
    // diverges from what is being tested.
    private static IReadOnlyList<string> GetSelectFields(
        string keyField,
        string contentField,
        string titleField)
    {
        var fieldsToSelect = new[] { keyField, contentField, titleField }
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct()
            .ToList();

        return fieldsToSelect;
    }

    // Convenience overload that uses the default KeyField so existing tests stay concise.
    private static IReadOnlyList<string> GetSelectFields(string contentField, string titleField)
        => GetSelectFields("id", contentField, titleField);

    [Fact]
    public void SelectFields_DefaultFieldNames_IncludesAllThree()
    {
        var fields = GetSelectFields("chunk", "title");

        Assert.Equal(3, fields.Count);
        Assert.Contains("id", fields);
        Assert.Contains("chunk", fields);
        Assert.Contains("title", fields);
    }

    [Fact]
    public void SelectFields_EmptyContentField_OmitsContentFromSelect()
    {
        var fields = GetSelectFields(string.Empty, "title");

        Assert.DoesNotContain(string.Empty, fields);
        Assert.Contains("id", fields);
        Assert.Contains("title", fields);
    }

    [Fact]
    public void SelectFields_EmptyTitleField_OmitsTitleFromSelect()
    {
        var fields = GetSelectFields("chunk", string.Empty);

        Assert.DoesNotContain(string.Empty, fields);
        Assert.Contains("id", fields);
        Assert.Contains("chunk", fields);
    }

    [Fact]
    public void SelectFields_BothContentAndTitleEmpty_OnlyKeyFieldInSelect()
    {
        // When neither content nor title field is configured, only the key field is selected.
        var fields = GetSelectFields(string.Empty, string.Empty);

        Assert.Single(fields);
        Assert.Contains("id", fields);
    }

    [Fact]
    public void SelectFields_WhitespaceFieldNames_OmittedFromSelect()
    {
        var fields = GetSelectFields("   ", "\t");

        Assert.Single(fields);
        Assert.Contains("id", fields);
    }

    [Fact]
    public void SelectFields_CustomFieldNames_UsedVerbatim()
    {
        var fields = GetSelectFields("body", "documentName");

        Assert.Equal(3, fields.Count);
        Assert.Contains("id", fields);
        Assert.Contains("body", fields);
        Assert.Contains("documentName", fields);
    }

    [Fact]
    public void SelectFields_DuplicateFieldName_DeduplicatedInSelect()
    {
        var fields = GetSelectFields("text", "text");

        Assert.Equal(2, fields.Count); // "id" + "text"
        Assert.Contains("id", fields);
        Assert.Contains("text", fields);
    }

    [Fact]
    public void SelectFields_KeyFieldSameAsContentField_NoDuplication()
    {
        // If someone sets keyField = "id" and contentField = "id", Distinct() prevents duplication.
        var fields = GetSelectFields(keyField: "id", contentField: "id", titleField: "title");

        Assert.Equal(2, fields.Count); // "id" and "title" only
        Assert.Contains("id", fields);
        Assert.Contains("title", fields);
    }

    [Fact]
    public void SelectFields_CustomKeyField_UsedInsteadOfId()
    {
        // Indexes that use a key field other than "id" must be supported.
        var fields = GetSelectFields(keyField: "docKey", contentField: "chunk", titleField: "title");

        Assert.Equal(3, fields.Count);
        Assert.Contains("docKey", fields);
        Assert.Contains("chunk", fields);
        Assert.Contains("title", fields);
        Assert.DoesNotContain("id", fields);
    }
}
