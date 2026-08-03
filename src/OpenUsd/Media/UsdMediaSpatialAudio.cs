// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CS1591

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd.Media;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdMediaSpatialAudio : IUsdStageBound
{
    private readonly UsdStage? _stage;
    internal UsdMediaSpatialAudio(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }
    public string Path { get; }
    public UsdPrim Prim => Stage.GetPrim(Path);
    public UsdGeomXformable Xformable => new(Stage, Path);
    public UsdAssetPath FilePath
    {
        get => new(Stage.Native.GetMediaAsset(Path, OpenUsdNativeMediaAssetProperty.FilePath));
        set => Stage.Native.SetMediaAsset(Path, OpenUsdNativeMediaAssetProperty.FilePath, value.Path);
    }
    public string AuralMode
    {
        get => Prim.GetToken("auralMode");
        set => Prim.SetToken("auralMode", value);
    }
    public string PlaybackMode
    {
        get => Prim.GetToken("playbackMode");
        set => Prim.SetToken("playbackMode", value);
    }
    public double StartTime
    {
        get => Prim.GetDouble("startTime");
        set => Prim.SetDouble("startTime", value);
    }
    public double EndTime
    {
        get => Prim.GetDouble("endTime");
        set => Prim.SetDouble("endTime", value);
    }
    public double MediaOffset
    {
        get => Prim.GetDouble("mediaOffset");
        set => Prim.SetDouble("mediaOffset", value);
    }
    public double Gain
    {
        get => Prim.GetDouble("gain");
        set => Prim.SetDouble("gain", value);
    }
    public static bool TryWrap(UsdPrim prim, out UsdMediaSpatialAudio value)
    {
        if (UsdMediaSchema.TryValidate(prim, OpenUsdNativeMediaSchemaKind.SpatialAudio, out UsdStage? stage))
        {
            value = new(stage!, prim.Path);
            return true;
        }

        value = default;
        return false;
    }
    public static UsdMediaSpatialAudio Wrap(UsdPrim prim) =>
        new(
            UsdMediaSchema.Validate(prim, OpenUsdNativeMediaSchemaKind.SpatialAudio, nameof(UsdMediaSpatialAudio)),
            prim.Path);
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}


