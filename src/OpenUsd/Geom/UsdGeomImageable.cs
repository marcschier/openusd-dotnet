// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace OpenUsd.Geom;

/// <summary>A validated view of a prim conforming to UsdGeomImageable.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdGeomImageable : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdGeomImageable(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }

    /// <summary>Gets the prim path.</summary>
    public string Path { get; }

    /// <summary>Gets the underlying prim descriptor.</summary>
    public UsdPrim Prim => Stage.GetPrim(Path);

    /// <summary>Tries to wrap a compatible prim.</summary>
    public static bool TryWrap(UsdPrim prim, out UsdGeomImageable value)
    {
        if (UsdGeomSchema.TryValidate(prim, UsdGeomSchemaKind.Imageable, out UsdStage? stage))
        {
            value = new UsdGeomImageable(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Wraps a compatible prim or throws for a wrong schema.</summary>
    public static UsdGeomImageable Wrap(UsdPrim prim) => new(
        UsdGeomSchema.Validate(prim, UsdGeomSchemaKind.Imageable, nameof(UsdGeomImageable)),
        prim.Path);

    /// <summary>Authors visibility at default time.</summary>
    public void SetVisibility(UsdGeomVisibility visibility) =>
        Stage.Native.SetGeomVisibility(Path, (int)visibility);

    /// <summary>Authors visibility at a numeric time code.</summary>
    public void SetVisibility(UsdGeomVisibility visibility, double timeCode) =>
        Stage.Native.SetGeomVisibility(Path, (int)visibility, timeCode);

    /// <summary>Computes composed visibility at default time.</summary>
    public UsdGeomVisibility GetVisibility() =>
        (UsdGeomVisibility)Stage.Native.GetGeomVisibility(Path);

    /// <summary>Computes composed visibility at a numeric time code.</summary>
    public UsdGeomVisibility GetVisibility(double timeCode) =>
        (UsdGeomVisibility)Stage.Native.GetGeomVisibility(Path, timeCode);

    /// <summary>Authors purpose.</summary>
    public void SetPurpose(UsdGeomPurpose purpose) =>
        Stage.Native.SetGeomPurpose(Path, (int)purpose);

    /// <summary>Gets composed purpose.</summary>
    public UsdGeomPurpose GetPurpose() =>
        (UsdGeomPurpose)Stage.Native.GetGeomPurpose(Path);

    private UsdStage Stage =>
        _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}
