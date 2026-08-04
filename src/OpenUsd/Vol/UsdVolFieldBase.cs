// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CS1591

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd.Vol;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdVolFieldBase : IUsdStageBound
{
    private readonly UsdStage? _stage;
    internal UsdVolFieldBase(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }
    public string Path { get; }
    public UsdPrim Prim => Stage.GetPrim(Path);
    public UsdGeomXformable Xformable => new(Stage, Path);

    public static bool TryWrap(UsdPrim prim, out UsdVolFieldBase value)
    {
        if (UsdVolSchema.TryValidate(prim, OpenUsdNativeVolSchemaKind.FieldBase, out UsdStage? stage))
        {
            value = new UsdVolFieldBase(stage!, prim.Path);
            return true;
        }

        value = default;
        return false;
    }
    public static UsdVolFieldBase Wrap(UsdPrim prim) =>
        new(
            UsdVolSchema.Validate(prim, OpenUsdNativeVolSchemaKind.FieldBase, nameof(UsdVolFieldBase)),
            prim.Path);
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}


