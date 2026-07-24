// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

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

    /// <summary>Connects the universal surface output to a shader output.</summary>
    public void ConnectSurface(UsdShadeOutput source) =>
        CreateSurfaceOutput().ConnectToSource(source);

    /// <summary>Binds this material directly to a prim.</summary>
    public void Bind(UsdPrim prim)
    {
        UsdShadeSchema.ValidateSameStage(Stage, prim.OwningStage);
        Stage.Native.BindMaterial(prim.Path, Path);
    }

    /// <summary>Removes a direct binding from a prim.</summary>
    public void Unbind(UsdPrim prim)
    {
        UsdShadeSchema.ValidateSameStage(Stage, prim.OwningStage);
        Stage.Native.UnbindMaterial(prim.Path);
    }

    internal UsdStage Stage { get; }
}
