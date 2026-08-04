// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CS1591

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd.Proc;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdProcGenerativeProcedural : IUsdStageBound
{
    private readonly UsdStage? _stage;
    internal UsdProcGenerativeProcedural(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }
    public string Path { get; }
    public UsdPrim Prim => Stage.GetPrim(Path);
    public UsdGeomImageable Imageable => new(Stage, Path);
    public string ProceduralSystem
    {
        get => Prim.GetToken("proceduralSystem");
        set => Prim.SetToken("proceduralSystem", value);
    }
    public static bool TryWrap(UsdPrim prim, out UsdProcGenerativeProcedural value)
    {
        if (UsdProcSchema.TryValidate(prim, OpenUsdNativeProcSchemaKind.GenerativeProcedural, out UsdStage? stage))
        {
            value = new(stage!, prim.Path);
            return true;
        }

        value = default;
        return false;
    }
    public static UsdProcGenerativeProcedural Wrap(UsdPrim prim) =>
        new(
            UsdProcSchema.Validate(
                prim,
                OpenUsdNativeProcSchemaKind.GenerativeProcedural,
                nameof(UsdProcGenerativeProcedural)),
            prim.Path);
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}

