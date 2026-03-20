using AzureChat.Models;

namespace AzureChat.Services;

/// <summary>
/// Applies heuristics to a list of raw index fields to suggest which field best
/// represents the document content body and which represents the document title.
/// This helps users who don't know what <c>ContentField</c>/<c>TitleField</c> to
/// configure — they can run <c>GET /api/search/fields</c> and look at the suggestions.
/// </summary>
internal static class IndexFieldRecommender
{
    // Ordered lists: first match wins.
    private static readonly string[] ContentNameHints =
        ["content", "chunk", "text", "body", "description", "passage", "document", "page_content"];

    private static readonly string[] TitleNameHints =
        ["title", "name", "filename", "source", "filepath", "heading", "subject", "label"];

    /// <summary>
    /// Annotates each string field with a <see cref="IndexField.SuggestedRole"/> of
    /// <c>"content"</c> or <c>"title"</c> where applicable, and returns the full list.
    /// </summary>
    public static IReadOnlyList<IndexField> Annotate(IReadOnlyList<IndexField> fields)
    {
        // Only string fields can meaningfully hold content or title text.
        var stringFields = fields
            .Where(f => f.Type == "Edm.String" && f.IsRetrievable)
            .ToList();

        string? contentName = PickBestMatch(stringFields, ContentNameHints);
        string? titleName   = PickBestMatch(stringFields, TitleNameHints, exclude: contentName);

        return fields.Select(f => new IndexField(
            name:          f.Name,
            type:          f.Type,
            isSearchable:  f.IsSearchable,
            isRetrievable: f.IsRetrievable,
            suggestedRole: f.Name == contentName ? "content"
                         : f.Name == titleName   ? "title"
                         : null))
            .ToList();
    }

    // Pick the field name from <fields> whose name most closely matches any hint.
    private static string? PickBestMatch(
        IReadOnlyList<IndexField> fields,
        string[] hints,
        string? exclude = null)
    {
        foreach (string hint in hints)
        {
            // Exact match (case-insensitive) first
            var exact = fields.FirstOrDefault(f =>
                f.Name != exclude &&
                f.Name.Equals(hint, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
                return exact.Name;
        }

        foreach (string hint in hints)
        {
            // Partial match: field name contains the hint
            var partial = fields.FirstOrDefault(f =>
                f.Name != exclude &&
                f.Name.Contains(hint, StringComparison.OrdinalIgnoreCase));
            if (partial is not null)
                return partial.Name;
        }

        return null;
    }
}
