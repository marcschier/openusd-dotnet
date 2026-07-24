// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace OpenUsd.Geom;

/// <summary>A validated view of a prim conforming to UsdGeomXformable.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdGeomXformable : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdGeomXformable(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }

    /// <summary>Gets the prim path.</summary>
    public string Path { get; }

    /// <summary>Gets the imageable schema view.</summary>
    public UsdGeomImageable Imageable => new(Stage, Path);

    /// <summary>Gets the underlying prim descriptor.</summary>
    public UsdPrim Prim => Stage.GetPrim(Path);

    /// <summary>Tries to wrap a compatible prim.</summary>
    public static bool TryWrap(UsdPrim prim, out UsdGeomXformable value)
    {
        if (UsdGeomSchema.TryValidate(prim, UsdGeomSchemaKind.Xformable, out UsdStage? stage))
        {
            value = new UsdGeomXformable(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Wraps a compatible prim or throws for a wrong schema.</summary>
    public static UsdGeomXformable Wrap(UsdPrim prim) => new(
        UsdGeomSchema.Validate(prim, UsdGeomSchemaKind.Xformable, nameof(UsdGeomXformable)),
        prim.Path);

    /// <summary>Replaces the local transform stack with one matrix operation at default time.</summary>
    public void SetLocalTransform(UsdMatrix4d value) =>
        Stage.Native.SetGeomLocalTransform(Path, value.ToNative());

    /// <summary>Replaces the local transform stack with one sampled matrix operation.</summary>
    public void SetLocalTransform(UsdMatrix4d value, double timeCode) =>
        Stage.Native.SetGeomLocalTransform(Path, value.ToNative(), timeCode);

    /// <summary>Computes the local transform at default time.</summary>
    public UsdMatrix4d GetLocalTransform() =>
        UsdMatrix4d.FromNative(Stage.Native.GetGeomLocalTransform(Path));

    /// <summary>Computes the local transform at a numeric time code.</summary>
    public UsdMatrix4d GetLocalTransform(double timeCode) =>
        UsdMatrix4d.FromNative(Stage.Native.GetGeomLocalTransform(Path, timeCode));

    /// <summary>Computes the local-to-world transform at default time.</summary>
    public UsdMatrix4d GetWorldTransform()
    {
        UsdPath.ValidateAbsolutePrimPath(Path, nameof(Path));
        return UsdMatrix4d.FromNative(Stage.Native.GetGeomWorldTransform(Path));
    }

    /// <summary>Computes the local-to-world transform at a numeric time code.</summary>
    public UsdMatrix4d GetWorldTransform(double timeCode)
    {
        if (!double.IsFinite(timeCode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeCode),
                "The time code must be finite.");
        }
        UsdPath.ValidateAbsolutePrimPath(Path, nameof(Path));
        return UsdMatrix4d.FromNative(Stage.Native.GetGeomWorldTransform(Path, timeCode));
    }

    /// <summary>Sets whether this prim resets its inherited transform stack.</summary>
    public void SetResetXformStack(bool reset) =>
        Stage.Native.SetGeomResetXformStack(Path, reset);

    /// <summary>Gets whether this prim resets its inherited transform stack.</summary>
    public bool GetResetXformStack() => Stage.Native.GetGeomResetXformStack(Path);

    private UsdStage Stage =>
        _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}
