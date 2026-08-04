// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CS1591

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd.Render;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdRenderSettingsBase : IUsdStageBound
{
    private readonly UsdStage? _stage;
    internal UsdRenderSettingsBase(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }
    public string Path { get; }
    public UsdPrim Prim => Stage.GetPrim(Path);

    public UsdRelationship Camera => Prim.GetRelationship("camera");
    public UsdAttribute Resolution => Prim.GetAttribute("resolution");
    public void SetCamera(UsdGeomCamera camera) =>
        Prim.SetRelationshipTargets("camera", [camera.Path]);
    public string[] GetCameraTargets() => Prim.GetRelationshipTargets("camera");
    public void SetResolution(int width, int height) =>
        Stage.Native.SetRenderResolution(Path, width, height);
    public void GetResolution(out int width, out int height) =>
        Stage.Native.GetRenderResolution(Path, out width, out height);
    public void SetDataWindowNdc(float minX, float minY, float maxX, float maxY) =>
        Stage.Native.SetRenderDataWindowNdc(Path, minX, minY, maxX, maxY);
    public void GetDataWindowNdc(out float minX, out float minY, out float maxX, out float maxY) =>
        Stage.Native.GetRenderDataWindowNdc(Path, out minX, out minY, out maxX, out maxY);
    public bool DisableMotionBlur
    {
        get => Prim.GetBool("disableMotionBlur");
        set => Prim.SetBool("disableMotionBlur", value);
    }
    public bool DisableDepthOfField
    {
        get => Prim.GetBool("disableDepthOfField");
        set => Prim.SetBool("disableDepthOfField", value);
    }
    public UsdAttribute PixelAspectRatio => Prim.GetAttribute("pixelAspectRatio");
    public string AspectRatioConformPolicy
    {
        get => Prim.GetToken("aspectRatioConformPolicy");
        set => Prim.SetToken("aspectRatioConformPolicy", value);
    }

    public static bool TryWrap(UsdPrim prim, out UsdRenderSettingsBase value)
    {
        if (UsdRenderSchema.TryValidate(prim, OpenUsdNativeRenderSchemaKind.SettingsBase, out UsdStage? stage))
        {
            value = new UsdRenderSettingsBase(stage!, prim.Path);
            return true;
        }

        value = default;
        return false;
    }
    public static UsdRenderSettingsBase Wrap(UsdPrim prim) =>
        new(
            UsdRenderSchema.Validate(prim, OpenUsdNativeRenderSchemaKind.SettingsBase, nameof(UsdRenderSettingsBase)),
            prim.Path);
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}



