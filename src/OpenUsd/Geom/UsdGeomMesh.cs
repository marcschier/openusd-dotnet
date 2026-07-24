// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.Geom;

/// <summary>A validated, bulk-oriented view of a UsdGeomMesh prim.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdGeomMesh : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdGeomMesh(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }

    /// <summary>Gets the prim path.</summary>
    public string Path { get; }

    /// <summary>Gets the imageable schema view.</summary>
    public UsdGeomImageable Imageable => new(Stage, Path);

    /// <summary>Gets the xformable schema view.</summary>
    public UsdGeomXformable Xformable => new(Stage, Path);

    /// <summary>Gets the underlying prim descriptor.</summary>
    public UsdPrim Prim => Stage.GetPrim(Path);

    /// <summary>Tries to wrap a UsdGeomMesh prim.</summary>
    public static bool TryWrap(UsdPrim prim, out UsdGeomMesh value)
    {
        if (UsdGeomSchema.TryValidate(prim, UsdGeomSchemaKind.Mesh, out UsdStage? stage))
        {
            value = new UsdGeomMesh(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Wraps a UsdGeomMesh prim or throws for a wrong schema.</summary>
    public static UsdGeomMesh Wrap(UsdPrim prim) => new(
        UsdGeomSchema.Validate(prim, UsdGeomSchemaKind.Mesh, nameof(UsdGeomMesh)),
        prim.Path);

    /// <summary>Authors points at default time using one contiguous transfer.</summary>
    public void SetPoints(ReadOnlySpan<UsdVec3f> values) =>
        Stage.Native.SetGeomMeshPoints(Path, UsdGeomSchema.ToNative(values));

    /// <summary>Authors sampled points using one contiguous transfer.</summary>
    public void SetPoints(ReadOnlySpan<UsdVec3f> values, double timeCode) =>
        Stage.Native.SetGeomMeshPoints(Path, UsdGeomSchema.ToNative(values), timeCode);

    /// <summary>Gets points at default time using one contiguous data transfer.</summary>
    public UsdVec3f[] GetPoints() =>
        UsdGeomSchema.FromNative(Stage.Native.GetGeomMeshPoints(Path));

    /// <summary>Gets sampled points using one contiguous data transfer.</summary>
    public UsdVec3f[] GetPoints(double timeCode) =>
        UsdGeomSchema.FromNative(Stage.Native.GetGeomMeshPoints(Path, timeCode));

    /// <summary>Authors validated face counts and indices in one native call.</summary>
    public void SetTopology(
        ReadOnlySpan<int> faceVertexCounts,
        ReadOnlySpan<int> faceVertexIndices)
    {
        long expectedIndices = 0;
        for (int index = 0; index < faceVertexCounts.Length; ++index)
        {
            if (faceVertexCounts[index] < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(faceVertexCounts),
                    "Face vertex counts must be non-negative.");
            }
            expectedIndices = checked(expectedIndices + faceVertexCounts[index]);
        }
        if (expectedIndices != faceVertexIndices.Length)
        {
            throw new ArgumentException(
                "The sum of face vertex counts must equal the number of indices.",
                nameof(faceVertexIndices));
        }
        for (int index = 0; index < faceVertexIndices.Length; ++index)
        {
            if (faceVertexIndices[index] < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(faceVertexIndices),
                    "Face vertex indices must be non-negative.");
            }
        }
        Stage.Native.SetGeomMeshTopology(Path, faceVertexCounts, faceVertexIndices);
    }

    /// <summary>Gets face vertex counts using one contiguous data transfer.</summary>
    public int[] GetFaceVertexCounts() => Stage.Native.GetGeomMeshFaceVertexCounts(Path);

    /// <summary>Gets face vertex indices using one contiguous data transfer.</summary>
    public int[] GetFaceVertexIndices() => Stage.Native.GetGeomMeshFaceVertexIndices(Path);

    /// <summary>Authors normals and their interpolation at default time.</summary>
    public void SetNormals(
        ReadOnlySpan<UsdVec3f> values,
        UsdGeomInterpolation interpolation) =>
        Stage.Native.SetGeomMeshNormals(
            Path, UsdGeomSchema.ToNative(values), (int)interpolation);

    /// <summary>Authors sampled normals and their interpolation.</summary>
    public void SetNormals(
        ReadOnlySpan<UsdVec3f> values,
        UsdGeomInterpolation interpolation,
        double timeCode) =>
        Stage.Native.SetGeomMeshNormals(
            Path, UsdGeomSchema.ToNative(values), (int)interpolation, timeCode);

    /// <summary>Gets normals at default time.</summary>
    public UsdVec3f[] GetNormals() =>
        UsdGeomSchema.FromNative(Stage.Native.GetGeomMeshNormals(Path));

    /// <summary>Gets sampled normals.</summary>
    public UsdVec3f[] GetNormals(double timeCode) =>
        UsdGeomSchema.FromNative(Stage.Native.GetGeomMeshNormals(Path, timeCode));

    /// <summary>Gets or sets normals interpolation.</summary>
    public UsdGeomInterpolation NormalsInterpolation
    {
        get => (UsdGeomInterpolation)Stage.Native.GetGeomMeshNormalsInterpolation(Path);
        set => Stage.Native.SetGeomMeshNormalsInterpolation(Path, (int)value);
    }

    /// <summary>Gets or sets the subdivision scheme.</summary>
    public UsdGeomSubdivisionScheme SubdivisionScheme
    {
        get => (UsdGeomSubdivisionScheme)Stage.Native.GetGeomMeshSubdivisionScheme(Path);
        set => Stage.Native.SetGeomMeshSubdivisionScheme(Path, (int)value);
    }

    /// <summary>Gets or sets mesh orientation.</summary>
    public UsdGeomOrientation Orientation
    {
        get => (UsdGeomOrientation)Stage.Native.GetGeomMeshOrientation(Path);
        set => Stage.Native.SetGeomMeshOrientation(Path, (int)value);
    }

    /// <summary>Gets or sets double-sided state.</summary>
    public bool DoubleSided
    {
        get => Stage.Native.GetGeomMeshDoubleSided(Path);
        set => Stage.Native.SetGeomMeshDoubleSided(Path, value);
    }

    /// <summary>Authors an extent at default time.</summary>
    public void SetExtent(UsdExtent3f extent) =>
        Stage.Native.SetGeomMeshExtent(Path, ToNative(extent));

    /// <summary>Authors a sampled extent.</summary>
    public void SetExtent(UsdExtent3f extent, double timeCode) =>
        Stage.Native.SetGeomMeshExtent(Path, ToNative(extent), timeCode);

    /// <summary>Gets the extent at default time.</summary>
    public UsdExtent3f GetExtent() => FromNative(Stage.Native.GetGeomMeshExtent(Path));

    /// <summary>Gets a sampled extent.</summary>
    public UsdExtent3f GetExtent(double timeCode) =>
        FromNative(Stage.Native.GetGeomMeshExtent(Path, timeCode));

    private static OpenUsdNativeExtent3f ToNative(UsdExtent3f extent) => new()
    {
        Minimum = extent.Minimum.ToNative(),
        Maximum = extent.Maximum.ToNative()
    };

    private static UsdExtent3f FromNative(OpenUsdNativeExtent3f extent) => new(
        UsdVec3f.FromNative(extent.Minimum),
        UsdVec3f.FromNative(extent.Maximum));

    private UsdStage Stage =>
        _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}
