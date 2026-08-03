// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Shade;

/// <summary>A connectable UsdShade prim view.</summary>
public readonly struct UsdShadeConnectable : IUsdStageBound
{
    internal UsdShadeConnectable(UsdStage stage, string path)
    {
        Stage = stage;
        Path = path;
    }

    /// <summary>Gets the connectable prim path.</summary>
    public string Path { get; }

    /// <summary>Gets the underlying prim descriptor.</summary>
    public UsdPrim Prim => Stage.GetPrim(Path);

    /// <summary>Gets authored input names.</summary>
    public IReadOnlyList<string> GetInputNames() => Stage.Native.GetShadeInputNames(Path);

    /// <summary>Gets authored output names.</summary>
    public IReadOnlyList<string> GetOutputNames() => Stage.Native.GetShadeOutputNames(Path);

    /// <summary>Creates or validates an input.</summary>
    public UsdShadeInput CreateInput(string name, UsdShadeValueType valueType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Stage.Native.CreateShadeInput(Path, name, UsdShadeSchema.ToNative(valueType));
        return new UsdShadeInput(Stage, Path, name, valueType);
    }

    /// <summary>Gets an existing supported input.</summary>
    public UsdShadeInput GetInput(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var type = (UsdShadeValueType)Stage.Native.GetShadeInputType(Path, name);
        return new UsdShadeInput(Stage, Path, name, type);
    }

    /// <summary>Creates or validates an output.</summary>
    public UsdShadeOutput CreateOutput(string name, UsdShadeValueType valueType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Stage.Native.CreateShadeOutput(Path, name, UsdShadeSchema.ToNative(valueType));
        return new UsdShadeOutput(Stage, Path, name, valueType);
    }

    /// <summary>Gets an existing supported output.</summary>
    public UsdShadeOutput GetOutput(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var type = (UsdShadeValueType)Stage.Native.GetShadeOutputType(Path, name);
        return new UsdShadeOutput(Stage, Path, name, type);
    }

    internal UsdStage Stage { get; }
}
