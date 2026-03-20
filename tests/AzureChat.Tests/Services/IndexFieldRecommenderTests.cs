using AzureChat.Models;
using AzureChat.Services;
using Xunit;

namespace AzureChat.Tests.Services;

public class IndexFieldRecommenderTests
{
    private static IndexField Str(string name, bool searchable = true, bool retrievable = true) =>
        new(name, "Edm.String", searchable, retrievable);

    private static IndexField Vec(string name) =>
        new(name, "Collection(Edm.Single)", false, false);

    // ── Content field detection ──────────────────────────────────────────────

    [Theory]
    [InlineData("content")]
    [InlineData("Content")]
    [InlineData("CONTENT")]
    public void Annotate_ExactContentName_SuggestsContent(string fieldName)
    {
        var result = IndexFieldRecommender.Annotate([Str(fieldName), Str("title")]);
        Assert.Equal("content", result.First(f => f.Name == fieldName).SuggestedRole);
    }

    [Theory]
    [InlineData("chunk")]
    [InlineData("text")]
    [InlineData("body")]
    [InlineData("description")]
    [InlineData("page_content")]
    public void Annotate_CommonContentAliases_SuggestsContent(string fieldName)
    {
        var result = IndexFieldRecommender.Annotate([Str(fieldName)]);
        Assert.Equal("content", result.First(f => f.Name == fieldName).SuggestedRole);
    }

    [Fact]
    public void Annotate_PartialContentMatch_SuggestsContent()
    {
        // e.g. "document_content" contains "content"
        var result = IndexFieldRecommender.Annotate([Str("document_content")]);
        Assert.Equal("content", result[0].SuggestedRole);
    }

    // ── Title field detection ────────────────────────────────────────────────

    [Theory]
    [InlineData("title")]
    [InlineData("Title")]
    [InlineData("name")]
    [InlineData("filename")]
    [InlineData("source")]
    public void Annotate_CommonTitleNames_SuggestsTitle(string fieldName)
    {
        // Make sure it doesn't also claim "content" role.
        var result = IndexFieldRecommender.Annotate([Str("content"), Str(fieldName)]);
        Assert.Equal("title", result.First(f => f.Name == fieldName).SuggestedRole);
    }

    [Fact]
    public void Annotate_PartialTitleMatch_SuggestsTitle()
    {
        // "file_title" contains "title" but no content hint → title role
        var result = IndexFieldRecommender.Annotate([Str("file_title")]);
        Assert.Equal("title", result[0].SuggestedRole);
    }

    // ── Both roles assigned independently ────────────────────────────────────

    [Fact]
    public void Annotate_IndexWithBothFields_AssignsBothRoles()
    {
        var fields = new[] { Str("content"), Str("title"), Vec("embedding") };
        var result = IndexFieldRecommender.Annotate(fields);

        Assert.Equal("content", result.First(f => f.Name == "content").SuggestedRole);
        Assert.Equal("title",   result.First(f => f.Name == "title").SuggestedRole);
        Assert.Null(result.First(f => f.Name == "embedding").SuggestedRole);
    }

    [Fact]
    public void Annotate_SameFieldNotAssignedBothRoles()
    {
        // "content" wins for content role; "title" wins for title role —
        // the same name must not appear as both.
        var fields = new[] { Str("content"), Str("title") };
        var result = IndexFieldRecommender.Annotate(fields);

        var contentField = result.Single(f => f.SuggestedRole == "content");
        var titleField   = result.Single(f => f.SuggestedRole == "title");
        Assert.NotEqual(contentField.Name, titleField.Name);
    }

    // ── Non-string fields are not suggested ───────────────────────────────────

    [Fact]
    public void Annotate_VectorField_NotSuggestedForAnyRole()
    {
        // Even if named "content", a vector field should not get a suggestion
        // because it is not an Edm.String.
        var result = IndexFieldRecommender.Annotate([Vec("content"), Vec("title")]);
        Assert.All(result, f => Assert.Null(f.SuggestedRole));
    }

    [Fact]
    public void Annotate_NonRetrievableStringField_NotSuggestedForAnyRole()
    {
        // Fields with isRetrievable=false cannot appear in $select, so we skip them.
        var result = IndexFieldRecommender.Annotate(
            [new IndexField("content", "Edm.String", true, isRetrievable: false)]);
        Assert.Null(result[0].SuggestedRole);
    }

    // ── Other field properties are preserved ─────────────────────────────────

    [Fact]
    public void Annotate_PreservesAllFieldProperties()
    {
        var original = new IndexField("myField", "Edm.String", isSearchable: true, isRetrievable: true);
        var result = IndexFieldRecommender.Annotate([original]);

        Assert.Equal("myField",    result[0].Name);
        Assert.Equal("Edm.String", result[0].Type);
        Assert.True(result[0].IsSearchable);
        Assert.True(result[0].IsRetrievable);
    }

    // ── Empty index ──────────────────────────────────────────────────────────

    [Fact]
    public void Annotate_EmptyFieldList_ReturnsEmptyList()
    {
        var result = IndexFieldRecommender.Annotate([]);
        Assert.Empty(result);
    }

    // ── No recognizable names fallback ───────────────────────────────────────

    [Fact]
    public void Annotate_UnrecognizedFieldNames_NullSuggestedRole()
    {
        var result = IndexFieldRecommender.Annotate([Str("field_a"), Str("field_b")]);
        Assert.All(result, f => Assert.Null(f.SuggestedRole));
    }
}
