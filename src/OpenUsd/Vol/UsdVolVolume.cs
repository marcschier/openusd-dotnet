// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CS1591

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd.Vol;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdVolVolume : IUsdStageBound
{
    private readonly UsdStage? _stage;
    internal UsdVolVolume(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }
    public string Path { get; }
    public UsdPrim Prim => Stage.GetPrim(Path);
    public UsdGeomXformable Xformable => new(Stage, Path);

    public IReadOnlyDictionary<string, string> GetFieldPaths()
    {
        string[] pairs = Stage.Native.GetVolFieldPathPairs(Path);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i + 1 < pairs.Length; i += 2)
        {
            result[pairs[i]] = pairs[i + 1];
        }

        return result;
    }
    public void SetField(string fieldName, UsdVolVolumeFieldBase field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        UsdVolSchema.ValidateAttachedPrim(field.Prim);
        Stage.Native.SetVolFieldPath(Path, fieldName, field.Path);
    }
    public bool HasFieldRelationship(string fieldName) => Stage.Native.HasVolFieldRelationship(Path, fieldName);
    public void BlockFieldRelationship(string fieldName) => Stage.Native.BlockVolFieldRelationship(Path, fieldName);

    public static bool TryWrap(UsdPrim prim, out UsdVolVolume value)
    {
        if (UsdVolSchema.TryValidate(prim, OpenUsdNativeVolSchemaKind.Volume, out UsdStage? stage))
        {
            value = new UsdVolVolume(stage!, prim.Path);
            return true;
        }

        value = default;
        return false;
    }
    public static UsdVolVolume Wrap(UsdPrim prim) =>
        new(
            UsdVolSchema.Validate(prim, OpenUsdNativeVolSchemaKind.Volume, nameof(UsdVolVolume)),
            prim.Path);
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}

