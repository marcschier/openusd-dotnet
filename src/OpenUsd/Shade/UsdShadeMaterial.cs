// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.Shade;

/// <summary>A validated UsdShadeMaterial schema view.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdShadeMaterial : IUsdStageBound
{
    internal UsdShadeMaterial(UsdStage stage, string path)
    {
        Stage = stage;
        Path = path;
    }

    /// <summary>Gets the material prim path.</summary>
    public string Path { get; }

    /// <summary>Gets the underlying prim descriptor.</summary>
    public UsdPrim Prim => Stage.GetPrim(Path);

    /// <summary>Tries to wrap a UsdShadeMaterial prim.</summary>
    public static bool TryWrap(UsdPrim prim, out UsdShadeMaterial value)
    {
        if (UsdShadeSchema.TryValidate(
                prim,
                static (native, path) => native.IsShadeMaterial(path),
                out UsdStage? stage))
        {
            value = new UsdShadeMaterial(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Wraps a UsdShadeMaterial prim or throws for a wrong schema.</summary>
    public static UsdShadeMaterial Wrap(UsdPrim prim) =>
        new(
            UsdShadeSchema.Validate(
                prim,
                static (native, path) => native.IsShadeMaterial(path),
                nameof(UsdShadeMaterial)),
            prim.Path);

    /// <summary>Creates or validates the universal surface output.</summary>
    public UsdShadeOutput CreateSurfaceOutput()
    {
        Stage.Native.CreateMaterialSurfaceOutput(Path);
        return new UsdShadeOutput(Stage, Path, "surface", UsdShadeValueType.Token);
    }

    /// <summary>Gets the existing universal surface output.</summary>
    public UsdShadeOutput GetSurfaceOutput() =>
        new(Stage, Path, "surface", UsdShadeValueType.Token);

    /// <summary>Creates or validates a terminal output.</summary>
    public UsdShadeOutput CreateTerminalOutput(
        UsdShadeMaterialTerminal terminal,
        string renderContext = "")
    {
        ArgumentNullException.ThrowIfNull(renderContext);
        Stage.Native.CreateMaterialTerminalOutput(
            Path,
            (OpenUsdNativeShadeMaterialTerminal)terminal,
            renderContext);
        return new UsdShadeOutput(Stage, Path, GetTerminalName(terminal, renderContext), UsdShadeValueType.Token);
    }

    /// <summary>Creates or validates the universal displacement output.</summary>
    public UsdShadeOutput CreateDisplacementOutput() =>
        CreateTerminalOutput(UsdShadeMaterialTerminal.Displacement);

    /// <summary>Creates or validates the universal volume output.</summary>
    public UsdShadeOutput CreateVolumeOutput() =>
        CreateTerminalOutput(UsdShadeMaterialTerminal.Volume);

    /// <summary>Connects the universal surface output to a shader output.</summary>
    public void ConnectSurface(UsdShadeOutput source) =>
        CreateSurfaceOutput().ConnectToSource(source);

    /// <summary>Binds this material directly to a prim.</summary>
    public void Bind(UsdPrim prim)
    {
        UsdShadeSchema.ValidateSameStage(Stage, prim.OwningStage);
        Stage.Native.BindMaterial(prim.Path, Path);
    }

    /// <summary>Binds this material directly to a prim with purpose and strength.</summary>
    public void Bind(
        UsdPrim prim,
        UsdShadeBindingStrength strength,
        UsdShadeMaterialPurpose purpose = UsdShadeMaterialPurpose.All)
    {
        UsdShadeSchema.ValidateSameStage(Stage, prim.OwningStage);
        Stage.Native.BindMaterial(
            prim.Path,
            Path,
            (OpenUsdNativeShadeBindingStrength)strength,
            (OpenUsdNativeShadeMaterialPurpose)purpose);
    }

    /// <summary>Binds this material to a collection on a prim.</summary>
    public void BindCollection(
        UsdPrim prim,
        UsdPrim collectionPrim,
        string collectionName,
        string bindingName = "",
        UsdShadeBindingStrength strength = UsdShadeBindingStrength.WeakerThanDescendants,
        UsdShadeMaterialPurpose purpose = UsdShadeMaterialPurpose.All)
    {
        UsdShadeSchema.ValidateSameStage(Stage, prim.OwningStage);
        UsdShadeSchema.ValidateSameStage(Stage, collectionPrim.OwningStage);
        Stage.Native.BindMaterialCollection(
            prim.Path,
            collectionPrim.Path,
            collectionName,
            Path,
            bindingName,
            (OpenUsdNativeShadeBindingStrength)strength,
            (OpenUsdNativeShadeMaterialPurpose)purpose);
    }

    /// <summary>Removes a direct binding from a prim.</summary>
    public void Unbind(UsdPrim prim)
    {
        UsdShadeSchema.ValidateSameStage(Stage, prim.OwningStage);
        Stage.Native.UnbindMaterial(prim.Path);
    }

    internal UsdStage Stage { get; }

    private static string GetTerminalName(UsdShadeMaterialTerminal terminal, string renderContext)
    {
        string terminalName = terminal switch
        {
            UsdShadeMaterialTerminal.Surface => "surface",
            UsdShadeMaterialTerminal.Displacement => "displacement",
            UsdShadeMaterialTerminal.Volume => "volume",
            _ => throw new ArgumentOutOfRangeException(nameof(terminal))
        };
        return string.IsNullOrEmpty(renderContext)
            ? terminalName
            : $"{renderContext}:{terminalName}";
    }
}
