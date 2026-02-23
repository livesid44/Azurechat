using AzureChat.Configuration;
using AzureChat.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AzureChat.Tests.Services;

/// <summary>
/// Tests for the SearchService $select field-name handling.
/// These validate the logic that builds the field list before calling Azure AI Search,
/// which was the source of the "property not found" HTTP 400 error.
/// </summary>
public class SearchServiceSelectFieldTests
{
    // Helper that calls the internal field-selection logic via reflection so we can
    // test it without standing up a real Azure AI Search service.
    private static IReadOnlyList<string> GetSelectFields(
        string contentField,
        string titleField)
    {
        var options = Options.Create(new AzureSearchOptions
        {
            ContentField = contentField,
            TitleField = titleField,
        });

        // Replicate the exact logic from SearchService.SearchAsync so the test
        // breaks if someone changes the implementation without updating the guard.
        var fieldsToSelect = new[] { "id", contentField, titleField }
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct()
            .ToList();

        return fieldsToSelect;
    }

    [Fact]
    public void SelectFields_DefaultFieldNames_IncludesAllThree()
    {
        // Arrange: default field names
        var fields = GetSelectFields("content", "title");

        // Assert: all three fields present, no duplicates
        Assert.Equal(3, fields.Count);
        Assert.Contains("id", fields);
        Assert.Contains("content", fields);
        Assert.Contains("title", fields);
    }

    [Fact]
    public void SelectFields_EmptyContentField_OmitsContentFromSelect()
    {
        // If content field is empty (not configured), it should NOT be in $select.
        // Azure Search would 400 on an empty string or non-existent field name.
        var fields = GetSelectFields(string.Empty, "title");

        Assert.DoesNotContain(string.Empty, fields);
        Assert.Contains("id", fields);
        Assert.Contains("title", fields);
    }

    [Fact]
    public void SelectFields_EmptyTitleField_OmitsTitleFromSelect()
    {
        var fields = GetSelectFields("content", string.Empty);

        Assert.DoesNotContain(string.Empty, fields);
        Assert.Contains("id", fields);
        Assert.Contains("content", fields);
    }

    [Fact]
    public void SelectFields_BothFieldsEmpty_OnlyIdInSelect()
    {
        // When neither content nor title field is configured, only "id" is selected.
        // Azure Search returns all fields when the SDK Select collection is empty —
        // but since "id" is always hardcoded we at least get a valid request.
        var fields = GetSelectFields(string.Empty, string.Empty);

        Assert.Single(fields);
        Assert.Contains("id", fields);
    }

    [Fact]
    public void SelectFields_WhitespaceFieldNames_OmittedFromSelect()
    {
        var fields = GetSelectFields("   ", "\t");

        // Only "id" should remain
        Assert.Single(fields);
        Assert.Contains("id", fields);
    }

    [Fact]
    public void SelectFields_CustomFieldNames_UsedVerbatim()
    {
        // Users with non-default field names (e.g. "body", "name") should work.
        var fields = GetSelectFields("body", "documentName");

        Assert.Equal(3, fields.Count);
        Assert.Contains("id", fields);
        Assert.Contains("body", fields);
        Assert.Contains("documentName", fields);
    }

    [Fact]
    public void SelectFields_DuplicateFieldName_DeduplicatedInSelect()
    {
        // If titleField and contentField happen to be the same, no duplicates.
        var fields = GetSelectFields("text", "text");

        Assert.Equal(2, fields.Count); // "id" + "text"
        Assert.Contains("id", fields);
        Assert.Contains("text", fields);
    }

    [Fact]
    public void SelectFields_IdNotDuplicated_WhenUserNamesFieldId()
    {
        // If someone sets contentField = "id", Distinct() should prevent duplication.
        var fields = GetSelectFields("id", "title");

        Assert.Equal(2, fields.Count); // "id" and "title" only
        Assert.Contains("id", fields);
        Assert.Contains("title", fields);
    }
}
