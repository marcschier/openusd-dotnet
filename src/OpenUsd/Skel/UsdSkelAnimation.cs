// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.Skel;

/// <summary>A validated UsdSkelAnimation view.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdSkelAnimation : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdSkelAnimation(UsdStage stage, string path)
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

    /// <summary>Authors ordered animation joint path tokens in one packed call.</summary>
    public void SetJoints(ReadOnlySpan<string> joints) =>
        Stage.Native.SetSkelJoints(Path, OpenUsdNativeSkelSchemaKind.Animation, joints);

    /// <summary>Gets ordered animation joint path tokens in one packed call.</summary>
    public string[] GetJoints() =>
        Stage.Native.GetSkelJoints(Path, OpenUsdNativeSkelSchemaKind.Animation);

    /// <summary>Authors joint-local translations at default time.</summary>
    public void SetTranslations(ReadOnlySpan<UsdVec3f> values) =>
        SetVectors(OpenUsdNativeSkelAnimationVec3Property.Translations, values, null);

    /// <summary>Authors joint-local translations at a numeric time code.</summary>
    public void SetTranslations(ReadOnlySpan<UsdVec3f> values, double timeCode) =>
        SetVectors(OpenUsdNativeSkelAnimationVec3Property.Translations, values, timeCode);

    /// <summary>Gets joint-local translations at default time.</summary>
    public UsdVec3f[] GetTranslations() =>
        GetVectors(OpenUsdNativeSkelAnimationVec3Property.Translations, null);

    /// <summary>Gets joint-local translations at a numeric time code.</summary>
    public UsdVec3f[] GetTranslations(double timeCode) =>
        GetVectors(OpenUsdNativeSkelAnimationVec3Property.Translations, timeCode);

    /// <summary>Authors unit-quaternion joint rotations at default time.</summary>
    public void SetRotations(ReadOnlySpan<UsdQuatf> values) => SetRotations(values, null);

    /// <summary>Authors unit-quaternion joint rotations at a numeric time code.</summary>
    public void SetRotations(ReadOnlySpan<UsdQuatf> values, double timeCode) =>
        SetRotations(values, (double?)timeCode);

    /// <summary>Gets joint rotations at default time.</summary>
    public UsdQuatf[] GetRotations() =>
        UsdSkelSchema.FromNative(Stage.Native.GetSkelAnimationRotations(Path));

    /// <summary>Gets joint rotations at a numeric time code.</summary>
    public UsdQuatf[] GetRotations(double timeCode) =>
        UsdSkelSchema.FromNative(Stage.Native.GetSkelAnimationRotations(Path, timeCode));

    /// <summary>Authors joint-local scales at default time.</summary>
    public void SetScales(ReadOnlySpan<UsdVec3f> values) =>
        SetVectors(OpenUsdNativeSkelAnimationVec3Property.Scales, values, null);

    /// <summary>Authors joint-local scales at a numeric time code.</summary>
    public void SetScales(ReadOnlySpan<UsdVec3f> values, double timeCode) =>
        SetVectors(OpenUsdNativeSkelAnimationVec3Property.Scales, values, timeCode);

    /// <summary>Gets joint-local scales at default time.</summary>
    public UsdVec3f[] GetScales() =>
        GetVectors(OpenUsdNativeSkelAnimationVec3Property.Scales, null);

    /// <summary>Gets joint-local scales at a numeric time code.</summary>
    public UsdVec3f[] GetScales(double timeCode) =>
        GetVectors(OpenUsdNativeSkelAnimationVec3Property.Scales, timeCode);

    /// <summary>Tries to wrap an exact UsdSkelAnimation prim.</summary>
    public static bool TryWrap(UsdPrim prim, out UsdSkelAnimation value)
    {
        if (UsdSkelSchema.TryValidate(
            prim,
            OpenUsdNativeSkelSchemaKind.Animation,
            out UsdStage? stage))
        {
            value = new UsdSkelAnimation(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Wraps an exact UsdSkelAnimation prim.</summary>
    public static UsdSkelAnimation Wrap(UsdPrim prim) => new(
        UsdSkelSchema.Validate(
            prim,
            OpenUsdNativeSkelSchemaKind.Animation,
            nameof(UsdSkelAnimation)),
        prim.Path);

    private void SetVectors(
        OpenUsdNativeSkelAnimationVec3Property property,
        ReadOnlySpan<UsdVec3f> values,
        double? timeCode) =>
        Stage.Native.SetSkelAnimationVec3(
            Path,
            property,
            UsdSkelSchema.ToNative(values),
            timeCode);

    private UsdVec3f[] GetVectors(
        OpenUsdNativeSkelAnimationVec3Property property,
        double? timeCode) =>
        UsdSkelSchema.FromNative(
            Stage.Native.GetSkelAnimationVec3(Path, property, timeCode));

    private void SetRotations(ReadOnlySpan<UsdQuatf> values, double? timeCode) =>
        Stage.Native.SetSkelAnimationRotations(
            Path,
            UsdSkelSchema.ToNative(values),
            timeCode);

    private UsdStage Stage =>
        _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}
