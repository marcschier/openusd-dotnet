// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

#pragma warning disable CS1591

namespace OpenUsd.Geom;

/// <summary>A validated view of the UsdGeomPrimvarsAPI on a prim.</summary>
[ExcludeFromCodeCoverage(Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdGeomPrimvarsAPI : IUsdStageBound
{
    private readonly UsdStage? _stage;

    public UsdGeomPrimvarsAPI(UsdPrim prim)
    {
        _stage = UsdGeomFacade.Validate(prim, UsdGeomSchemaKind.PrimvarsAPI, nameof(UsdGeomPrimvarsAPI));
        Path = prim.Path;
    }

    internal UsdGeomPrimvarsAPI(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }

    public string Path { get; }
    public UsdPrim Prim => Stage.GetPrim(Path);
    public UsdGeomPrimvar GetPrimvar(string name) => new(Stage, Path, ValidatePrimvarName(name));

    public UsdGeomPrimvar CreatePrimvar(
        string name,
        UsdGeomInterpolation interpolation = UsdGeomInterpolation.Constant,
        int elementSize = 1)
    {
        UsdGeomPrimvar primvar = GetPrimvar(name);
        primvar.Interpolation = interpolation;
        primvar.ElementSize = elementSize;
        return primvar;
    }

    public static bool TryWrap(UsdPrim prim, out UsdGeomPrimvarsAPI value)
    {
        if (UsdGeomFacade.TryWrap(prim, UsdGeomSchemaKind.PrimvarsAPI, out UsdStage? stage))
        {
            value = new UsdGeomPrimvarsAPI(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    public static UsdGeomPrimvarsAPI Wrap(UsdPrim prim) => new(prim);

    private UsdStage Stage => UsdGeomFacade.Require(_stage);

    private static string ValidatePrimvarName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Contains(':', StringComparison.Ordinal) || name.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException("Primvar names must be simple names without ':' or '/'.", nameof(name));
        }
        return name;
    }
}

