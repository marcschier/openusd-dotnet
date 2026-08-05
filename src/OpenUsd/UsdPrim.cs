// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Geom;

namespace OpenUsd;

/// <summary>
/// A lightweight path-based view of a prim on an owning <see cref="UsdStage"/>.
/// </summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdPrim : IUsdStageBound
{
    private static readonly IReadOnlyList<UsdPayloadArc> EmptyPayloadArcs =
        Array.AsReadOnly(Array.Empty<UsdPayloadArc>());

    private readonly UsdStage? _stage;

    internal UsdPrim(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }

    /// <summary>Gets the absolute prim path.</summary>
    public string Path { get; }

    /// <summary>Gets the composed authored type name, or an empty string for an untyped prim.</summary>
    public string TypeName => Stage.Native.GetPrimTypeName(Path);

    /// <summary>Gets API schema names applied through authored or built-in schema composition.</summary>
    public string[] GetAppliedSchemas() => Stage.Native.GetPrimAppliedSchemas(Path);

    /// <summary>Gets all direct child prims, including inactive, undefined, and abstract children.</summary>
    public IReadOnlyList<UsdPrim> GetChildren()
    {
        string[] paths = Stage.Native.GetPrimChildPaths(Path);
        var children = new UsdPrim[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            children[i] = new UsdPrim(Stage, paths[i]);
        }
        return children;
    }

    /// <summary>Gets composed attribute names using one bulk native call.</summary>
    public string[] GetAttributeNames() => Stage.Native.GetPrimAttributeNames(Path);

    /// <summary>Gets composed attribute descriptors using one bulk native call.</summary>
    public IReadOnlyList<UsdAttribute> GetAttributes()
    {
        string[] names = Stage.Native.GetPrimAttributeNames(Path);
        var attributes = new UsdAttribute[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            attributes[i] = new UsdAttribute(Stage, Path, names[i]);
        }
        return attributes;
    }

    /// <summary>Gets a path-based attribute descriptor.</summary>
    public UsdAttribute GetAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new UsdAttribute(Stage, Path, name);
    }

    /// <summary>Gets composed relationship names using one bulk native call.</summary>
    public string[] GetRelationshipNames() => Stage.Native.GetPrimRelationshipNames(Path);

    /// <summary>Gets composed relationship descriptors using one bulk native call.</summary>
    public IReadOnlyList<UsdRelationship> GetRelationships()
    {
        string[] names = Stage.Native.GetPrimRelationshipNames(Path);
        var relationships = new UsdRelationship[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            relationships[i] = new UsdRelationship(Stage, Path, names[i]);
        }
        return relationships;
    }

    /// <summary>Gets a path-based relationship descriptor.</summary>
    public UsdRelationship GetRelationship(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new UsdRelationship(Stage, Path, name);
    }

    /// <summary>Gets this prim's world bounds at default time for the selected purposes.</summary>
    /// <remarks>
    /// A missing, inactive, unloaded-without-extents-hint, or otherwise unbounded prim returns
    /// <see cref="UsdBounds3d.Empty"/>.
    /// </remarks>
    public UsdBounds3d GetWorldBounds(
        UsdGeomPurposeMask purposeMask = UsdGeomPurposeMask.All)
    {
        UsdPath.ValidateAbsolutePrimPath(Path, nameof(Path));
        return UsdBounds3d.FromNative(
            Stage.Native.GetWorldBounds(
                Path,
                UsdBounds3d.ValidatePurposeMask(purposeMask)));
    }

    /// <summary>Gets this prim's world bounds at a numeric time for the selected purposes.</summary>
    public UsdBounds3d GetWorldBounds(
        double timeCode,
        UsdGeomPurposeMask purposeMask = UsdGeomPurposeMask.All)
    {
        UsdPath.ValidateAbsolutePrimPath(Path, nameof(Path));
        return UsdBounds3d.FromNative(
            Stage.Native.GetWorldBounds(
                Path,
                UsdBounds3d.ValidatePurposeMask(purposeMask),
                UsdBounds3d.ValidateTimeCode(timeCode)));
    }

    /// <summary>Gets this prim's world oriented bounds at default time for the selected purposes.</summary>
    public UsdOrientedBounds3d GetWorldOrientedBounds(
        UsdGeomPurposeMask purposeMask = UsdGeomPurposeMask.All)
    {
        UsdPath.ValidateAbsolutePrimPath(Path, nameof(Path));
        return UsdOrientedBounds3d.FromNative(
            Stage.Native.GetWorldOrientedBounds(
                Path,
                UsdOrientedBounds3d.ValidatePurposeMask(purposeMask)));
    }

    /// <summary>Gets this prim's world oriented bounds at a numeric time for the selected purposes.</summary>
    public UsdOrientedBounds3d GetWorldOrientedBounds(
        double timeCode,
        UsdGeomPurposeMask purposeMask = UsdGeomPurposeMask.All)
    {
        UsdPath.ValidateAbsolutePrimPath(Path, nameof(Path));
        return UsdOrientedBounds3d.FromNative(
            Stage.Native.GetWorldOrientedBounds(
                Path,
                UsdOrientedBounds3d.ValidatePurposeMask(purposeMask),
                UsdOrientedBounds3d.ValidateTimeCode(timeCode)));
    }

    /// <summary>Gets the composed defining, abstract, prototype, and specifier classification.</summary>
    public UsdPrimClassification GetClassification() =>
        UsdPrimClassification.FromNative(Stage.Native.GetPrimClassification(Path));

    /// <summary>Returns whether this prim is defined.</summary>
    public bool IsDefined() => GetClassification().IsDefined;

    /// <summary>Returns whether this prim is abstract.</summary>
    public bool IsAbstract() => GetClassification().IsAbstract;

    /// <summary>Returns whether this prim is inside a prototype.</summary>
    public bool IsInPrototype() => GetClassification().IsInPrototype;

    /// <summary>Gets this prim's authored specifier.</summary>
    public UsdPrimSpecifier GetSpecifier() => GetClassification().Specifier;

    /// <summary>Sets a custom double attribute at default time.</summary>
    public void SetDouble(string attributeName, double value) =>
        Stage.Native.SetDouble(Path, attributeName, value);

    /// <summary>Authors a time sample for a custom double attribute.</summary>
    public void SetDouble(string attributeName, double value, double timeCode) =>
        Stage.Native.SetDouble(Path, attributeName, value, timeCode);

    /// <summary>Gets a double attribute at default time.</summary>
    public double GetDouble(string attributeName) =>
        Stage.Native.GetDouble(Path, attributeName);

    /// <summary>Gets a sampled double attribute.</summary>
    public double GetDouble(string attributeName, double timeCode) =>
        Stage.Native.GetDouble(Path, attributeName, timeCode);

    /// <summary>Sets a custom double-array attribute at default time.</summary>
    public void SetDoubleArray(string attributeName, ReadOnlySpan<double> values) =>
        Stage.Native.SetDoubleArray(Path, attributeName, values);

    /// <summary>Authors a time sample for a custom double-array attribute.</summary>
    public void SetDoubleArray(
        string attributeName,
        ReadOnlySpan<double> values,
        double timeCode) =>
        Stage.Native.SetDoubleArray(Path, attributeName, values, timeCode);

    /// <summary>Gets a double-array attribute at default time.</summary>
    public double[] GetDoubleArray(string attributeName) =>
        Stage.Native.GetDoubleArray(Path, attributeName);

    /// <summary>Gets a sampled double-array attribute.</summary>
    public double[] GetDoubleArray(string attributeName, double timeCode) =>
        Stage.Native.GetDoubleArray(Path, attributeName, timeCode);

    /// <summary>Sets a custom matrix4d attribute at default time.</summary>
    public void SetMatrix4d(string attributeName, UsdMatrix4d value) =>
        Stage.Native.SetMatrix4d(Path, attributeName, value.ToNative());

    /// <summary>Authors a time sample for a custom matrix4d attribute.</summary>
    public void SetMatrix4d(string attributeName, UsdMatrix4d value, double timeCode) =>
        Stage.Native.SetMatrix4d(Path, attributeName, value.ToNative(), timeCode);

    /// <summary>Gets a matrix4d attribute at default time.</summary>
    public UsdMatrix4d GetMatrix4d(string attributeName) =>
        UsdMatrix4d.FromNative(Stage.Native.GetMatrix4d(Path, attributeName));

    /// <summary>Gets a sampled matrix4d attribute.</summary>
    public UsdMatrix4d GetMatrix4d(string attributeName, double timeCode) =>
        UsdMatrix4d.FromNative(Stage.Native.GetMatrix4d(Path, attributeName, timeCode));

    /// <summary>Sets an int32-array attribute at default time.</summary>
    public void SetInt32Array(string attributeName, ReadOnlySpan<int> values) =>
        Stage.Native.SetInt32Array(Path, attributeName, values);

    /// <summary>Authors a time sample for an int32-array attribute.</summary>
    public void SetInt32Array(
        string attributeName,
        ReadOnlySpan<int> values,
        double timeCode) =>
        Stage.Native.SetInt32Array(Path, attributeName, values, timeCode);

    /// <summary>Gets an int32-array attribute at default time.</summary>
    public int[] GetInt32Array(string attributeName) =>
        Stage.Native.GetInt32Array(Path, attributeName);

    /// <summary>Gets a sampled int32-array attribute.</summary>
    public int[] GetInt32Array(string attributeName, double timeCode) =>
        Stage.Native.GetInt32Array(Path, attributeName, timeCode);

    /// <summary>Sets a float-array attribute at default time.</summary>
    public void SetFloatArray(string attributeName, ReadOnlySpan<float> values) =>
        Stage.Native.SetFloatArray(Path, attributeName, values);

    /// <summary>Authors a time sample for a float-array attribute.</summary>
    public void SetFloatArray(
        string attributeName,
        ReadOnlySpan<float> values,
        double timeCode) =>
        Stage.Native.SetFloatArray(Path, attributeName, values, timeCode);

    /// <summary>Gets a float-array attribute at default time.</summary>
    public float[] GetFloatArray(string attributeName) =>
        Stage.Native.GetFloatArray(Path, attributeName);

    /// <summary>Gets a sampled float-array attribute.</summary>
    public float[] GetFloatArray(string attributeName, double timeCode) =>
        Stage.Native.GetFloatArray(Path, attributeName, timeCode);

    /// <summary>Sets a vec2f-array attribute at default time.</summary>
    public void SetVec2fArray(string attributeName, ReadOnlySpan<UsdVec2f> values) =>
        Stage.Native.SetVec2fArray(Path, attributeName, ToNative(values));

    /// <summary>Authors a time sample for a vec2f-array attribute.</summary>
    public void SetVec2fArray(
        string attributeName,
        ReadOnlySpan<UsdVec2f> values,
        double timeCode) =>
        Stage.Native.SetVec2fArray(Path, attributeName, ToNative(values), timeCode);

    /// <summary>Gets a vec2f-array attribute at default time.</summary>
    public UsdVec2f[] GetVec2fArray(string attributeName) =>
        FromNative(Stage.Native.GetVec2fArray(Path, attributeName));

    /// <summary>Gets a sampled vec2f-array attribute.</summary>
    public UsdVec2f[] GetVec2fArray(string attributeName, double timeCode) =>
        FromNative(Stage.Native.GetVec2fArray(Path, attributeName, timeCode));

    /// <summary>Sets a vec3f-array attribute at default time.</summary>
    public void SetVec3fArray(string attributeName, ReadOnlySpan<UsdVec3f> values) =>
        Stage.Native.SetVec3fArray(Path, attributeName, ToNative(values));

    /// <summary>Authors a time sample for a vec3f-array attribute.</summary>
    public void SetVec3fArray(
        string attributeName,
        ReadOnlySpan<UsdVec3f> values,
        double timeCode) =>
        Stage.Native.SetVec3fArray(Path, attributeName, ToNative(values), timeCode);

    /// <summary>Gets a vec3f-array attribute at default time.</summary>
    public UsdVec3f[] GetVec3fArray(string attributeName) =>
        FromNative(Stage.Native.GetVec3fArray(Path, attributeName));

    /// <summary>Gets a sampled vec3f-array attribute.</summary>
    public UsdVec3f[] GetVec3fArray(string attributeName, double timeCode) =>
        FromNative(Stage.Native.GetVec3fArray(Path, attributeName, timeCode));

    /// <summary>Sets a color3f-array attribute at default time.</summary>
    public void SetColor3fArray(string attributeName, ReadOnlySpan<UsdVec3f> values) =>
        Stage.Native.SetColor3fArray(Path, attributeName, ToNative(values));

    /// <summary>Authors a time sample for a color3f-array attribute.</summary>
    public void SetColor3fArray(
        string attributeName,
        ReadOnlySpan<UsdVec3f> values,
        double timeCode) =>
        Stage.Native.SetColor3fArray(Path, attributeName, ToNative(values), timeCode);

    /// <summary>Gets a color3f-array attribute at default time.</summary>
    public UsdVec3f[] GetColor3fArray(string attributeName) =>
        FromNative(Stage.Native.GetColor3fArray(Path, attributeName));

    /// <summary>Gets a sampled color3f-array attribute.</summary>
    public UsdVec3f[] GetColor3fArray(string attributeName, double timeCode) =>
        FromNative(Stage.Native.GetColor3fArray(Path, attributeName, timeCode));

    /// <summary>Sets a bool-array attribute at default time.</summary>
    public void SetBoolArray(string attributeName, ReadOnlySpan<bool> values) =>
        Stage.Native.SetBoolArray(Path, attributeName, values);

    /// <summary>Authors a time sample for a bool-array attribute.</summary>
    public void SetBoolArray(
        string attributeName,
        ReadOnlySpan<bool> values,
        double timeCode) =>
        Stage.Native.SetBoolArray(Path, attributeName, values, timeCode);

    /// <summary>Gets a bool-array attribute at default time.</summary>
    public bool[] GetBoolArray(string attributeName) =>
        Stage.Native.GetBoolArray(Path, attributeName);

    /// <summary>Gets a sampled bool-array attribute.</summary>
    public bool[] GetBoolArray(string attributeName, double timeCode) =>
        Stage.Native.GetBoolArray(Path, attributeName, timeCode);

    /// <summary>Sets a token-array attribute at default time.</summary>
    public void SetTokenArray(string attributeName, ReadOnlySpan<string> values) =>
        Stage.Native.SetTokenArray(Path, attributeName, values);

    /// <summary>Authors a time sample for a token-array attribute.</summary>
    public void SetTokenArray(
        string attributeName,
        ReadOnlySpan<string> values,
        double timeCode) =>
        Stage.Native.SetTokenArray(Path, attributeName, values, timeCode);

    /// <summary>Gets a token-array attribute at default time.</summary>
    public string[] GetTokenArray(string attributeName) =>
        Stage.Native.GetTokenArray(Path, attributeName);

    /// <summary>Gets a sampled token-array attribute.</summary>
    public string[] GetTokenArray(string attributeName, double timeCode) =>
        Stage.Native.GetTokenArray(Path, attributeName, timeCode);

    /// <summary>Sets a string-array attribute at default time.</summary>
    public void SetStringArray(string attributeName, ReadOnlySpan<string> values) =>
        Stage.Native.SetStringArray(Path, attributeName, values);

    /// <summary>Authors a time sample for a string-array attribute.</summary>
    public void SetStringArray(
        string attributeName,
        ReadOnlySpan<string> values,
        double timeCode) =>
        Stage.Native.SetStringArray(Path, attributeName, values, timeCode);

    /// <summary>Gets a string-array attribute at default time.</summary>
    public string[] GetStringArray(string attributeName) =>
        Stage.Native.GetStringArray(Path, attributeName);

    /// <summary>Gets a sampled string-array attribute.</summary>
    public string[] GetStringArray(string attributeName, double timeCode) =>
        Stage.Native.GetStringArray(Path, attributeName, timeCode);

    /// <summary>Sets a custom bool attribute at default time.</summary>
    public void SetBool(string attributeName, bool value) =>
        Stage.Native.SetBool(Path, attributeName, value);

    /// <summary>Authors a time sample for a custom bool attribute.</summary>
    public void SetBool(string attributeName, bool value, double timeCode) =>
        Stage.Native.SetBool(Path, attributeName, value, timeCode);

    /// <summary>Gets a bool attribute at default time.</summary>
    public bool GetBool(string attributeName) => Stage.Native.GetBool(Path, attributeName);

    /// <summary>Gets a sampled bool attribute.</summary>
    public bool GetBool(string attributeName, double timeCode) =>
        Stage.Native.GetBool(Path, attributeName, timeCode);

    /// <summary>Sets a custom int64 attribute at default time.</summary>
    public void SetInt64(string attributeName, long value) =>
        Stage.Native.SetInt64(Path, attributeName, value);

    /// <summary>Authors a time sample for a custom int64 attribute.</summary>
    public void SetInt64(string attributeName, long value, double timeCode) =>
        Stage.Native.SetInt64(Path, attributeName, value, timeCode);

    /// <summary>Gets an int64 attribute at default time.</summary>
    public long GetInt64(string attributeName) => Stage.Native.GetInt64(Path, attributeName);

    /// <summary>Gets a sampled int64 attribute.</summary>
    public long GetInt64(string attributeName, double timeCode) =>
        Stage.Native.GetInt64(Path, attributeName, timeCode);

    /// <summary>Sets a custom string attribute at default time.</summary>
    public void SetString(string attributeName, string value) =>
        Stage.Native.SetString(Path, attributeName, value);

    /// <summary>Authors a time sample for a custom string attribute.</summary>
    public void SetString(string attributeName, string value, double timeCode) =>
        Stage.Native.SetString(Path, attributeName, value, timeCode);

    /// <summary>Gets a string attribute at default time.</summary>
    public string GetString(string attributeName) => Stage.Native.GetString(Path, attributeName);

    /// <summary>Gets a sampled string attribute.</summary>
    public string GetString(string attributeName, double timeCode) =>
        Stage.Native.GetString(Path, attributeName, timeCode);

    /// <summary>Sets a custom token attribute at default time.</summary>
    public void SetToken(string attributeName, string value) =>
        Stage.Native.SetToken(Path, attributeName, value);

    /// <summary>Authors a time sample for a custom token attribute.</summary>
    public void SetToken(string attributeName, string value, double timeCode) =>
        Stage.Native.SetToken(Path, attributeName, value, timeCode);

    /// <summary>Gets a token attribute at default time.</summary>
    public string GetToken(string attributeName) => Stage.Native.GetToken(Path, attributeName);

    /// <summary>Gets a sampled token attribute.</summary>
    public string GetToken(string attributeName, double timeCode) =>
        Stage.Native.GetToken(Path, attributeName, timeCode);

    /// <summary>Sets a custom vec3f attribute at default time.</summary>
    public void SetVec3f(string attributeName, UsdVec3f value) =>
        Stage.Native.SetVec3f(Path, attributeName, value.ToNative());

    /// <summary>Authors a time sample for a custom vec3f attribute.</summary>
    public void SetVec3f(string attributeName, UsdVec3f value, double timeCode) =>
        Stage.Native.SetVec3f(Path, attributeName, value.ToNative(), timeCode);

    /// <summary>Gets a vec3f attribute at default time.</summary>
    public UsdVec3f GetVec3f(string attributeName) =>
        UsdVec3f.FromNative(Stage.Native.GetVec3f(Path, attributeName));

    /// <summary>Gets a sampled vec3f attribute.</summary>
    public UsdVec3f GetVec3f(string attributeName, double timeCode) =>
        UsdVec3f.FromNative(Stage.Native.GetVec3f(Path, attributeName, timeCode));

    /// <summary>Sets a custom color3f attribute at default time.</summary>
    public void SetColor3f(string attributeName, UsdVec3f value) =>
        Stage.Native.SetColor3f(Path, attributeName, value.ToNative());

    /// <summary>Authors a time sample for a custom color3f attribute.</summary>
    public void SetColor3f(string attributeName, UsdVec3f value, double timeCode) =>
        Stage.Native.SetColor3f(Path, attributeName, value.ToNative(), timeCode);

    /// <summary>Gets a color3f attribute at default time.</summary>
    public UsdVec3f GetColor3f(string attributeName) =>
        UsdVec3f.FromNative(Stage.Native.GetColor3f(Path, attributeName));

    /// <summary>Gets a sampled color3f attribute.</summary>
    public UsdVec3f GetColor3f(string attributeName, double timeCode) =>
        UsdVec3f.FromNative(Stage.Native.GetColor3f(Path, attributeName, timeCode));

    /// <summary>Attempts to author a tagged value on an existing compatible attribute at default time.</summary>
    public bool TrySetValue(string attributeName, in UsdScalarValue value) =>
        GetAttribute(attributeName).TrySet(value);

    /// <summary>Attempts to author a tagged value on an existing compatible attribute time sample.</summary>
    public bool TrySetValue(string attributeName, in UsdScalarValue value, double timeCode) =>
        GetAttribute(attributeName).TrySet(value, timeCode);

    /// <summary>Attempts to read a tagged value from an existing supported attribute at default time.</summary>
    public bool TryGetValue(string attributeName, out UsdScalarValue value) =>
        GetAttribute(attributeName).TryGetValue(out value);

    /// <summary>Attempts to read a tagged value from an existing supported attribute time sample.</summary>
    public bool TryGetValue(string attributeName, double timeCode, out UsdScalarValue value) =>
        GetAttribute(attributeName).TryGetValue(timeCode, out value);

    /// <summary>Returns whether this prim currently exists on its stage.</summary>
    public bool Exists() => Stage.Native.HasPrim(Path);

    /// <summary>Removes this prim and all of its descendants.</summary>
    public void Remove() => Stage.Native.RemovePrim(Path);

    /// <summary>Sets whether this prim is active.</summary>
    public void SetActive(bool active) => Stage.Native.SetPrimActive(Path, active);

    /// <summary>Gets whether this prim is active.</summary>
    public bool IsActive() => Stage.Native.GetPrimActive(Path);

    /// <summary>Sets whether this prim is instanceable.</summary>
    public void SetInstanceable(bool instanceable) => Stage.Native.SetInstanceable(Path, instanceable);

    /// <summary>Gets whether this prim is instanceable.</summary>
    public bool IsInstanceable() => Stage.Native.GetInstanceable(Path);

    /// <summary>Sets this prim's visibility, using the standard <c>UsdGeomImageable</c> token attribute.</summary>
    public void SetVisibility(string visibility) => SetToken("visibility", visibility);

    /// <summary>Gets this prim's visibility, using the standard <c>UsdGeomImageable</c> token attribute.</summary>
    public string GetVisibility() => GetToken("visibility");

    /// <summary>Sets this prim's purpose, using the standard <c>UsdGeomImageable</c> token attribute.</summary>
    public void SetPurpose(string purpose) => SetToken("purpose", purpose);

    /// <summary>Gets this prim's purpose, using the standard <c>UsdGeomImageable</c> token attribute.</summary>
    public string GetPurpose() => GetToken("purpose");

    /// <summary>Creates a relationship on this prim.</summary>
    public void CreateRelationship(string relationshipName) =>
        Stage.Native.CreateRelationship(Path, relationshipName);

    /// <summary>Replaces the targets of a relationship using one bulk native call.</summary>
    public void SetRelationshipTargets(string relationshipName, ReadOnlySpan<string> targets) =>
        Stage.Native.SetRelationshipTargets(Path, relationshipName, targets);

    /// <summary>Gets the composed targets of a relationship.</summary>
    public string[] GetRelationshipTargets(string relationshipName) =>
        Stage.Native.GetRelationshipTargets(Path, relationshipName);

    /// <summary>Clears the authored targets of a relationship.</summary>
    public void ClearRelationshipTargets(string relationshipName) =>
        Stage.Native.ClearRelationshipTargets(Path, relationshipName);

    /// <summary>Adds a reference to this prim.</summary>
    public void AddReference(string assetPath, string? targetPrimPath = null) =>
        Stage.Native.AddReference(Path, assetPath, targetPrimPath);

    /// <summary>Clears all authored references from this prim.</summary>
    public void ClearReferences() => Stage.Native.ClearReferences(Path);

    /// <summary>Adds a payload to this prim.</summary>
    public void AddPayload(string assetPath, string? targetPrimPath = null) =>
        Stage.Native.AddPayload(Path, assetPath, targetPrimPath);

    /// <summary>Clears all authored payloads from this prim.</summary>
    public void ClearPayloads() => Stage.Native.ClearPayloads(Path);

    /// <summary>
    /// Gets applied direct payload-list entries in deterministic OpenUSD composition order.
    /// </summary>
    /// <remarks>
    /// Each result carries the authored payload asset and target paths that introduce the composed
    /// arc. Results describe payload intent even when the payload is unloaded; they are not limited
    /// to currently instantiated Pcp nodes. Deleted list-op entries and ancestral payload arcs are
    /// not returned. An authored target omission is preserved as an empty string.
    /// </remarks>
    public IReadOnlyList<UsdPayloadArc> GetPayloadArcs()
    {
        UsdPath.ValidateAbsolutePrimPath(Path, nameof(Path));
        var nativeArcs = Stage.Native.GetComposedPayloadArcs(Path);
        if (nativeArcs.Length == 0)
        {
            return EmptyPayloadArcs;
        }

        var arcs = new UsdPayloadArc[nativeArcs.Length];
        for (int index = 0; index < arcs.Length; index++)
        {
            var nativeArc = nativeArcs[index];
            arcs[index] = new UsdPayloadArc(
                nativeArc.AssetPath,
                nativeArc.TargetPrimPath,
                nativeArc.SourceLayerIdentifier);
        }
        return Array.AsReadOnly(arcs);
    }

    /// <summary>Computes and returns a detached Pcp prim-index inspection snapshot.</summary>
    public PcpPrimIndex GetPrimIndex()
    {
        OpenUsd.Interop.OpenUsdNativePcpPrimIndex nativeIndex = Stage.Native.GetPcpPrimIndex(Path);
        var nodes = new PcpPrimIndexNode[nativeIndex.Nodes.Length];
        for (int index = 0; index < nodes.Length; index++)
        {
            OpenUsd.Interop.OpenUsdNativePcpNode node = nativeIndex.Nodes[index];
            nodes[index] = new PcpPrimIndexNode(
                node.ParentIndex,
                (PcpArcType)node.ArcType,
                node.IsCulled,
                node.IsInert,
                node.IsDueToAncestor,
                node.HasSpecs,
                node.CanContributeSpecs,
                node.NamespaceDepth,
                node.DepthBelowIntroduction,
                node.SiblingIndexAtOrigin,
                node.SitePath,
                node.IntroPath,
                node.PathAtIntroduction,
                node.PathAtOriginRootIntroduction,
                node.LayerStackIdentifier,
                Array.AsReadOnly(node.LayerIdentifiers));
        }
        return new PcpPrimIndex(
            Array.AsReadOnly(nodes),
            Array.AsReadOnly(nativeIndex.Errors));
    }

    /// <summary>Adds an inherit arc to an existing absolute prim path.</summary>
    public void AddInherit(string inheritedPrimPath)
    {
        UsdPath.ValidateAbsolutePrimPath(inheritedPrimPath, nameof(inheritedPrimPath));
        Stage.Native.AddInherit(Path, inheritedPrimPath);
    }

    /// <summary>Clears authored inherit arcs at the current edit target.</summary>
    public void ClearInherits() => Stage.Native.ClearInherits(Path);

    /// <summary>Adds a specialize arc to an existing absolute prim path.</summary>
    public void AddSpecialize(string specializedPrimPath)
    {
        UsdPath.ValidateAbsolutePrimPath(specializedPrimPath, nameof(specializedPrimPath));
        Stage.Native.AddSpecialize(Path, specializedPrimPath);
    }

    /// <summary>Clears authored specialize arcs at the current edit target.</summary>
    public void ClearSpecializes() => Stage.Native.ClearSpecializes(Path);

    /// <summary>Loads this prim, its ancestors, and its descendants.</summary>
    public void Load() => Stage.Native.LoadPrim(Path);

    /// <summary>Unloads this prim and its descendants.</summary>
    public void Unload() => Stage.Native.UnloadPrim(Path);

    /// <summary>Gets the composed load state for this prim.</summary>
    public bool IsLoaded() => Stage.Native.IsPrimLoaded(Path);

    /// <summary>Gets whether this prim is an instance.</summary>
    public bool IsInstance() => Stage.Native.IsPrimInstance(Path);

    /// <summary>Gets whether this prim is a prototype root.</summary>
    public bool IsPrototype() => Stage.Native.IsPrimPrototype(Path);

    /// <summary>Gets the prototype path for this instance prim.</summary>
    public string GetPrototypePath() => Stage.Native.GetPrimPrototypePath(Path);

    /// <summary>Creates a variant set on this prim, if necessary.</summary>
    public void AddVariantSet(string variantSetName) => Stage.Native.AddVariantSet(Path, variantSetName);

    /// <summary>
    /// Gets composed variant-set names in OpenUSD's deterministic strength and list-op order.
    /// </summary>
    public string[] GetVariantSetNames()
    {
        UsdPath.ValidateAbsolutePrimPath(Path, nameof(Path));
        return Stage.Native.GetVariantSetNames(Path);
    }

    /// <summary>Adds a variant to a variant set, creating the variant set if necessary.</summary>
    public void AddVariant(string variantSetName, string variantName) =>
        Stage.Native.AddVariant(Path, variantSetName, variantName);

    /// <summary>Authors a variant selection, or clears it when <paramref name="variantSelection"/> is null.</summary>
    public void SetVariantSelection(string variantSetName, string? variantSelection) =>
        Stage.Native.SetVariantSelection(Path, variantSetName, variantSelection);

    /// <summary>Gets the authored variant selection.</summary>
    public string GetVariantSelection(string variantSetName) =>
        Stage.Native.GetVariantSelection(Path, variantSetName);

    /// <summary>Gets the composed variant names for a variant set.</summary>
    public string[] GetVariantNames(string variantSetName) => Stage.Native.GetVariantNames(Path, variantSetName);

    /// <summary>Sets a string entry in this prim's customData dictionary.</summary>
    public void SetMetadata(string key, string value) => Stage.Native.SetMetadata(Path, key, value);

    /// <summary>Sets a bool entry in this prim's customData dictionary.</summary>
    public void SetMetadata(string key, bool value) => Stage.Native.SetMetadata(Path, key, value);

    /// <summary>Sets an int64 entry in this prim's customData dictionary.</summary>
    public void SetMetadata(string key, long value) => Stage.Native.SetMetadata(Path, key, value);

    /// <summary>Sets a double entry in this prim's customData dictionary.</summary>
    public void SetMetadata(string key, double value) => Stage.Native.SetMetadata(Path, key, value);

    /// <summary>Gets a string entry from this prim's customData dictionary.</summary>
    public string GetMetadataString(string key) => Stage.Native.GetMetadataString(Path, key);

    /// <summary>Gets a bool entry from this prim's customData dictionary.</summary>
    public bool GetMetadataBool(string key) => Stage.Native.GetMetadataBool(Path, key);

    /// <summary>Gets an int64 entry from this prim's customData dictionary.</summary>
    public long GetMetadataInt64(string key) => Stage.Native.GetMetadataInt64(Path, key);

    /// <summary>Gets a double entry from this prim's customData dictionary.</summary>
    public double GetMetadataDouble(string key) => Stage.Native.GetMetadataDouble(Path, key);

    /// <summary>Clears an entry from this prim's customData dictionary.</summary>
    public void ClearMetadata(string key) => Stage.Native.ClearMetadata(Path, key);

    internal UsdStage Stage =>
        _stage ?? throw new InvalidOperationException("The prim is not attached to a stage.");

    internal UsdStage OwningStage => Stage;

    private static OpenUsd.Interop.OpenUsdNativeVec2f[] ToNative(ReadOnlySpan<UsdVec2f> values)
    {
        var native = new OpenUsd.Interop.OpenUsdNativeVec2f[values.Length];
        for (int index = 0; index < values.Length; ++index)
        {
            native[index] = values[index].ToNative();
        }
        return native;
    }

    private static OpenUsd.Interop.OpenUsdNativeVec3f[] ToNative(ReadOnlySpan<UsdVec3f> values)
    {
        var native = new OpenUsd.Interop.OpenUsdNativeVec3f[values.Length];
        for (int index = 0; index < values.Length; ++index)
        {
            native[index] = values[index].ToNative();
        }
        return native;
    }

    private static UsdVec2f[] FromNative(OpenUsd.Interop.OpenUsdNativeVec2f[] values)
    {
        var managed = new UsdVec2f[values.Length];
        for (int index = 0; index < values.Length; ++index)
        {
            managed[index] = UsdVec2f.FromNative(values[index]);
        }
        return managed;
    }

    private static UsdVec3f[] FromNative(OpenUsd.Interop.OpenUsdNativeVec3f[] values)
    {
        var managed = new UsdVec3f[values.Length];
        for (int index = 0; index < values.Length; ++index)
        {
            managed[index] = UsdVec3f.FromNative(values[index]);
        }
        return managed;
    }
}
