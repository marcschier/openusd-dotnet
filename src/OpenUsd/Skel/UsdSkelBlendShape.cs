// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Skel;

/// <summary>A validated UsdSkelBlendShape view.</summary>
public readonly struct UsdSkelBlendShape : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdSkelBlendShape(UsdStage stage, string path)
    {
        ArgumentNullException.ThrowIfNull(stage);
        UsdPath.ValidateAbsolutePrimPath(path, nameof(path));
        _stage = stage;
        Path = path;
    }

    /// <summary>Gets the prim path.</summary>
    public string Path { get; }

    /// <summary>Gets the underlying prim descriptor.</summary>
    public UsdPrim Prim => Stage.GetPrim(Path);

    /// <summary>Authors point offsets in one bulk native call.</summary>
    public void SetOffsets(ReadOnlySpan<UsdVec3f> values) =>
        SetVectors(OpenUsdNativeSkelBlendShapeVec3Property.Offsets, values);

    /// <summary>Gets point offsets in one bulk native call.</summary>
    public UsdVec3f[] GetOffsets() =>
        GetVectors(OpenUsdNativeSkelBlendShapeVec3Property.Offsets);

    /// <summary>Authors normal offsets in one bulk native call.</summary>
    public void SetNormalOffsets(ReadOnlySpan<UsdVec3f> values) =>
        SetVectors(OpenUsdNativeSkelBlendShapeVec3Property.NormalOffsets, values);

    /// <summary>Gets normal offsets in one bulk native call.</summary>
    public UsdVec3f[] GetNormalOffsets() =>
        GetVectors(OpenUsdNativeSkelBlendShapeVec3Property.NormalOffsets);

    /// <summary>Authors sparse point indices in one bulk native call.</summary>
    public void SetPointIndices(ReadOnlySpan<int> values) =>
        Stage.Native.SetSkelBlendShapePointIndices(Path, values);

    /// <summary>Gets sparse point indices in one bulk native call.</summary>
    public int[] GetPointIndices() => Stage.Native.GetSkelBlendShapePointIndices(Path);

    /// <summary>Creates or updates an inbetween shape.</summary>
    public void SetInbetween(
        string name,
        float weight,
        ReadOnlySpan<UsdVec3f> offsets,
        ReadOnlySpan<UsdVec3f> normalOffsets = default) =>
        Stage.Native.SetSkelBlendShapeInbetween(
            Path,
            name,
            weight,
            UsdSkelSchema.ToNative(offsets),
            UsdSkelSchema.ToNative(normalOffsets));

    /// <summary>Gets authored inbetween names.</summary>
    public IReadOnlyList<string> GetInbetweenNames() =>
        Stage.Native.GetSkelBlendShapeInbetweenNames(Path);

    /// <summary>Gets one authored inbetween shape.</summary>
    public UsdSkelBlendShapeInbetween GetInbetween(string name)
    {
        OpenUsdNativeSkelBlendShapeInbetween value =
            Stage.Native.GetSkelBlendShapeInbetween(Path, name);
        return new UsdSkelBlendShapeInbetween(
            name,
            value.Weight,
            UsdSkelSchema.FromNative(value.Offsets),
            UsdSkelSchema.FromNative(value.NormalOffsets));
    }

    /// <summary>Tries to wrap an exact UsdSkelBlendShape prim.</summary>
    public static bool TryWrap(UsdPrim prim, out UsdSkelBlendShape value)
    {
        if (UsdSkelSchema.TryValidate(
            prim,
            OpenUsdNativeSkelSchemaKind.BlendShape,
            out UsdStage? stage))
        {
            value = new UsdSkelBlendShape(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Wraps an exact UsdSkelBlendShape prim.</summary>
    public static UsdSkelBlendShape Wrap(UsdPrim prim) => new(
        UsdSkelSchema.Validate(
            prim,
            OpenUsdNativeSkelSchemaKind.BlendShape,
            nameof(UsdSkelBlendShape)),
        prim.Path);

    private void SetVectors(
        OpenUsdNativeSkelBlendShapeVec3Property property,
        ReadOnlySpan<UsdVec3f> values) =>
        Stage.Native.SetSkelBlendShapeVec3(Path, property, UsdSkelSchema.ToNative(values));

    private UsdVec3f[] GetVectors(OpenUsdNativeSkelBlendShapeVec3Property property) =>
        UsdSkelSchema.FromNative(Stage.Native.GetSkelBlendShapeVec3(Path, property));

    private UsdStage Stage =>
        _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}
