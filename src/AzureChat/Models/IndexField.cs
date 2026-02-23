namespace AzureChat.Models;

/// <summary>Describes a single field in an Azure AI Search index.</summary>
public sealed class IndexField
{
    /// <summary>The field name as it appears in the index schema.</summary>
    public string Name { get; init; }

    /// <summary>The Edm type of the field (e.g. "Edm.String", "Collection(Edm.Single)").</summary>
    public string Type { get; init; }

    /// <summary>Whether this field is marked searchable (full-text search).</summary>
    public bool IsSearchable { get; init; }

    /// <summary>Whether this field is retrievable ($select).</summary>
    public bool IsRetrievable { get; init; }

    /// <summary>
    /// Heuristic recommendation: "content", "title", or null.
    /// Set by <see cref="IndexFieldRecommender"/> to guide users.
    /// </summary>
    public string? SuggestedRole { get; init; }

    public IndexField(string name, string type, bool isSearchable, bool isRetrievable, string? suggestedRole = null)
    {
        Name = name;
        Type = type;
        IsSearchable = isSearchable;
        IsRetrievable = isRetrievable;
        SuggestedRole = suggestedRole;
    }
}
