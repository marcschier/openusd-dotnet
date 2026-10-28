// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer;

internal enum ViewerPropertyFilter
{
    All,
    AuthoredAttributes,
    UnauthoredAttributes,
    BlockedAttributes,
    TimeSampledAttributes,
    Relationships,
    UnknownAuthoredState
}

internal enum ViewerPropertyGroup
{
    All,
    Transform,
    Camera,
    Light,
    ShaderInputs,
    ShaderOutputs,
    MaterialBindings,
    Other
}

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
        int pageIndex,
        ViewerPropertyFilter filter = ViewerPropertyFilter.All,
        ViewerPropertyGroup group = ViewerPropertyGroup.All,
        bool isCamera = false,
        bool isLight = false,
        IReadOnlyList<string>? lightSchemaProperties = null)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(relationships);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        if (!Enum.IsDefined(filter))
        {
            throw new ArgumentOutOfRangeException(nameof(filter));
        }
        if (!Enum.IsDefined(group))
        {
            throw new ArgumentOutOfRangeException(nameof(group));
        }
        string search = query?.Trim() ?? string.Empty;
        int total = checked(attributes.Count + relationships.Count);
        int matches = 0;
        foreach (ViewerAttributeSnapshot attribute in attributes)
        {
            if (Matches(attribute, filter, search, group, isCamera, isLight, lightSchemaProperties))
            {
                matches++;
            }
        }
        foreach (ViewerRelationshipSnapshot relationship in relationships)
        {
            if (Matches(relationship, filter, search, group))
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
            if (Matches(attribute, filter, search, group, isCamera, isLight, lightSchemaProperties) &&
                seen++ >= start && pageAttributes.Count < PageSize)
            {
                pageAttributes.Add(attribute);
            }
        }
        foreach (ViewerRelationshipSnapshot relationship in relationships)
        {
            if (Matches(relationship, filter, search, group) &&
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

    private static bool Matches(
        ViewerAttributeSnapshot attribute, ViewerPropertyFilter filter, string query,
        ViewerPropertyGroup group, bool isCamera, bool isLight, IReadOnlyList<string>? lightSchemaProperties) =>
        (group == ViewerPropertyGroup.All ||
         (group == ViewerPropertyGroup.ShaderInputs &&
             attribute.Name.StartsWith("inputs:", StringComparison.Ordinal)) ||
         AttributeGroup(attribute.Name, isCamera, isLight, lightSchemaProperties) == group) &&
        Matches(attribute.Name, attribute.TypeName, query) && (filter switch
        {
            ViewerPropertyFilter.All => true,
            ViewerPropertyFilter.AuthoredAttributes => attribute.HasAuthoredValue == true,
            ViewerPropertyFilter.UnauthoredAttributes => attribute.HasAuthoredValue == false,
            ViewerPropertyFilter.BlockedAttributes => attribute.IsBlocked,
            ViewerPropertyFilter.TimeSampledAttributes => attribute.TimeSampleCount > 0,
            ViewerPropertyFilter.UnknownAuthoredState => attribute.HasAuthoredValue is null,
            _ => false
        });

    private static bool Matches(
        ViewerRelationshipSnapshot relationship, ViewerPropertyFilter filter, string query,
        ViewerPropertyGroup group) =>
        filter is ViewerPropertyFilter.All or ViewerPropertyFilter.Relationships &&
        (group == ViewerPropertyGroup.All || RelationshipGroup(relationship.Name) == group) &&
        Matches(relationship.Name, "relationship", query);

    private static ViewerPropertyGroup AttributeGroup(
        string name, bool isCamera, bool isLight, IReadOnlyList<string>? lightSchemaProperties)
    {
        if (name == "xformOpOrder" || name.StartsWith("xformOp:", StringComparison.Ordinal))
        {
            return ViewerPropertyGroup.Transform;
        }
        if (isCamera && name is "projection" or "horizontalAperture" or "verticalAperture" or
            "horizontalApertureOffset" or "verticalApertureOffset" or "focalLength" or
            "clippingRange" or "clippingPlanes" or "fStop" or "focusDistance" or "exposure" or
            "exposure:iso" or "exposure:time" or "exposure:fStop" or "exposure:responsivity" or
            "shutter:open" or "shutter:close" or "stereoRole")
        {
            return ViewerPropertyGroup.Camera;
        }
        if (isLight && (lightSchemaProperties?.Contains(name, StringComparer.Ordinal) == true ||
            name is
            "inputs:intensity" or "inputs:exposure" or "inputs:diffuse" or
            "inputs:specular" or "inputs:colorTemperature" or "inputs:enableColorTemperature" or
            "inputs:normalize" or "inputs:color"))
        {
            return ViewerPropertyGroup.Light;
        }
        if (name.StartsWith("inputs:", StringComparison.Ordinal))
        {
            return ViewerPropertyGroup.ShaderInputs;
        }
        if (name.StartsWith("outputs:", StringComparison.Ordinal))
        {
            return ViewerPropertyGroup.ShaderOutputs;
        }
        return ViewerPropertyGroup.Other;
    }

    private static ViewerPropertyGroup RelationshipGroup(string name) =>
        name == "material:binding" || name.StartsWith("material:binding:", StringComparison.Ordinal)
            ? ViewerPropertyGroup.MaterialBindings
            : ViewerPropertyGroup.Other;
}
