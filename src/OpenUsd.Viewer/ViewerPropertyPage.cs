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
    Primvars,
    Collections,
    Imageable,
    Physics,
    Mesh,
    Skinning,
    Model,
    Instancer,
    PointsAndCurves,
    Volume,
    Shape,
    Render,
    Other
}

internal enum ViewerGeometrySchema
{
    None,
    Mesh,
    PointInstancer,
    Points,
    BasisCurves,
    NurbsCurves,
    HermiteCurves,
    Volume,
    OpenVdbAsset,
    Field3DAsset,
    FieldAsset,
    Sphere,
    Cube,
    Cylinder,
    Cone,
    Capsule,
    Plane,
    Skeleton,
    SkelAnimation,
    BlendShape,
    RenderSettings,
    RenderProduct,
    RenderVar,
    RenderPass
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
        IReadOnlyList<string>? lightSchemaProperties = null,
        ViewerGeometrySchema geometry = ViewerGeometrySchema.None)
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
            if (Matches(attribute, filter, search, group, isCamera, isLight, lightSchemaProperties,
                    geometry))
            {
                matches++;
            }
        }
        foreach (ViewerRelationshipSnapshot relationship in relationships)
        {
            if (Matches(relationship, filter, search, group, geometry))
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
            if (Matches(attribute, filter, search, group, isCamera, isLight, lightSchemaProperties,
                    geometry) &&
                seen++ >= start && pageAttributes.Count < PageSize)
            {
                pageAttributes.Add(attribute);
            }
        }
        foreach (ViewerRelationshipSnapshot relationship in relationships)
        {
            if (Matches(relationship, filter, search, group, geometry) &&
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
        ViewerPropertyGroup group, bool isCamera, bool isLight, IReadOnlyList<string>? lightSchemaProperties,
        ViewerGeometrySchema geometry) =>
        (group == ViewerPropertyGroup.All ||
         (group == ViewerPropertyGroup.ShaderInputs &&
             attribute.Name.StartsWith("inputs:", StringComparison.Ordinal)) ||
         AttributeGroup(attribute.Name, isCamera, isLight, lightSchemaProperties, geometry) ==
             group) &&
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
        ViewerPropertyGroup group, ViewerGeometrySchema geometry) =>
        filter is ViewerPropertyFilter.All or ViewerPropertyFilter.Relationships &&
        (group == ViewerPropertyGroup.All || RelationshipGroup(relationship.Name, geometry) == group) &&
        Matches(relationship.Name, "relationship", query);

    private static ViewerPropertyGroup AttributeGroup(
        string name, bool isCamera, bool isLight, IReadOnlyList<string>? lightSchemaProperties,
        ViewerGeometrySchema geometry)
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
        if (geometry == ViewerGeometrySchema.Mesh && name is "points" or "velocities" or "accelerations" or
            "normals" or "extent" or "doubleSided" or "orientation" or "faceVertexCounts" or "faceVertexIndices" or
            "subdivisionScheme" or "interpolateBoundary" or "faceVaryingLinearInterpolation" or
            "triangleSubdivisionRule" or "holeIndices" or "cornerIndices" or "cornerSharpnesses" or
            "creaseIndices" or "creaseLengths" or "creaseSharpnesses")
        {
            return ViewerPropertyGroup.Mesh;
        }
        if (geometry == ViewerGeometrySchema.PointInstancer && name is "protoIndices" or "ids" or "positions" or
            "orientations" or "orientationsf" or "scales" or "velocities" or "accelerations" or "angularVelocities" or
            "invisibleIds" or "extent")
        {
            return ViewerPropertyGroup.Instancer;
        }
        if (IsPointsOrCurvesProperty(name, geometry))
        {
            return ViewerPropertyGroup.PointsAndCurves;
        }
        if (IsFieldAssetProperty(name, geometry))
        {
            return ViewerPropertyGroup.Volume;
        }
        if (IsShapeProperty(name, geometry))
        {
            return ViewerPropertyGroup.Shape;
        }
        if (IsSkeletalProperty(name, geometry))
        {
            return ViewerPropertyGroup.Skinning;
        }
        if (IsRenderProperty(name, geometry))
        {
            return ViewerPropertyGroup.Render;
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
        if (HasNamespace(name, "primvars:"))
        {
            return ViewerPropertyGroup.Primvars;
        }
        if (HasNamespace(name, "collection:"))
        {
            return ViewerPropertyGroup.Collections;
        }
        if (name is "visibility" or "purpose")
        {
            return ViewerPropertyGroup.Imageable;
        }
        if (HasNamespace(name, "skel:"))
        {
            return ViewerPropertyGroup.Skinning;
        }
        if (HasNamespace(name, "model:"))
        {
            return ViewerPropertyGroup.Model;
        }
        return HasNamespace(name, "physics:") ? ViewerPropertyGroup.Physics : ViewerPropertyGroup.Other;
    }

    private static ViewerPropertyGroup RelationshipGroup(string name, ViewerGeometrySchema geometry)
    {
        if (geometry == ViewerGeometrySchema.PointInstancer && name == "prototypes")
        {
            return ViewerPropertyGroup.Instancer;
        }
        if (geometry == ViewerGeometrySchema.Volume && HasNamespace(name, "field:"))
        {
            return ViewerPropertyGroup.Volume;
        }
        if (IsRenderRelationship(name, geometry))
        {
            return ViewerPropertyGroup.Render;
        }
        if (name == "material:binding" || name.StartsWith("material:binding:", StringComparison.Ordinal))
        {
            return ViewerPropertyGroup.MaterialBindings;
        }
        if (HasNamespace(name, "collection:"))
        {
            return ViewerPropertyGroup.Collections;
        }
        if (name == "proxyPrim")
        {
            return ViewerPropertyGroup.Imageable;
        }
        if (HasNamespace(name, "skel:"))
        {
            return ViewerPropertyGroup.Skinning;
        }
        if (HasNamespace(name, "model:"))
        {
            return ViewerPropertyGroup.Model;
        }
        return HasNamespace(name, "physics:") ? ViewerPropertyGroup.Physics : ViewerPropertyGroup.Other;
    }

    private static bool IsPointsOrCurvesProperty(string name, ViewerGeometrySchema geometry) =>
        geometry switch
        {
            ViewerGeometrySchema.Points => IsPointBasedProperty(name) || name is "widths" or "ids",
            ViewerGeometrySchema.BasisCurves => IsCurvesProperty(name) || name is "type" or "basis" or "wrap",
            ViewerGeometrySchema.NurbsCurves => IsCurvesProperty(name) ||
                name is "order" or "knots" or "ranges" or "pointWeights",
            ViewerGeometrySchema.HermiteCurves => IsCurvesProperty(name) || name == "tangents",
            _ => false
        };

    private static bool IsFieldAssetProperty(string name, ViewerGeometrySchema geometry) =>
        geometry switch
        {
            ViewerGeometrySchema.OpenVdbAsset => IsFieldAssetBaseProperty(name) || name == "fieldClass",
            ViewerGeometrySchema.Field3DAsset => IsFieldAssetBaseProperty(name) || name == "fieldPurpose",
            ViewerGeometrySchema.FieldAsset => IsFieldAssetBaseProperty(name),
            _ => false
        };

    private static bool IsShapeProperty(string name, ViewerGeometrySchema geometry) =>
        geometry switch
        {
            ViewerGeometrySchema.Sphere => IsShapeBaseProperty(name) || name == "radius",
            ViewerGeometrySchema.Cube => IsShapeBaseProperty(name) || name == "size",
            ViewerGeometrySchema.Cylinder or ViewerGeometrySchema.Cone or ViewerGeometrySchema.Capsule =>
                IsShapeBaseProperty(name) || name is "height" or "radius" or "axis",
            ViewerGeometrySchema.Plane => IsShapeBaseProperty(name) || name is "width" or "length" or "axis",
            _ => false
        };

    private static bool IsSkeletalProperty(string name, ViewerGeometrySchema geometry) =>
        geometry switch
        {
            ViewerGeometrySchema.Skeleton => name is "joints" or "jointNames" or "bindTransforms" or "restTransforms",
            ViewerGeometrySchema.SkelAnimation => name is "joints" or "translations" or "rotations" or "scales" or
                "blendShapes" or "blendShapeWeights",
            ViewerGeometrySchema.BlendShape => name is "offsets" or "normalOffsets" or "pointIndices" ||
                HasNamespace(name, "inbetweens:"),
            _ => false
        };

    private static bool IsRenderProperty(string name, ViewerGeometrySchema geometry) =>
        geometry switch
        {
            ViewerGeometrySchema.RenderSettings => IsRenderSettingsBaseProperty(name) ||
                name is "includedPurposes" or "materialBindingPurposes" or "renderingColorSpace",
            ViewerGeometrySchema.RenderProduct => IsRenderSettingsBaseProperty(name) ||
                name is "productType" or "productName",
            ViewerGeometrySchema.RenderVar => name is "dataType" or "sourceName" or "sourceType",
            ViewerGeometrySchema.RenderPass => name is "passType" or "command" or "fileName" or "denoise:enable",
            _ => false
        };

    private static bool IsRenderRelationship(string name, ViewerGeometrySchema geometry) =>
        geometry switch
        {
            ViewerGeometrySchema.RenderSettings => name is "camera" or "products",
            ViewerGeometrySchema.RenderProduct => name is "camera" or "orderedVars",
            ViewerGeometrySchema.RenderPass => name is "renderSource" or "inputPasses",
            _ => false
        };

    private static bool IsRenderSettingsBaseProperty(string name) =>
        name is "resolution" or "pixelAspectRatio" or "aspectRatioConformPolicy" or "dataWindowNDC" or
            "instantaneousShutter" or "disableMotionBlur" or "disableDepthOfField";

    private static bool IsShapeBaseProperty(string name) => name is "extent" or "doubleSided" or "orientation";

    private static bool IsFieldAssetBaseProperty(string name) =>
        name is "filePath" or "fieldName" or "fieldIndex" or "fieldDataType" or "vectorDataRoleHint";

    private static bool IsCurvesProperty(string name) =>
        IsPointBasedProperty(name) || name is "curveVertexCounts" or "widths";

    private static bool IsPointBasedProperty(string name) =>
        name is "points" or "velocities" or "accelerations" or "normals" or "extent" or "doubleSided" or
            "orientation";

    // A namespace prefix only groups a property that has a nonempty name after it.
    private static bool HasNamespace(string name, string prefix) =>
        name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.Ordinal);
}
