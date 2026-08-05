// Copyright (c) marcschier. Licensed under the MIT License.

using Microsoft.Win32.SafeHandles;

namespace OpenUsd.Interop;

/// <summary>
/// Owns a native OpenUSD stage handle.
/// </summary>
internal sealed class OpenUsdNativeStage : SafeHandleZeroOrMinusOneIsInvalid
{
    internal OpenUsdNativeStage(nint handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    /// <summary>Gets the root layer identifier reported by OpenUSD.</summary>
    public string RootLayerIdentifier => OpenUsdNativeRuntime.GetRootLayerIdentifier(this);

    internal OpenUsdNativeStage Retain() => OpenUsdNativeRuntime.RetainStage(this);

    internal T WithAccess<T>(Func<T> action) =>
        OpenUsdNativeRuntime.WithStageAccess(this, action);

    internal void WithAccess(Action action) =>
        OpenUsdNativeRuntime.WithStageAccess(this, action);

    /// <summary>Gets the session layer identifier reported by OpenUSD.</summary>
    public string SessionLayerIdentifier => OpenUsdNativeRuntime.GetSessionLayerIdentifier(this);

    /// <summary>Gets the current edit-target layer identifier.</summary>
    public string EditTargetLayerIdentifier =>
        OpenUsdNativeRuntime.GetEditTargetLayerIdentifier(this);

    /// <summary>Sets the root layer as the current edit target.</summary>
    public void SetEditTargetToRootLayer() =>
        OpenUsdNativeRuntime.SetEditTargetToRootLayer(this);

    /// <summary>Sets the session layer as the current edit target.</summary>
    public void SetEditTargetToSessionLayer() =>
        OpenUsdNativeRuntime.SetEditTargetToSessionLayer(this);

    /// <summary>Sets an owned local layer as the current edit target.</summary>
    public void SetEditTarget(OpenUsdNativeLayer layer) =>
        OpenUsdNativeRuntime.SetEditTarget(this, layer);

    /// <summary>Gets local layer-stack identifiers in strong-to-weak order.</summary>
    public string[] GetLayerStackIdentifiers() =>
        OpenUsdNativeRuntime.GetLayerStackIdentifiers(this);

    /// <summary>Mutes a local layer by identifier.</summary>
    public void MuteLayer(string layerIdentifier) =>
        OpenUsdNativeRuntime.MuteLayer(this, layerIdentifier);

    /// <summary>Unmutes a layer by identifier.</summary>
    public void UnmuteLayer(string layerIdentifier) =>
        OpenUsdNativeRuntime.UnmuteLayer(this, layerIdentifier);

    /// <summary>Returns whether a known layer identifier is muted.</summary>
    public bool IsLayerMuted(string layerIdentifier) =>
        OpenUsdNativeRuntime.IsLayerMuted(this, layerIdentifier);

    /// <summary>Gets an owned handle to the stage's root layer.</summary>
    public OpenUsdNativeLayer GetRootLayer() => OpenUsdNativeRuntime.GetRootLayer(this);

    /// <summary>Gets an owned handle to the stage's session layer.</summary>
    public OpenUsdNativeLayer GetSessionLayer() => OpenUsdNativeRuntime.GetSessionLayer(this);

    /// <summary>Gets the serial incremented by OpenUSD object-change notices.</summary>
    public ulong ChangeSerial => OpenUsdNativeRuntime.GetChangeSerial(this);

    /// <summary>Gets or sets the composed start time code.</summary>
    public double StartTimeCode
    {
        get => OpenUsdNativeRuntime.GetStartTimeCode(this);
        set => OpenUsdNativeRuntime.SetStartTimeCode(this, value);
    }

    /// <summary>Gets or sets the composed end time code.</summary>
    public double EndTimeCode
    {
        get => OpenUsdNativeRuntime.GetEndTimeCode(this);
        set => OpenUsdNativeRuntime.SetEndTimeCode(this, value);
    }

    /// <summary>Gets or sets the advisory playback rate.</summary>
    public double FramesPerSecond
    {
        get => OpenUsdNativeRuntime.GetFramesPerSecond(this);
        set => OpenUsdNativeRuntime.SetFramesPerSecond(this, value);
    }

    /// <summary>Gets or sets the number of time codes per second.</summary>
    public double TimeCodesPerSecond
    {
        get => OpenUsdNativeRuntime.GetTimeCodesPerSecond(this);
        set => OpenUsdNativeRuntime.SetTimeCodesPerSecond(this, value);
    }

    /// <summary>Gets the path of the valid default prim.</summary>
    public string GetDefaultPrimPath() => OpenUsdNativeRuntime.GetDefaultPrimPath(this);

    /// <summary>Sets the default prim by absolute path.</summary>
    public void SetDefaultPrim(string primPath) => OpenUsdNativeRuntime.SetDefaultPrim(this, primPath);

    /// <summary>Clears the authored default prim.</summary>
    public void ClearDefaultPrim() => OpenUsdNativeRuntime.ClearDefaultPrim(this);

    /// <summary>Saves the root layer.</summary>
    public void Save() => OpenUsdNativeRuntime.SaveStage(this);

    /// <summary>Reloads all non-session layers contributing to the stage.</summary>
    public void Reload() => OpenUsdNativeRuntime.ReloadStage(this);

    /// <summary>Exports the composed stage as a flattened layer.</summary>
    public void Export(string path) => OpenUsdNativeRuntime.ExportStage(this, path);

    /// <summary>Defines a prim at an absolute path.</summary>
    /// <param name="primPath">The absolute prim path.</param>
    /// <param name="typeName">The optional schema type name.</param>
    public void DefinePrim(string primPath, string? typeName = null) =>
        OpenUsdNativeRuntime.DefinePrim(this, primPath, typeName);

    /// <summary>Authors an override prim at an absolute path.</summary>
    public void OverridePrim(string primPath) => OpenUsdNativeRuntime.OverridePrim(this, primPath);

    /// <summary>Authors a class prim at an absolute root prim path.</summary>
    public void CreateClassPrim(string primPath) => OpenUsdNativeRuntime.CreateClassPrim(this, primPath);

    /// <summary>Gets all composed prim paths in traversal order.</summary>
    public string[] GetPrimPaths() => OpenUsdNativeRuntime.GetPrimPaths(this);

    /// <summary>Gets a prim's composed authored type name.</summary>
    public string GetPrimTypeName(string primPath) =>
        OpenUsdNativeRuntime.GetPrimTypeName(this, primPath);

    /// <summary>Gets the API schema names applied to a prim using one bulk native call.</summary>
    public string[] GetPrimAppliedSchemas(string primPath) =>
        OpenUsdNativeRuntime.GetPrimAppliedSchemas(this, primPath);

    /// <summary>Gets all direct child prim paths using one bulk native call.</summary>
    public string[] GetPrimChildPaths(string primPath) =>
        OpenUsdNativeRuntime.GetPrimChildPaths(this, primPath);

    /// <summary>Gets composed attribute names using one bulk native call.</summary>
    public string[] GetPrimAttributeNames(string primPath) =>
        OpenUsdNativeRuntime.GetPrimAttributeNames(this, primPath);

    /// <summary>Gets composed relationship names using one bulk native call.</summary>
    public string[] GetPrimRelationshipNames(string primPath) =>
        OpenUsdNativeRuntime.GetPrimRelationshipNames(this, primPath);

    /// <summary>Gets an attribute's declared USD type name.</summary>
    public string GetAttributeTypeName(string primPath, string attributeName) =>
        OpenUsdNativeRuntime.GetAttributeTypeName(this, primPath, attributeName);

    /// <summary>Gets authored and blocked value state at default time.</summary>
    public OpenUsdNativeAttributeValueState GetAttributeValueState(
        string primPath,
        string attributeName) =>
        OpenUsdNativeRuntime.GetAttributeValueState(this, primPath, attributeName, null);

    /// <summary>Gets authored and blocked value state at a numeric time code.</summary>
    public OpenUsdNativeAttributeValueState GetAttributeValueState(
        string primPath,
        string attributeName,
        double timeCode) =>
        OpenUsdNativeRuntime.GetAttributeValueState(this, primPath, attributeName, timeCode);

    /// <summary>Gets sorted authored time samples using a bulk native buffer.</summary>
    public double[] GetAttributeTimeSamples(string primPath, string attributeName) =>
        OpenUsdNativeRuntime.GetAttributeTimeSamples(this, primPath, attributeName);

    public bool AttributeHasSpline(string primPath, string attributeName) =>
        OpenUsdNativeRuntime.AttributeHasSpline(this, primPath, attributeName);

    public nint GetAttributeSpline(string primPath, string attributeName) =>
        OpenUsdNativeRuntime.GetAttributeSpline(this, primPath, attributeName);

    public void SetAttributeSpline(string primPath, string attributeName, nint spline) =>
        OpenUsdNativeRuntime.SetAttributeSpline(this, primPath, attributeName, spline);

    /// <summary>Clears all authored values for an attribute at the current edit target.</summary>
    public void ClearAttributeValue(string primPath, string attributeName) =>
        OpenUsdNativeRuntime.ClearAttributeValue(this, primPath, attributeName);

    /// <summary>Blocks weaker values for an attribute at the current edit target.</summary>
    public void BlockAttributeValue(string primPath, string attributeName) =>
        OpenUsdNativeRuntime.BlockAttributeValue(this, primPath, attributeName);

    /// <summary>Gets an explicitly tagged supported scalar at default time.</summary>
    public OpenUsdNativeScalarResult GetAttributeScalarValue(
        string primPath,
        string attributeName) =>
        OpenUsdNativeRuntime.GetAttributeScalarValue(this, primPath, attributeName, null);

    /// <summary>Gets an explicitly tagged supported scalar at a numeric time code.</summary>
    public OpenUsdNativeScalarResult GetAttributeScalarValue(
        string primPath,
        string attributeName,
        double timeCode) =>
        OpenUsdNativeRuntime.GetAttributeScalarValue(this, primPath, attributeName, timeCode);

    /// <summary>Sets a custom double attribute.</summary>
    public void SetDouble(
        string primPath,
        string attributeName,
        double value,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetDouble(this, primPath, attributeName, value, timeCode);

    /// <summary>Gets a double attribute.</summary>
    public double GetDouble(
        string primPath,
        string attributeName,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetDouble(this, primPath, attributeName, timeCode);

    /// <summary>Sets a custom double-array attribute using one bulk native call.</summary>
    public void SetDoubleArray(
        string primPath,
        string attributeName,
        ReadOnlySpan<double> values,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetDoubleArray(this, primPath, attributeName, values, timeCode);

    /// <summary>Gets a double-array attribute using a two-call bulk buffer transfer.</summary>
    public double[] GetDoubleArray(
        string primPath,
        string attributeName,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetDoubleArray(this, primPath, attributeName, timeCode);

    /// <summary>Sets a custom matrix4d attribute.</summary>
    public void SetMatrix4d(
        string primPath,
        string attributeName,
        OpenUsdNativeMatrix4d value,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetMatrix4d(this, primPath, attributeName, value, timeCode);

    /// <summary>Gets a matrix4d attribute.</summary>
    public OpenUsdNativeMatrix4d GetMatrix4d(
        string primPath,
        string attributeName,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetMatrix4d(this, primPath, attributeName, timeCode);

    /// <summary>Sets an int32-array attribute using one bulk native call.</summary>
    public void SetInt32Array(
        string primPath,
        string attributeName,
        ReadOnlySpan<int> values,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetInt32Array(this, primPath, attributeName, values, timeCode);

    /// <summary>Gets an int32-array attribute using a two-call bulk buffer transfer.</summary>
    public int[] GetInt32Array(
        string primPath,
        string attributeName,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetInt32Array(this, primPath, attributeName, timeCode);

    /// <summary>Sets a float-array attribute using one bulk native call.</summary>
    public void SetFloatArray(
        string primPath,
        string attributeName,
        ReadOnlySpan<float> values,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetFloatArray(this, primPath, attributeName, values, timeCode);

    /// <summary>Gets a float-array attribute using a two-call bulk buffer transfer.</summary>
    public float[] GetFloatArray(
        string primPath,
        string attributeName,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetFloatArray(this, primPath, attributeName, timeCode);

    /// <summary>Sets a vec2f-array attribute using one bulk native call.</summary>
    public void SetVec2fArray(
        string primPath,
        string attributeName,
        ReadOnlySpan<OpenUsdNativeVec2f> values,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetVec2fArray(this, primPath, attributeName, values, timeCode);

    /// <summary>Gets a vec2f-array attribute using a two-call bulk buffer transfer.</summary>
    public OpenUsdNativeVec2f[] GetVec2fArray(
        string primPath,
        string attributeName,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetVec2fArray(this, primPath, attributeName, timeCode);

    /// <summary>Sets a vec3f-array attribute using one bulk native call.</summary>
    public void SetVec3fArray(
        string primPath,
        string attributeName,
        ReadOnlySpan<OpenUsdNativeVec3f> values,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetVec3fArray(this, primPath, attributeName, values, timeCode);

    /// <summary>Gets a vec3f-array attribute using a two-call bulk buffer transfer.</summary>
    public OpenUsdNativeVec3f[] GetVec3fArray(
        string primPath,
        string attributeName,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetVec3fArray(this, primPath, attributeName, timeCode);

    /// <summary>Sets a color3f-array attribute using one bulk native call.</summary>
    public void SetColor3fArray(
        string primPath,
        string attributeName,
        ReadOnlySpan<OpenUsdNativeVec3f> values,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetColor3fArray(this, primPath, attributeName, values, timeCode);

    /// <summary>Gets a color3f-array attribute using a two-call bulk buffer transfer.</summary>
    public OpenUsdNativeVec3f[] GetColor3fArray(
        string primPath,
        string attributeName,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetColor3fArray(this, primPath, attributeName, timeCode);

    /// <summary>Sets a bool-array attribute using one bulk native call.</summary>
    public void SetBoolArray(
        string primPath,
        string attributeName,
        ReadOnlySpan<bool> values,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetBoolArray(this, primPath, attributeName, values, timeCode);

    /// <summary>Gets a bool-array attribute using a two-call bulk buffer transfer.</summary>
    public bool[] GetBoolArray(
        string primPath,
        string attributeName,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetBoolArray(this, primPath, attributeName, timeCode);

    /// <summary>Sets a token-array attribute using one packed native string-list transfer.</summary>
    public void SetTokenArray(
        string primPath,
        string attributeName,
        ReadOnlySpan<string> values,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetTokenArray(this, primPath, attributeName, values, timeCode);

    /// <summary>Gets a token-array attribute using one packed native string-list transfer.</summary>
    public string[] GetTokenArray(
        string primPath,
        string attributeName,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetTokenArray(this, primPath, attributeName, timeCode);

    /// <summary>Sets a string-array attribute using one packed native string-list transfer.</summary>
    public void SetStringArray(
        string primPath,
        string attributeName,
        ReadOnlySpan<string> values,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetStringArray(this, primPath, attributeName, values, timeCode);

    /// <summary>Gets a string-array attribute using one packed native string-list transfer.</summary>
    public string[] GetStringArray(
        string primPath,
        string attributeName,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetStringArray(this, primPath, attributeName, timeCode);

    internal OpenUsdNativeBounds3d GetWorldBounds(
        string? targetPrimPath,
        uint purposeMask,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetWorldBounds(this, targetPrimPath, purposeMask, timeCode);

    internal OpenUsdNativeOrientedBounds3d GetWorldOrientedBounds(
        string? targetPrimPath,
        uint purposeMask,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetWorldOrientedBounds(this, targetPrimPath, purposeMask, timeCode);

    /// <summary>Returns whether a prim conforms to a focused UsdGeom schema kind.</summary>
    public bool IsGeomSchema(string primPath, int schemaKind) =>
        OpenUsdNativeRuntime.IsGeomSchema(this, primPath, schemaKind);

    /// <summary>Defines a UsdGeomXform prim.</summary>
    public void DefineGeomXform(string primPath) =>
        OpenUsdNativeRuntime.DefineGeomXform(this, primPath);

    /// <summary>Defines a UsdGeomMesh prim.</summary>
    public void DefineGeomMesh(string primPath) =>
        OpenUsdNativeRuntime.DefineGeomMesh(this, primPath);

    /// <summary>Defines a UsdGeomCamera prim.</summary>
    public void DefineGeomCamera(string primPath) =>
        OpenUsdNativeRuntime.DefineGeomCamera(this, primPath);

    /// <summary>Defines a concrete UsdGeom schema prim.</summary>
    public void DefineGeomSchema(string primPath, int schemaKind) =>
        OpenUsdNativeRuntime.DefineGeomSchema(this, primPath, schemaKind);

    /// <summary>Sets an exact int UsdGeom schema attribute.</summary>
    public void SetGeomInt32(
        string primPath,
        string attributeName,
        int value,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetGeomInt32(this, primPath, attributeName, value, timeCode);

    /// <summary>Gets an exact int UsdGeom schema attribute.</summary>
    public int GetGeomInt32(
        string primPath,
        string attributeName,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetGeomInt32(this, primPath, attributeName, timeCode);

    /// <summary>Authors UsdGeomPointInstancer orientations using one bulk native call.</summary>
    public void SetGeomPointInstancerOrientations(
        string primPath,
        ReadOnlySpan<OpenUsdNativeQuatf> values) =>
        OpenUsdNativeRuntime.SetGeomPointInstancerOrientations(this, primPath, values);

    /// <summary>Gets UsdGeomPointInstancer orientations using a two-call bulk buffer transfer.</summary>
    public OpenUsdNativeQuatf[] GetGeomPointInstancerOrientations(string primPath) =>
        OpenUsdNativeRuntime.GetGeomPointInstancerOrientations(this, primPath);

    /// <summary>Authors UsdGeomImageable visibility.</summary>
    public void SetGeomVisibility(
        string primPath,
        int visibility,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetGeomVisibility(this, primPath, visibility, timeCode);

    /// <summary>Computes UsdGeomImageable visibility.</summary>
    public int GetGeomVisibility(string primPath, double? timeCode = null) =>
        OpenUsdNativeRuntime.GetGeomVisibility(this, primPath, timeCode);

    /// <summary>Authors UsdGeomImageable purpose.</summary>
    public void SetGeomPurpose(string primPath, int purpose) =>
        OpenUsdNativeRuntime.SetGeomPurpose(this, primPath, purpose);

    /// <summary>Gets UsdGeomImageable purpose.</summary>
    public int GetGeomPurpose(string primPath) =>
        OpenUsdNativeRuntime.GetGeomPurpose(this, primPath);

    /// <summary>Authors one matrix transform operation on a UsdGeomXformable.</summary>
    public void SetGeomLocalTransform(
        string primPath,
        OpenUsdNativeMatrix4d value,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetGeomLocalTransform(this, primPath, value, timeCode);

    /// <summary>Computes the local transform of a UsdGeomXformable.</summary>
    public OpenUsdNativeMatrix4d GetGeomLocalTransform(
        string primPath,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetGeomLocalTransform(this, primPath, timeCode);

    /// <summary>Computes the local-to-world transform of a UsdGeomXformable.</summary>
    public OpenUsdNativeMatrix4d GetGeomWorldTransform(
        string primPath,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetGeomWorldTransform(this, primPath, timeCode);

    /// <summary>Sets whether a UsdGeomXformable resets its inherited transform stack.</summary>
    public void SetGeomResetXformStack(string primPath, bool reset) =>
        OpenUsdNativeRuntime.SetGeomResetXformStack(this, primPath, reset);

    /// <summary>Gets whether a UsdGeomXformable resets its inherited transform stack.</summary>
    public bool GetGeomResetXformStack(string primPath) =>
        OpenUsdNativeRuntime.GetGeomResetXformStack(this, primPath);

    /// <summary>Authors UsdGeomMesh points with one bulk transfer.</summary>
    public void SetGeomMeshPoints(
        string primPath,
        ReadOnlySpan<OpenUsdNativeVec3f> values,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetGeomMeshPoints(this, primPath, values, timeCode);

    /// <summary>Gets UsdGeomMesh points with one bulk data transfer.</summary>
    public OpenUsdNativeVec3f[] GetGeomMeshPoints(
        string primPath,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetGeomMeshPoints(this, primPath, timeCode);

    /// <summary>Authors validated UsdGeomMesh topology using two contiguous input buffers.</summary>
    public void SetGeomMeshTopology(
        string primPath,
        ReadOnlySpan<int> faceVertexCounts,
        ReadOnlySpan<int> faceVertexIndices) =>
        OpenUsdNativeRuntime.SetGeomMeshTopology(
            this, primPath, faceVertexCounts, faceVertexIndices);

    /// <summary>Gets UsdGeomMesh face vertex counts.</summary>
    public int[] GetGeomMeshFaceVertexCounts(string primPath) =>
        OpenUsdNativeRuntime.GetGeomMeshFaceVertexCounts(this, primPath);

    /// <summary>Gets UsdGeomMesh face vertex indices.</summary>
    public int[] GetGeomMeshFaceVertexIndices(string primPath) =>
        OpenUsdNativeRuntime.GetGeomMeshFaceVertexIndices(this, primPath);

    /// <summary>Authors UsdGeomMesh normals and interpolation.</summary>
    public void SetGeomMeshNormals(
        string primPath,
        ReadOnlySpan<OpenUsdNativeVec3f> values,
        int interpolation,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetGeomMeshNormals(
            this, primPath, values, interpolation, timeCode);

    /// <summary>Gets UsdGeomMesh normals.</summary>
    public OpenUsdNativeVec3f[] GetGeomMeshNormals(
        string primPath,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetGeomMeshNormals(this, primPath, timeCode);

    /// <summary>Sets UsdGeomMesh normals interpolation.</summary>
    public void SetGeomMeshNormalsInterpolation(string primPath, int interpolation) =>
        OpenUsdNativeRuntime.SetGeomMeshNormalsInterpolation(this, primPath, interpolation);

    /// <summary>Gets UsdGeomMesh normals interpolation.</summary>
    public int GetGeomMeshNormalsInterpolation(string primPath) =>
        OpenUsdNativeRuntime.GetGeomMeshNormalsInterpolation(this, primPath);

    /// <summary>Sets the UsdGeomMesh subdivision scheme.</summary>
    public void SetGeomMeshSubdivisionScheme(string primPath, int scheme) =>
        OpenUsdNativeRuntime.SetGeomMeshSubdivisionScheme(this, primPath, scheme);

    /// <summary>Gets the UsdGeomMesh subdivision scheme.</summary>
    public int GetGeomMeshSubdivisionScheme(string primPath) =>
        OpenUsdNativeRuntime.GetGeomMeshSubdivisionScheme(this, primPath);

    /// <summary>Sets UsdGeomMesh orientation.</summary>
    public void SetGeomMeshOrientation(string primPath, int orientation) =>
        OpenUsdNativeRuntime.SetGeomMeshOrientation(this, primPath, orientation);

    /// <summary>Gets UsdGeomMesh orientation.</summary>
    public int GetGeomMeshOrientation(string primPath) =>
        OpenUsdNativeRuntime.GetGeomMeshOrientation(this, primPath);

    /// <summary>Sets UsdGeomMesh double-sided state.</summary>
    public void SetGeomMeshDoubleSided(string primPath, bool doubleSided) =>
        OpenUsdNativeRuntime.SetGeomMeshDoubleSided(this, primPath, doubleSided);

    /// <summary>Gets UsdGeomMesh double-sided state.</summary>
    public bool GetGeomMeshDoubleSided(string primPath) =>
        OpenUsdNativeRuntime.GetGeomMeshDoubleSided(this, primPath);

    /// <summary>Authors a UsdGeomMesh extent.</summary>
    public void SetGeomMeshExtent(
        string primPath,
        OpenUsdNativeExtent3f extent,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetGeomMeshExtent(this, primPath, extent, timeCode);

    /// <summary>Gets a UsdGeomMesh extent.</summary>
    public OpenUsdNativeExtent3f GetGeomMeshExtent(
        string primPath,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetGeomMeshExtent(this, primPath, timeCode);

    /// <summary>Sets UsdGeomCamera projection.</summary>
    public void SetGeomCameraProjection(string primPath, int projection) =>
        OpenUsdNativeRuntime.SetGeomCameraProjection(this, primPath, projection);

    /// <summary>Gets UsdGeomCamera projection.</summary>
    public int GetGeomCameraProjection(string primPath) =>
        OpenUsdNativeRuntime.GetGeomCameraProjection(this, primPath);

    /// <summary>Sets a focused UsdGeomCamera float property.</summary>
    public void SetGeomCameraFloat(string primPath, int property, float value) =>
        OpenUsdNativeRuntime.SetGeomCameraFloat(this, primPath, property, value);

    /// <summary>Gets a focused UsdGeomCamera float property.</summary>
    public float GetGeomCameraFloat(string primPath, int property) =>
        OpenUsdNativeRuntime.GetGeomCameraFloat(this, primPath, property);

    /// <summary>Sets UsdGeomCamera clipping range.</summary>
    public void SetGeomCameraClippingRange(string primPath, OpenUsdNativeVec2f value) =>
        OpenUsdNativeRuntime.SetGeomCameraClippingRange(this, primPath, value);

    /// <summary>Gets UsdGeomCamera clipping range.</summary>
    public OpenUsdNativeVec2f GetGeomCameraClippingRange(string primPath) =>
        OpenUsdNativeRuntime.GetGeomCameraClippingRange(this, primPath);

    /// <summary>Gets one detached time-sampled UsdGeomCamera state.</summary>
    internal OpenUsdNativeCameraState GetGeomCameraState(
        string primPath,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetGeomCameraState(this, primPath, timeCode);

    /// <summary>Sets a custom bool attribute.</summary>
    public void SetBool(
        string primPath,
        string attributeName,
        bool value,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetBool(this, primPath, attributeName, value, timeCode);

    /// <summary>Gets a bool attribute.</summary>
    public bool GetBool(
        string primPath,
        string attributeName,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetBool(this, primPath, attributeName, timeCode);

    /// <summary>Sets a custom int64 attribute.</summary>
    public void SetInt64(
        string primPath,
        string attributeName,
        long value,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetInt64(this, primPath, attributeName, value, timeCode);

    /// <summary>Gets an int64 attribute.</summary>
    public long GetInt64(
        string primPath,
        string attributeName,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetInt64(this, primPath, attributeName, timeCode);

    /// <summary>Sets a custom string attribute.</summary>
    public void SetString(
        string primPath,
        string attributeName,
        string value,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetStringAttribute(this, primPath, attributeName, value, timeCode);

    /// <summary>Gets a string attribute.</summary>
    public string GetString(
        string primPath,
        string attributeName,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetStringAttribute(this, primPath, attributeName, timeCode);

    /// <summary>Sets a custom token attribute.</summary>
    public void SetToken(
        string primPath,
        string attributeName,
        string value,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetTokenAttribute(this, primPath, attributeName, value, timeCode);

    /// <summary>Gets a token attribute.</summary>
    public string GetToken(
        string primPath,
        string attributeName,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetTokenAttribute(this, primPath, attributeName, timeCode);

    /// <summary>Sets a custom vec3f attribute.</summary>
    public void SetVec3f(
        string primPath,
        string attributeName,
        OpenUsdNativeVec3f value,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetVec3f(this, primPath, attributeName, value, timeCode);

    /// <summary>Gets a vec3f attribute.</summary>
    public OpenUsdNativeVec3f GetVec3f(
        string primPath,
        string attributeName,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetVec3f(this, primPath, attributeName, timeCode);

    /// <summary>Sets a custom color3f attribute.</summary>
    public void SetColor3f(
        string primPath,
        string attributeName,
        OpenUsdNativeVec3f value,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetColor3f(this, primPath, attributeName, value, timeCode);

    /// <summary>Gets a color3f attribute.</summary>
    public OpenUsdNativeVec3f GetColor3f(
        string primPath,
        string attributeName,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetColor3f(this, primPath, attributeName, timeCode);

    /// <summary>Returns whether a prim exists at the given absolute path.</summary>
    public bool HasPrim(string primPath) => OpenUsdNativeRuntime.HasPrim(this, primPath);

    /// <summary>Removes a prim and all of its descendants.</summary>
    public void RemovePrim(string primPath) => OpenUsdNativeRuntime.RemovePrim(this, primPath);

    /// <summary>Sets a prim's active state.</summary>
    public void SetPrimActive(string primPath, bool active) =>
        OpenUsdNativeRuntime.SetPrimActive(this, primPath, active);

    /// <summary>Gets a prim's active state.</summary>
    public bool GetPrimActive(string primPath) => OpenUsdNativeRuntime.GetPrimActive(this, primPath);

    public OpenUsdNativePrimClassification GetPrimClassification(string primPath) =>
        OpenUsdNativeRuntime.GetPrimClassification(this, primPath);

    /// <summary>Creates a relationship at the given prim path.</summary>
    public void CreateRelationship(string primPath, string relationshipName) =>
        OpenUsdNativeRuntime.CreateRelationship(this, primPath, relationshipName);

    /// <summary>Replaces the targets of a relationship using one bulk native call.</summary>
    public void SetRelationshipTargets(
        string primPath,
        string relationshipName,
        ReadOnlySpan<string> targets) =>
        OpenUsdNativeRuntime.SetRelationshipTargets(this, primPath, relationshipName, targets);

    /// <summary>Gets the composed targets of a relationship.</summary>
    public string[] GetRelationshipTargets(string primPath, string relationshipName) =>
        OpenUsdNativeRuntime.GetRelationshipTargets(this, primPath, relationshipName);

    /// <summary>Clears the authored targets of a relationship.</summary>
    public void ClearRelationshipTargets(string primPath, string relationshipName) =>
        OpenUsdNativeRuntime.ClearRelationshipTargets(this, primPath, relationshipName);

    /// <summary>Adds a reference to a prim.</summary>
    public void AddReference(string primPath, string assetPath, string? targetPrimPath = null) =>
        OpenUsdNativeRuntime.AddReference(this, primPath, assetPath, targetPrimPath);

    /// <summary>Clears all authored references from a prim.</summary>
    public void ClearReferences(string primPath) => OpenUsdNativeRuntime.ClearReferences(this, primPath);

    /// <summary>Adds a payload to a prim.</summary>
    public void AddPayload(string primPath, string assetPath, string? targetPrimPath = null) =>
        OpenUsdNativeRuntime.AddPayload(this, primPath, assetPath, targetPrimPath);

    /// <summary>Clears all authored payloads from a prim.</summary>
    public void ClearPayloads(string primPath) => OpenUsdNativeRuntime.ClearPayloads(this, primPath);

    internal OpenUsdNativePayloadArc[] GetComposedPayloadArcs(string primPath) =>
        OpenUsdNativeRuntime.GetComposedPayloadArcs(this, primPath);

    internal OpenUsdNativePcpPrimIndex GetPcpPrimIndex(string primPath) =>
        OpenUsdNativeRuntime.GetPcpPrimIndex(this, primPath);

    internal OpenUsdNativeValidationError[] ValidateStage() =>
        OpenUsdNativeRuntime.ValidateStage(this);

    internal OpenUsdNativeValidationError[] ValidatePrim(string primPath) =>
        OpenUsdNativeRuntime.ValidatePrim(this, primPath);

    /// <summary>Adds an inherit arc to an existing prim path.</summary>
    public void AddInherit(string primPath, string inheritedPrimPath) =>
        OpenUsdNativeRuntime.AddInherit(this, primPath, inheritedPrimPath);

    /// <summary>Clears authored inherit arcs at the current edit target.</summary>
    public void ClearInherits(string primPath) =>
        OpenUsdNativeRuntime.ClearInherits(this, primPath);

    /// <summary>Adds a specialize arc to an existing prim path.</summary>
    public void AddSpecialize(string primPath, string specializedPrimPath) =>
        OpenUsdNativeRuntime.AddSpecialize(this, primPath, specializedPrimPath);

    /// <summary>Clears authored specialize arcs at the current edit target.</summary>
    public void ClearSpecializes(string primPath) =>
        OpenUsdNativeRuntime.ClearSpecializes(this, primPath);

    /// <summary>Loads a prim and its descendants.</summary>
    public void LoadPrim(string primPath) => OpenUsdNativeRuntime.LoadPrim(this, primPath);

    /// <summary>Unloads a prim and its descendants.</summary>
    public void UnloadPrim(string primPath) => OpenUsdNativeRuntime.UnloadPrim(this, primPath);

    /// <summary>Gets the composed load state for a prim.</summary>
    public bool IsPrimLoaded(string primPath) =>
        OpenUsdNativeRuntime.IsPrimLoaded(this, primPath);

    /// <summary>Sets whether a prim is instanceable.</summary>
    public void SetInstanceable(string primPath, bool instanceable) =>
        OpenUsdNativeRuntime.SetInstanceable(this, primPath, instanceable);

    /// <summary>Gets whether a prim is instanceable.</summary>
    public bool GetInstanceable(string primPath) => OpenUsdNativeRuntime.GetInstanceable(this, primPath);

    /// <summary>Gets whether a prim is an instance.</summary>
    public bool IsPrimInstance(string primPath) =>
        OpenUsdNativeRuntime.IsPrimInstance(this, primPath);

    /// <summary>Gets whether a prim is a prototype root.</summary>
    public bool IsPrimPrototype(string primPath) =>
        OpenUsdNativeRuntime.IsPrimPrototype(this, primPath);

    /// <summary>Gets the prototype path for an instance prim.</summary>
    public string GetPrimPrototypePath(string primPath) =>
        OpenUsdNativeRuntime.GetPrimPrototypePath(this, primPath);

    /// <summary>Creates a variant set on a prim, if necessary.</summary>
    public void AddVariantSet(string primPath, string variantSetName) =>
        OpenUsdNativeRuntime.AddVariantSet(this, primPath, variantSetName);

    internal string[] GetVariantSetNames(string primPath) =>
        OpenUsdNativeRuntime.GetVariantSetNames(this, primPath);

    /// <summary>Adds a variant to a variant set, creating the variant set if necessary.</summary>
    public void AddVariant(string primPath, string variantSetName, string variantName) =>
        OpenUsdNativeRuntime.AddVariant(this, primPath, variantSetName, variantName);

    /// <summary>Authors a variant selection, or clears it when <paramref name="variantSelection"/> is null.</summary>
    public void SetVariantSelection(string primPath, string variantSetName, string? variantSelection) =>
        OpenUsdNativeRuntime.SetVariantSelection(this, primPath, variantSetName, variantSelection);

    /// <summary>Gets the authored variant selection.</summary>
    public string GetVariantSelection(string primPath, string variantSetName) =>
        OpenUsdNativeRuntime.GetVariantSelection(this, primPath, variantSetName);

    /// <summary>Gets the composed variant names for a variant set.</summary>
    public string[] GetVariantNames(string primPath, string variantSetName) =>
        OpenUsdNativeRuntime.GetVariantNames(this, primPath, variantSetName);

    /// <summary>Sets a string entry in the prim's customData dictionary.</summary>
    public void SetMetadata(string primPath, string key, string value) =>
        OpenUsdNativeRuntime.SetPrimMetadataString(this, primPath, key, value);

    /// <summary>Sets a bool entry in the prim's customData dictionary.</summary>
    public void SetMetadata(string primPath, string key, bool value) =>
        OpenUsdNativeRuntime.SetPrimMetadataBool(this, primPath, key, value);

    /// <summary>Sets an int64 entry in the prim's customData dictionary.</summary>
    public void SetMetadata(string primPath, string key, long value) =>
        OpenUsdNativeRuntime.SetPrimMetadataInt64(this, primPath, key, value);

    /// <summary>Sets a double entry in the prim's customData dictionary.</summary>
    public void SetMetadata(string primPath, string key, double value) =>
        OpenUsdNativeRuntime.SetPrimMetadataDouble(this, primPath, key, value);

    /// <summary>Gets a string entry from the prim's customData dictionary.</summary>
    public string GetMetadataString(string primPath, string key) =>
        OpenUsdNativeRuntime.GetPrimMetadataString(this, primPath, key);

    /// <summary>Gets a bool entry from the prim's customData dictionary.</summary>
    public bool GetMetadataBool(string primPath, string key) =>
        OpenUsdNativeRuntime.GetPrimMetadataBool(this, primPath, key);

    /// <summary>Gets an int64 entry from the prim's customData dictionary.</summary>
    public long GetMetadataInt64(string primPath, string key) =>
        OpenUsdNativeRuntime.GetPrimMetadataInt64(this, primPath, key);

    /// <summary>Gets a double entry from the prim's customData dictionary.</summary>
    public double GetMetadataDouble(string primPath, string key) =>
        OpenUsdNativeRuntime.GetPrimMetadataDouble(this, primPath, key);

    /// <summary>Clears an entry from the prim's customData dictionary.</summary>
    public void ClearMetadata(string primPath, string key) =>
        OpenUsdNativeRuntime.ClearPrimMetadata(this, primPath, key);

    /// <summary>Returns whether a prim is a UsdShadeMaterial.</summary>
    public bool IsShadeMaterial(string primPath) =>
        OpenUsdNativeRuntime.IsShadeMaterial(this, primPath);

    /// <summary>Returns whether a prim is a UsdShadeShader.</summary>
    public bool IsShadeShader(string primPath) =>
        OpenUsdNativeRuntime.IsShadeShader(this, primPath);

    /// <summary>Returns whether a prim is a UsdShadeNodeGraph.</summary>
    public bool IsShadeNodeGraph(string primPath) =>
        OpenUsdNativeRuntime.IsShadeNodeGraph(this, primPath);

    /// <summary>Defines a UsdShadeMaterial prim.</summary>
    public void DefineShadeMaterial(string primPath) =>
        OpenUsdNativeRuntime.DefineShadeMaterial(this, primPath);

    /// <summary>Defines a UsdShadeShader prim.</summary>
    public void DefineShadeShader(string primPath) =>
        OpenUsdNativeRuntime.DefineShadeShader(this, primPath);

    /// <summary>Defines a UsdShadeNodeGraph prim.</summary>
    public void DefineShadeNodeGraph(string primPath) =>
        OpenUsdNativeRuntime.DefineShadeNodeGraph(this, primPath);

    /// <summary>Authors a shader source identifier.</summary>
    public void SetShaderSourceId(string shaderPath, string sourceId) =>
        OpenUsdNativeRuntime.SetShaderSourceId(this, shaderPath, sourceId);

    /// <summary>Gets a shader source identifier.</summary>
    public string GetShaderSourceId(string shaderPath) =>
        OpenUsdNativeRuntime.GetShaderSourceId(this, shaderPath);

    /// <summary>Creates or validates a typed shading input.</summary>
    public void CreateShadeInput(
        string connectablePath,
        string inputName,
        OpenUsdNativeShadeValueType valueType) =>
        OpenUsdNativeRuntime.CreateShadeInput(this, connectablePath, inputName, valueType);

    /// <summary>Gets the supported type of a shading input.</summary>
    public OpenUsdNativeShadeValueType GetShadeInputType(
        string connectablePath,
        string inputName) =>
        OpenUsdNativeRuntime.GetShadeInputType(this, connectablePath, inputName);

    /// <summary>Sets a float shading input.</summary>
    public void SetShadeInput(string shaderPath, string inputName, float value) =>
        OpenUsdNativeRuntime.SetShadeInputFloat(this, shaderPath, inputName, value);

    /// <summary>Gets a float shading input.</summary>
    public float GetShadeInputFloat(string shaderPath, string inputName) =>
        OpenUsdNativeRuntime.GetShadeInputFloat(this, shaderPath, inputName);

    /// <summary>Sets a vec3 shading input with an explicit role type.</summary>
    public void SetShadeInput(
        string shaderPath,
        string inputName,
        OpenUsdNativeShadeValueType valueType,
        OpenUsdNativeVec3f value) =>
        OpenUsdNativeRuntime.SetShadeInputVec3f(
            this,
            shaderPath,
            inputName,
            valueType,
            value);

    /// <summary>Gets a vec3 shading input with an explicit role type.</summary>
    public OpenUsdNativeVec3f GetShadeInputVec3f(
        string shaderPath,
        string inputName,
        OpenUsdNativeShadeValueType valueType) =>
        OpenUsdNativeRuntime.GetShadeInputVec3f(
            this,
            shaderPath,
            inputName,
            valueType);

    /// <summary>Sets a token, string, or asset shading input.</summary>
    public void SetShadeInput(
        string shaderPath,
        string inputName,
        OpenUsdNativeShadeValueType valueType,
        string value) =>
        OpenUsdNativeRuntime.SetShadeInputString(
            this,
            shaderPath,
            inputName,
            valueType,
            value);

    /// <summary>Gets a token, string, or asset shading input.</summary>
    public string GetShadeInputString(
        string shaderPath,
        string inputName,
        OpenUsdNativeShadeValueType valueType) =>
        OpenUsdNativeRuntime.GetShadeInputString(
            this,
            shaderPath,
            inputName,
            valueType);

    /// <summary>Creates or validates a typed shading output.</summary>
    public void CreateShadeOutput(
        string connectablePath,
        string outputName,
        OpenUsdNativeShadeValueType valueType) =>
        OpenUsdNativeRuntime.CreateShadeOutput(
            this,
            connectablePath,
            outputName,
            valueType);

    /// <summary>Gets the supported type of a shading output.</summary>
    public OpenUsdNativeShadeValueType GetShadeOutputType(
        string connectablePath,
        string outputName) =>
        OpenUsdNativeRuntime.GetShadeOutputType(this, connectablePath, outputName);

    /// <summary>Gets authored shading input names.</summary>
    public string[] GetShadeInputNames(string connectablePath) =>
        OpenUsdNativeRuntime.GetShadeInputNames(this, connectablePath);

    /// <summary>Gets authored shading output names.</summary>
    public string[] GetShadeOutputNames(string connectablePath) =>
        OpenUsdNativeRuntime.GetShadeOutputNames(this, connectablePath);

    /// <summary>Connects a shading input or output to a source property.</summary>
    public void ConnectShade(
        string destinationPath,
        string destinationName,
        OpenUsdNativeShadeAttributeType destinationType,
        string sourcePath,
        string sourceName,
        OpenUsdNativeShadeAttributeType sourceType) =>
        OpenUsdNativeRuntime.ConnectShade(
            this,
            destinationPath,
            destinationName,
            destinationType,
            sourcePath,
            sourceName,
            sourceType);

    /// <summary>Disconnects a shading input or output.</summary>
    public void DisconnectShade(
        string destinationPath,
        string destinationName,
        OpenUsdNativeShadeAttributeType destinationType) =>
        OpenUsdNativeRuntime.DisconnectShade(
            this,
            destinationPath,
            destinationName,
            destinationType);

    /// <summary>Gets the single connected source of a shading property.</summary>
    public OpenUsdNativeShadeConnection GetConnectedShadeSource(
        string destinationPath,
        string destinationName,
        OpenUsdNativeShadeAttributeType destinationType) =>
        OpenUsdNativeRuntime.GetConnectedShadeSource(
            this,
            destinationPath,
            destinationName,
            destinationType);

    /// <summary>Gets all connected sources of a shading property.</summary>
    public IReadOnlyList<OpenUsdNativeShadeConnection> GetConnectedShadeSources(
        string destinationPath,
        string destinationName,
        OpenUsdNativeShadeAttributeType destinationType) =>
        OpenUsdNativeRuntime.GetConnectedShadeSources(
            this,
            destinationPath,
            destinationName,
            destinationType);

    /// <summary>Creates the universal material surface output.</summary>
    public void CreateMaterialSurfaceOutput(string materialPath) =>
        OpenUsdNativeRuntime.CreateMaterialSurfaceOutput(this, materialPath);

    /// <summary>Creates a material terminal output.</summary>
    public void CreateMaterialTerminalOutput(
        string materialPath,
        OpenUsdNativeShadeMaterialTerminal terminal,
        string renderContext) =>
        OpenUsdNativeRuntime.CreateMaterialTerminalOutput(
            this,
            materialPath,
            terminal,
            renderContext);

    /// <summary>Authors a direct material binding.</summary>
    public void BindMaterial(string primPath, string materialPath) =>
        OpenUsdNativeRuntime.BindMaterial(this, primPath, materialPath);

    /// <summary>Authors a direct material binding with strength and purpose.</summary>
    public void BindMaterial(
        string primPath,
        string materialPath,
        OpenUsdNativeShadeBindingStrength strength,
        OpenUsdNativeShadeMaterialPurpose purpose) =>
        OpenUsdNativeRuntime.BindMaterial(this, primPath, materialPath, strength, purpose);

    /// <summary>Authors a collection material binding.</summary>
    public void BindMaterialCollection(
        string primPath,
        string collectionPrimPath,
        string collectionName,
        string materialPath,
        string bindingName,
        OpenUsdNativeShadeBindingStrength strength,
        OpenUsdNativeShadeMaterialPurpose purpose) =>
        OpenUsdNativeRuntime.BindMaterialCollection(
            this,
            primPath,
            collectionPrimPath,
            collectionName,
            materialPath,
            bindingName,
            strength,
            purpose);

    /// <summary>Removes the direct material binding.</summary>
    public void UnbindMaterial(string primPath) =>
        OpenUsdNativeRuntime.UnbindMaterial(this, primPath);

    /// <summary>Returns whether a prim is the requested concrete UsdLux schema.</summary>
    public bool IsLuxSchema(string primPath, OpenUsdNativeLuxSchemaKind schemaKind) =>
        OpenUsdNativeRuntime.IsLuxSchema(this, primPath, schemaKind);

    /// <summary>Defines a concrete UsdLux schema prim.</summary>
    public void DefineLux(string primPath, OpenUsdNativeLuxSchemaKind schemaKind) =>
        OpenUsdNativeRuntime.DefineLux(this, primPath, schemaKind);

    /// <summary>Authors a shared scalar UsdLux input.</summary>
    public void SetLuxFloat(
        string primPath,
        OpenUsdNativeLuxFloatProperty property,
        float value) =>
        OpenUsdNativeRuntime.SetLuxFloat(this, primPath, property, value);

    /// <summary>Reads a shared scalar UsdLux input.</summary>
    public float GetLuxFloat(string primPath, OpenUsdNativeLuxFloatProperty property) =>
        OpenUsdNativeRuntime.GetLuxFloat(this, primPath, property);

    /// <summary>Authors a shared boolean UsdLux input.</summary>
    public void SetLuxBool(
        string primPath,
        OpenUsdNativeLuxBoolProperty property,
        bool value) =>
        OpenUsdNativeRuntime.SetLuxBool(this, primPath, property, value);

    /// <summary>Reads a shared boolean UsdLux input.</summary>
    public bool GetLuxBool(string primPath, OpenUsdNativeLuxBoolProperty property) =>
        OpenUsdNativeRuntime.GetLuxBool(this, primPath, property);

    /// <summary>Authors the shared UsdLux color input.</summary>
    public void SetLuxColor(string primPath, OpenUsdNativeVec3f value) =>
        OpenUsdNativeRuntime.SetLuxColor(this, primPath, value);

    /// <summary>Reads the shared UsdLux color input.</summary>
    public OpenUsdNativeVec3f GetLuxColor(string primPath) =>
        OpenUsdNativeRuntime.GetLuxColor(this, primPath);

    /// <summary>Authors a concrete-light shape input.</summary>
    public void SetLuxShape(
        string primPath,
        OpenUsdNativeLuxShapeProperty property,
        float value) =>
        OpenUsdNativeRuntime.SetLuxShape(this, primPath, property, value);

    /// <summary>Reads a concrete-light shape input.</summary>
    public float GetLuxShape(string primPath, OpenUsdNativeLuxShapeProperty property) =>
        OpenUsdNativeRuntime.GetLuxShape(this, primPath, property);

    /// <summary>Authors a supported light asset input.</summary>
    public void SetLuxAsset(
        string primPath,
        OpenUsdNativeLuxAssetProperty property,
        string value) =>
        OpenUsdNativeRuntime.SetLuxAsset(this, primPath, property, value);

    /// <summary>Reads a supported light asset input.</summary>
    public string GetLuxAsset(string primPath, OpenUsdNativeLuxAssetProperty property) =>
        OpenUsdNativeRuntime.GetLuxAsset(this, primPath, property);

    /// <summary>Returns whether UsdLuxShapingAPI is applied to a light.</summary>
    public bool HasLuxShaping(string primPath) =>
        OpenUsdNativeRuntime.HasLuxShaping(this, primPath);

    /// <summary>Applies UsdLuxShapingAPI to a light.</summary>
    public void ApplyLuxShaping(string primPath) =>
        OpenUsdNativeRuntime.ApplyLuxShaping(this, primPath);

    /// <summary>Authors a focused UsdLuxShapingAPI input.</summary>
    public void SetLuxShaping(
        string primPath,
        OpenUsdNativeLuxShapingProperty property,
        float value) =>
        OpenUsdNativeRuntime.SetLuxShaping(this, primPath, property, value);

    /// <summary>Reads a focused UsdLuxShapingAPI input.</summary>
    public float GetLuxShaping(
        string primPath,
        OpenUsdNativeLuxShapingProperty property) =>
        OpenUsdNativeRuntime.GetLuxShaping(this, primPath, property);

    public bool IsPhysicsSchema(string primPath, OpenUsdNativePhysicsSchemaKind schemaKind) =>
        OpenUsdNativeRuntime.IsPhysicsSchema(this, primPath, schemaKind);

    public void DefinePhysics(string primPath, OpenUsdNativePhysicsSchemaKind schemaKind) =>
        OpenUsdNativeRuntime.DefinePhysics(this, primPath, schemaKind);

    public bool HasPhysicsApi(
        string primPath,
        OpenUsdNativePhysicsApiKind apiKind,
        string? instanceName = null) =>
        OpenUsdNativeRuntime.HasPhysicsApi(this, primPath, apiKind, instanceName);

    public void ApplyPhysicsApi(
        string primPath,
        OpenUsdNativePhysicsApiKind apiKind,
        string? instanceName = null) =>
        OpenUsdNativeRuntime.ApplyPhysicsApi(this, primPath, apiKind, instanceName);

    public void SetPhysicsFloat(
        string primPath,
        OpenUsdNativePhysicsFloatProperty property,
        float value,
        string? instanceName = null) =>
        OpenUsdNativeRuntime.SetPhysicsFloat(this, primPath, property, value, instanceName);

    public float GetPhysicsFloat(
        string primPath,
        OpenUsdNativePhysicsFloatProperty property,
        string? instanceName = null) =>
        OpenUsdNativeRuntime.GetPhysicsFloat(this, primPath, property, instanceName);

    public void SetPhysicsBool(
        string primPath,
        OpenUsdNativePhysicsBoolProperty property,
        bool value) =>
        OpenUsdNativeRuntime.SetPhysicsBool(this, primPath, property, value);

    public bool GetPhysicsBool(string primPath, OpenUsdNativePhysicsBoolProperty property) =>
        OpenUsdNativeRuntime.GetPhysicsBool(this, primPath, property);

    public void SetPhysicsVec3f(
        string primPath,
        OpenUsdNativePhysicsVec3fProperty property,
        OpenUsdNativeVec3f value) =>
        OpenUsdNativeRuntime.SetPhysicsVec3f(this, primPath, property, value);

    public OpenUsdNativeVec3f GetPhysicsVec3f(
        string primPath,
        OpenUsdNativePhysicsVec3fProperty property) =>
        OpenUsdNativeRuntime.GetPhysicsVec3f(this, primPath, property);

    public void SetPhysicsQuatf(
        string primPath,
        OpenUsdNativePhysicsQuatfProperty property,
        OpenUsdNativeQuatf value) =>
        OpenUsdNativeRuntime.SetPhysicsQuatf(this, primPath, property, value);

    public OpenUsdNativeQuatf GetPhysicsQuatf(
        string primPath,
        OpenUsdNativePhysicsQuatfProperty property) =>
        OpenUsdNativeRuntime.GetPhysicsQuatf(this, primPath, property);

    public void SetPhysicsToken(
        string primPath,
        OpenUsdNativePhysicsTokenProperty property,
        string value,
        string? instanceName = null) =>
        OpenUsdNativeRuntime.SetPhysicsToken(this, primPath, property, value, instanceName);

    public string GetPhysicsToken(
        string primPath,
        OpenUsdNativePhysicsTokenProperty property,
        string? instanceName = null) =>
        OpenUsdNativeRuntime.GetPhysicsToken(this, primPath, property, instanceName);

    public void SetPhysicsString(
        string primPath,
        OpenUsdNativePhysicsStringProperty property,
        string value) =>
        OpenUsdNativeRuntime.SetPhysicsString(this, primPath, property, value);

    public string GetPhysicsString(
        string primPath,
        OpenUsdNativePhysicsStringProperty property) =>
        OpenUsdNativeRuntime.GetPhysicsString(this, primPath, property);

    /// <summary>Returns whether a prim is the requested concrete UsdSkel schema.</summary>
    public bool IsSkelSchema(string primPath, OpenUsdNativeSkelSchemaKind schemaKind) =>
        OpenUsdNativeRuntime.IsSkelSchema(this, primPath, schemaKind);

    /// <summary>Defines a concrete UsdSkel schema prim.</summary>
    public void DefineSkel(string primPath, OpenUsdNativeSkelSchemaKind schemaKind) =>
        OpenUsdNativeRuntime.DefineSkel(this, primPath, schemaKind);

    /// <summary>Returns whether UsdSkelBindingAPI is applied to a prim.</summary>
    public bool HasSkelBinding(string primPath) =>
        OpenUsdNativeRuntime.HasSkelBinding(this, primPath);

    /// <summary>Applies UsdSkelBindingAPI to a prim.</summary>
    public void ApplySkelBinding(string primPath) =>
        OpenUsdNativeRuntime.ApplySkelBinding(this, primPath);

    /// <summary>Authors ordered skeleton or animation joints using one packed call.</summary>
    public void SetSkelJoints(
        string primPath,
        OpenUsdNativeSkelSchemaKind schemaKind,
        ReadOnlySpan<string> joints) =>
        OpenUsdNativeRuntime.SetSkelJoints(this, primPath, schemaKind, joints);

    /// <summary>Reads ordered skeleton or animation joints using one packed call.</summary>
    public string[] GetSkelJoints(
        string primPath,
        OpenUsdNativeSkelSchemaKind schemaKind) =>
        OpenUsdNativeRuntime.GetSkelJoints(this, primPath, schemaKind);

    /// <summary>Authors a contiguous skeleton matrix array.</summary>
    public void SetSkelSkeletonMatrices(
        string primPath,
        OpenUsdNativeSkelMatrixProperty property,
        ReadOnlySpan<OpenUsdNativeMatrix4d> values) =>
        OpenUsdNativeRuntime.SetSkelSkeletonMatrices(this, primPath, property, values);

    /// <summary>Reads a contiguous skeleton matrix array.</summary>
    public OpenUsdNativeMatrix4d[] GetSkelSkeletonMatrices(
        string primPath,
        OpenUsdNativeSkelMatrixProperty property) =>
        OpenUsdNativeRuntime.GetSkelSkeletonMatrices(this, primPath, property);

    /// <summary>Authors contiguous skeleton animation vectors.</summary>
    public void SetSkelAnimationVec3(
        string primPath,
        OpenUsdNativeSkelAnimationVec3Property property,
        ReadOnlySpan<OpenUsdNativeVec3f> values,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetSkelAnimationVec3(this, primPath, property, values, timeCode);

    /// <summary>Reads contiguous skeleton animation vectors.</summary>
    public OpenUsdNativeVec3f[] GetSkelAnimationVec3(
        string primPath,
        OpenUsdNativeSkelAnimationVec3Property property,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetSkelAnimationVec3(this, primPath, property, timeCode);

    /// <summary>Authors contiguous skeleton animation rotations.</summary>
    public void SetSkelAnimationRotations(
        string primPath,
        ReadOnlySpan<OpenUsdNativeQuatf> values,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.SetSkelAnimationRotations(this, primPath, values, timeCode);

    /// <summary>Reads contiguous skeleton animation rotations.</summary>
    public OpenUsdNativeQuatf[] GetSkelAnimationRotations(
        string primPath,
        double? timeCode = null) =>
        OpenUsdNativeRuntime.GetSkelAnimationRotations(this, primPath, timeCode);

    /// <summary>Authors a focused skeleton binding relationship target.</summary>
    public void SetSkelBindingTarget(
        string primPath,
        OpenUsdNativeSkelBindingRelationship relationship,
        string targetPrimPath) =>
        OpenUsdNativeRuntime.SetSkelBindingTarget(
            this,
            primPath,
            relationship,
            targetPrimPath);

    /// <summary>Reads a focused skeleton binding relationship target.</summary>
    public string GetSkelBindingTarget(
        string primPath,
        OpenUsdNativeSkelBindingRelationship relationship) =>
        OpenUsdNativeRuntime.GetSkelBindingTarget(this, primPath, relationship);

    /// <summary>Clears a focused skeleton binding relationship target.</summary>
    public void ClearSkelBindingTarget(
        string primPath,
        OpenUsdNativeSkelBindingRelationship relationship) =>
        OpenUsdNativeRuntime.ClearSkelBindingTarget(this, primPath, relationship);

    /// <summary>Authors a geometry bind transform.</summary>
    public void SetSkelGeomBindTransform(
        string primPath,
        OpenUsdNativeMatrix4d value) =>
        OpenUsdNativeRuntime.SetSkelGeomBindTransform(this, primPath, value);

    /// <summary>Reads a geometry bind transform.</summary>
    public OpenUsdNativeMatrix4d GetSkelGeomBindTransform(string primPath) =>
        OpenUsdNativeRuntime.GetSkelGeomBindTransform(this, primPath);

    /// <summary>Authors joint indices and weights in one bulk call.</summary>
    public void SetSkelJointInfluences(
        string primPath,
        ReadOnlySpan<int> jointIndices,
        ReadOnlySpan<float> jointWeights,
        int elementSize,
        OpenUsdNativeSkelInterpolation interpolation) =>
        OpenUsdNativeRuntime.SetSkelJointInfluences(
            this,
            primPath,
            jointIndices,
            jointWeights,
            elementSize,
            interpolation);

    /// <summary>Reads joint indices, weights, and shape metadata in one bulk call.</summary>
    public OpenUsdNativeSkelInfluences GetSkelJointInfluences(string primPath) =>
        OpenUsdNativeRuntime.GetSkelJointInfluences(this, primPath);

    /// <summary>Authors a skinning method token.</summary>
    public void SetSkelSkinningMethod(
        string primPath,
        OpenUsdNativeSkelSkinningMethod method) =>
        OpenUsdNativeRuntime.SetSkelSkinningMethod(this, primPath, method);

    /// <summary>Reads a skinning method token.</summary>
    public OpenUsdNativeSkelSkinningMethod GetSkelSkinningMethod(string primPath) =>
        OpenUsdNativeRuntime.GetSkelSkinningMethod(this, primPath);

    /// <summary>Authors blend-shape channel names.</summary>
    public void SetSkelBlendShapes(string primPath, ReadOnlySpan<string> names) =>
        OpenUsdNativeRuntime.SetSkelBlendShapes(this, primPath, names);

    /// <summary>Reads blend-shape channel names.</summary>
    public string[] GetSkelBlendShapes(string primPath) =>
        OpenUsdNativeRuntime.GetSkelBlendShapes(this, primPath);

    /// <summary>Authors blend-shape targets.</summary>
    public void SetSkelBlendShapeTargets(string primPath, ReadOnlySpan<string> targets) =>
        OpenUsdNativeRuntime.SetSkelBlendShapeTargets(this, primPath, targets);

    /// <summary>Reads blend-shape targets.</summary>
    public string[] GetSkelBlendShapeTargets(string primPath) =>
        OpenUsdNativeRuntime.GetSkelBlendShapeTargets(this, primPath);

    /// <summary>Authors blend-shape vectors in one bulk call.</summary>
    public void SetSkelBlendShapeVec3(
        string primPath,
        OpenUsdNativeSkelBlendShapeVec3Property property,
        ReadOnlySpan<OpenUsdNativeVec3f> values) =>
        OpenUsdNativeRuntime.SetSkelBlendShapeVec3(this, primPath, property, values);

    /// <summary>Reads blend-shape vectors in one bulk call.</summary>
    public OpenUsdNativeVec3f[] GetSkelBlendShapeVec3(
        string primPath,
        OpenUsdNativeSkelBlendShapeVec3Property property) =>
        OpenUsdNativeRuntime.GetSkelBlendShapeVec3(this, primPath, property);

    /// <summary>Authors blend-shape point indices in one bulk call.</summary>
    public void SetSkelBlendShapePointIndices(string primPath, ReadOnlySpan<int> values) =>
        OpenUsdNativeRuntime.SetSkelBlendShapePointIndices(this, primPath, values);

    /// <summary>Reads blend-shape point indices in one bulk call.</summary>
    public int[] GetSkelBlendShapePointIndices(string primPath) =>
        OpenUsdNativeRuntime.GetSkelBlendShapePointIndices(this, primPath);

    /// <summary>Authors a blend-shape inbetween.</summary>
    public void SetSkelBlendShapeInbetween(
        string primPath,
        string name,
        float weight,
        ReadOnlySpan<OpenUsdNativeVec3f> offsets,
        ReadOnlySpan<OpenUsdNativeVec3f> normalOffsets) =>
        OpenUsdNativeRuntime.SetSkelBlendShapeInbetween(
            this,
            primPath,
            name,
            weight,
            offsets,
            normalOffsets);

    /// <summary>Reads authored blend-shape inbetween names.</summary>
    public string[] GetSkelBlendShapeInbetweenNames(string primPath) =>
        OpenUsdNativeRuntime.GetSkelBlendShapeInbetweenNames(this, primPath);

    /// <summary>Reads one blend-shape inbetween.</summary>
    public OpenUsdNativeSkelBlendShapeInbetween GetSkelBlendShapeInbetween(
        string primPath,
        string name) =>
        OpenUsdNativeRuntime.GetSkelBlendShapeInbetween(this, primPath, name);

    /// <summary>Gets the directly bound material prim path.</summary>
    public string GetDirectMaterialPath(string primPath) =>
        OpenUsdNativeRuntime.GetDirectMaterialPath(this, primPath);

    /// <summary>Gets the resolved bound material prim path.</summary>
    public string GetBoundMaterialPath(
        string primPath,
        OpenUsdNativeShadeMaterialPurpose purpose) =>
        OpenUsdNativeRuntime.GetBoundMaterialPath(this, primPath, purpose);

    /// <inheritdoc/>

    public bool IsVolSchema(string primPath, OpenUsdNativeVolSchemaKind schemaKind) =>
        OpenUsdNativeRuntime.IsVolSchema(this, primPath, schemaKind);

    public void DefineVol(string primPath, OpenUsdNativeVolSchemaKind schemaKind) =>
        OpenUsdNativeRuntime.DefineVol(this, primPath, schemaKind);

    public string[] GetVolFieldPathPairs(string primPath) =>
        OpenUsdNativeRuntime.GetVolFieldPathPairs(this, primPath);

    public void SetVolFieldPath(string primPath, string fieldName, string targetPrimPath) =>
        OpenUsdNativeRuntime.SetVolFieldPath(this, primPath, fieldName, targetPrimPath);

    public bool HasVolFieldRelationship(string primPath, string fieldName) =>
        OpenUsdNativeRuntime.HasVolFieldRelationship(this, primPath, fieldName);

    public void BlockVolFieldRelationship(string primPath, string fieldName) =>
        OpenUsdNativeRuntime.BlockVolFieldRelationship(this, primPath, fieldName);

    public void SetVolAsset(string primPath, OpenUsdNativeVolAssetProperty property, string assetPath) =>
        OpenUsdNativeRuntime.SetVolAsset(this, primPath, property, assetPath);

    public string GetVolAsset(string primPath, OpenUsdNativeVolAssetProperty property) =>
        OpenUsdNativeRuntime.GetVolAsset(this, primPath, property);

    public void SetVolFieldIndex(string primPath, int fieldIndex) =>
        OpenUsdNativeRuntime.SetVolFieldIndex(this, primPath, fieldIndex);

    public int GetVolFieldIndex(string primPath) =>
        OpenUsdNativeRuntime.GetVolFieldIndex(this, primPath);

    public bool IsRenderSchema(string primPath, OpenUsdNativeRenderSchemaKind schemaKind) =>
        OpenUsdNativeRuntime.IsRenderSchema(this, primPath, schemaKind);

    public void DefineRender(string primPath, OpenUsdNativeRenderSchemaKind schemaKind) =>
        OpenUsdNativeRuntime.DefineRender(this, primPath, schemaKind);

    public void SetRenderResolution(string primPath, int width, int height) =>
        OpenUsdNativeRuntime.SetRenderResolution(this, primPath, width, height);

    public void GetRenderResolution(string primPath, out int width, out int height) =>
        OpenUsdNativeRuntime.GetRenderResolution(this, primPath, out width, out height);

    public void SetRenderDataWindowNdc(
        string primPath,
        float minX,
        float minY,
        float maxX,
        float maxY) =>
        OpenUsdNativeRuntime.SetRenderDataWindowNdc(this, primPath, minX, minY, maxX, maxY);

    public void GetRenderDataWindowNdc(
        string primPath,
        out float minX,
        out float minY,
        out float maxX,
        out float maxY) =>
        OpenUsdNativeRuntime.GetRenderDataWindowNdc(this, primPath, out minX, out minY, out maxX, out maxY);

    public bool IsMediaSchema(string primPath, OpenUsdNativeMediaSchemaKind schemaKind) =>
        OpenUsdNativeRuntime.IsMediaSchema(this, primPath, schemaKind);

    public void DefineMedia(string primPath, OpenUsdNativeMediaSchemaKind schemaKind) =>
        OpenUsdNativeRuntime.DefineMedia(this, primPath, schemaKind);

    public void ApplyMediaApi(string primPath, OpenUsdNativeMediaSchemaKind schemaKind) =>
        OpenUsdNativeRuntime.ApplyMediaApi(this, primPath, schemaKind);

    public void SetMediaAsset(string primPath, OpenUsdNativeMediaAssetProperty property, string assetPath) =>
        OpenUsdNativeRuntime.SetMediaAsset(this, primPath, property, assetPath);

    public string GetMediaAsset(string primPath, OpenUsdNativeMediaAssetProperty property) =>
        OpenUsdNativeRuntime.GetMediaAsset(this, primPath, property);

    public void ClearMediaAsset(string primPath, OpenUsdNativeMediaAssetProperty property) =>
        OpenUsdNativeRuntime.ClearMediaAsset(this, primPath, property);

    public void SetMediaTime(string primPath, OpenUsdNativeMediaTimeProperty property, double value) =>
        OpenUsdNativeRuntime.SetMediaTime(this, primPath, property, value);

    public double GetMediaTime(string primPath, OpenUsdNativeMediaTimeProperty property) =>
        OpenUsdNativeRuntime.GetMediaTime(this, primPath, property);

    public bool IsProcSchema(string primPath, OpenUsdNativeProcSchemaKind schemaKind) =>
        OpenUsdNativeRuntime.IsProcSchema(this, primPath, schemaKind);

    public void DefineProc(string primPath, OpenUsdNativeProcSchemaKind schemaKind) =>
        OpenUsdNativeRuntime.DefineProc(this, primPath, schemaKind);

    public bool IsUiSchema(string primPath, OpenUsdNativeUiSchemaKind schemaKind) =>
        OpenUsdNativeRuntime.IsUiSchema(this, primPath, schemaKind);

    public void DefineUi(string primPath, OpenUsdNativeUiSchemaKind schemaKind) =>
        OpenUsdNativeRuntime.DefineUi(this, primPath, schemaKind);

    public void ApplyUiApi(string primPath, OpenUsdNativeUiSchemaKind schemaKind) =>
        OpenUsdNativeRuntime.ApplyUiApi(this, primPath, schemaKind);

    public void SetUiVec2f(string primPath, OpenUsdNativeUiVec2fProperty property, OpenUsdNativeVec2f value) =>
        OpenUsdNativeRuntime.SetUiVec2f(this, primPath, property, value);

    public OpenUsdNativeVec2f GetUiVec2f(string primPath, OpenUsdNativeUiVec2fProperty property) =>
        OpenUsdNativeRuntime.GetUiVec2f(this, primPath, property);

    protected override bool ReleaseHandle()
    {
        OpenUsdNativeRuntime.ReleaseStage(handle);
        return true;
    }
}
