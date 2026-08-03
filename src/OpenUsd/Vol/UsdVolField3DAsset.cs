// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CS1591

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd.Vol;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdVolField3DAsset : IUsdStageBound
{
    private readonly UsdStage? _stage;
    internal UsdVolField3DAsset(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }
    public string Path { get; }
    public UsdPrim Prim => Stage.GetPrim(Path);
    public UsdGeomXformable Xformable => new(Stage, Path);

    public string FieldPurpose
    {
        get => Prim.GetToken("fieldPurpose");
        set => Prim.SetToken("fieldPurpose", value);
    }

    public static bool TryWrap(UsdPrim prim, out UsdVolField3DAsset value)
    {
        if (UsdVolSchema.TryValidate(prim, OpenUsdNativeVolSchemaKind.Field3dAsset, out UsdStage? stage))
        {
            value = new UsdVolField3DAsset(stage!, prim.Path);
            return true;
        }

        value = default;
        return false;
    }
    public static UsdVolField3DAsset Wrap(UsdPrim prim) =>
        new(
            UsdVolSchema.Validate(prim, OpenUsdNativeVolSchemaKind.Field3dAsset, nameof(UsdVolField3DAsset)),
            prim.Path);
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}


