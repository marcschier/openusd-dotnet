// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CS1591

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.UI;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdUIBackdrop : IUsdStageBound
{
    private readonly UsdStage? _stage;
    internal UsdUIBackdrop(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }
    public string Path { get; }
    public UsdPrim Prim => Stage.GetPrim(Path);

    public string Description
    {
        get => Prim.GetString("description");
        set => Prim.SetString("description", value);
    }

    public static bool TryWrap(UsdPrim prim, out UsdUIBackdrop value)
    {
        if (UsdUISchema.TryValidate(prim, OpenUsdNativeUiSchemaKind.Backdrop, out UsdStage? stage))
        {
            value = new UsdUIBackdrop(stage!, prim.Path);
            return true;
        }

        value = default;
        return false;
    }
    public static UsdUIBackdrop Wrap(UsdPrim prim) =>
        new(
            UsdUISchema.Validate(prim, OpenUsdNativeUiSchemaKind.Backdrop, nameof(UsdUIBackdrop)),
            prim.Path);
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}


