// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd.Skel;

/// <summary>A validated UsdSkelSkeleton view.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdSkelSkeleton : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdSkelSkeleton(UsdStage stage, string path)
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

    /// <summary>Gets the imageable schema view.</summary>
    public UsdGeomImageable Imageable => new(Stage, Path);

    /// <summary>Gets the transformable schema view.</summary>
    public UsdGeomXformable Xformable => new(Stage, Path);

    /// <summary>Authors ordered joint path tokens in one packed call.</summary>
    public void SetJoints(ReadOnlySpan<string> joints) =>
        Stage.Native.SetSkelJoints(Path, OpenUsdNativeSkelSchemaKind.Skeleton, joints);

    /// <summary>Gets ordered joint path tokens in one packed call.</summary>
    public string[] GetJoints() =>
        Stage.Native.GetSkelJoints(Path, OpenUsdNativeSkelSchemaKind.Skeleton);

    /// <summary>Authors world-space bind transforms in one contiguous call.</summary>
    public void SetBindTransforms(ReadOnlySpan<UsdMatrix4d> values) =>
        Stage.Native.SetSkelSkeletonMatrices(
            Path,
            OpenUsdNativeSkelMatrixProperty.BindTransforms,
            UsdSkelSchema.ToNative(values));

    /// <summary>Gets world-space bind transforms in one contiguous call.</summary>
    public UsdMatrix4d[] GetBindTransforms() =>
        UsdSkelSchema.FromNative(
            Stage.Native.GetSkelSkeletonMatrices(
                Path,
                OpenUsdNativeSkelMatrixProperty.BindTransforms));

    /// <summary>Authors joint-local rest transforms in one contiguous call.</summary>
    public void SetRestTransforms(ReadOnlySpan<UsdMatrix4d> values) =>
        Stage.Native.SetSkelSkeletonMatrices(
            Path,
            OpenUsdNativeSkelMatrixProperty.RestTransforms,
            UsdSkelSchema.ToNative(values));

    /// <summary>Gets joint-local rest transforms in one contiguous call.</summary>
    public UsdMatrix4d[] GetRestTransforms() =>
        UsdSkelSchema.FromNative(
            Stage.Native.GetSkelSkeletonMatrices(
                Path,
                OpenUsdNativeSkelMatrixProperty.RestTransforms));

    /// <summary>Applies and returns the skeleton's UsdSkelBindingAPI view.</summary>
    public UsdSkelBinding ApplyBinding() => UsdSkelBinding.Apply(Prim);

    /// <summary>Tries to wrap an exact UsdSkelSkeleton prim.</summary>
    public static bool TryWrap(UsdPrim prim, out UsdSkelSkeleton value)
    {
        if (UsdSkelSchema.TryValidate(
            prim,
            OpenUsdNativeSkelSchemaKind.Skeleton,
            out UsdStage? stage))
        {
            value = new UsdSkelSkeleton(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Wraps an exact UsdSkelSkeleton prim.</summary>
    public static UsdSkelSkeleton Wrap(UsdPrim prim) => new(
        UsdSkelSchema.Validate(
            prim,
            OpenUsdNativeSkelSchemaKind.Skeleton,
            nameof(UsdSkelSkeleton)),
        prim.Path);

    private UsdStage Stage =>
        _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}
