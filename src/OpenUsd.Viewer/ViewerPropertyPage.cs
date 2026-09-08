// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer;

internal sealed class ViewerPropertyPage
{
    internal const int PageSize = 32;

    private ViewerPropertyPage(
        int totalCount,
        int matchCount,
        int pageIndex,
        ViewerAttributeSnapshot[] attributes,
        ViewerRelationshipSnapshot[] relationships)
    {
        TotalCount = totalCount;
        MatchCount = matchCount;
        PageIndex = pageIndex;
        Attributes = Array.AsReadOnly(attributes);
        Relationships = Array.AsReadOnly(relationships);
    }

    internal int TotalCount { get; }
    internal int MatchCount { get; }
    internal int PageIndex { get; }
    internal int StartIndex => PageIndex * PageSize;
    internal int Count => Attributes.Count + Relationships.Count;
    internal bool HasPrevious => PageIndex > 0;
    internal bool HasNext => StartIndex + Count < MatchCount;
    internal IReadOnlyList<ViewerAttributeSnapshot> Attributes { get; }
    internal IReadOnlyList<ViewerRelationshipSnapshot> Relationships { get; }

    internal static ViewerPropertyPage Create(
        IReadOnlyList<ViewerAttributeSnapshot> attributes,
        IReadOnlyList<ViewerRelationshipSnapshot> relationships,
        string? query,
        int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(relationships);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        string search = query?.Trim() ?? string.Empty;
        int total = checked(attributes.Count + relationships.Count);
        int matches = 0;
        foreach (ViewerAttributeSnapshot attribute in attributes)
        {
            if (Matches(attribute.Name, attribute.TypeName, search))
            {
                matches++;
            }
        }
        foreach (ViewerRelationshipSnapshot relationship in relationships)
        {
            if (Matches(relationship.Name, "relationship", search))
            {
                matches++;
            }
        }

        int selectedPage = Math.Min(pageIndex, matches == 0 ? 0 : (matches - 1) / PageSize);
        int start = selectedPage * PageSize;
        int seen = 0;
        var pageAttributes = new List<ViewerAttributeSnapshot>(PageSize);
        var pageRelationships = new List<ViewerRelationshipSnapshot>();
        foreach (ViewerAttributeSnapshot attribute in attributes)
        {
            if (Matches(attribute.Name, attribute.TypeName, search) &&
                seen++ >= start && pageAttributes.Count < PageSize)
            {
                pageAttributes.Add(attribute);
            }
        }
        foreach (ViewerRelationshipSnapshot relationship in relationships)
        {
            if (Matches(relationship.Name, "relationship", search) &&
                seen++ >= start && pageAttributes.Count + pageRelationships.Count < PageSize)
            {
                pageRelationships.Add(relationship);
            }
        }
        return new ViewerPropertyPage(total, matches, selectedPage,
            pageAttributes.ToArray(), pageRelationships.ToArray());
    }

    private static bool Matches(string name, string typeName, string query) =>
        name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        typeName.Contains(query, StringComparison.OrdinalIgnoreCase);
}
