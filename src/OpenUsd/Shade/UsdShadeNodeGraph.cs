// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Shade;

/// <summary>A validated UsdShadeNodeGraph schema view.</summary>
public readonly struct UsdShadeNodeGraph : IUsdStageBound
{
    internal UsdShadeNodeGraph(UsdStage stage, string path)
    {
        Stage = stage;
        Path = path;
    }

    /// <summary>Gets the node-graph prim path.</summary>
    public string Path { get; }

    /// <summary>Gets the underlying prim descriptor.</summary>
    public UsdPrim Prim => Stage.GetPrim(Path);

    /// <summary>Gets generic connectability operations for the node graph.</summary>
    public UsdShadeConnectable Connectable => new(Stage, Path);

    /// <summary>Tries to wrap a UsdShadeNodeGraph prim.</summary>
    public static bool TryWrap(UsdPrim prim, out UsdShadeNodeGraph value)
    {
        if (UsdShadeSchema.TryValidate(
                prim,
                static (native, path) => native.IsShadeNodeGraph(path),
                out UsdStage? stage))
        {
            value = new UsdShadeNodeGraph(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Wraps a UsdShadeNodeGraph prim or throws for a wrong schema.</summary>
    public static UsdShadeNodeGraph Wrap(UsdPrim prim) =>
        new(
            UsdShadeSchema.Validate(
                prim,
                static (native, path) => native.IsShadeNodeGraph(path),
                nameof(UsdShadeNodeGraph)),
            prim.Path);

    /// <summary>Creates or validates an interface input.</summary>
    public UsdShadeInput CreateInput(string name, UsdShadeValueType valueType) =>
        Connectable.CreateInput(name, valueType);

    /// <summary>Gets an existing supported interface input.</summary>
    public UsdShadeInput GetInput(string name) => Connectable.GetInput(name);

    /// <summary>Creates or validates an output.</summary>
    public UsdShadeOutput CreateOutput(string name, UsdShadeValueType valueType) =>
        Connectable.CreateOutput(name, valueType);

    /// <summary>Gets an existing supported output.</summary>
    public UsdShadeOutput GetOutput(string name) => Connectable.GetOutput(name);

    /// <summary>Gets authored interface input names.</summary>
    public IReadOnlyList<string> GetInputNames() => Connectable.GetInputNames();

    /// <summary>Gets authored output names.</summary>
    public IReadOnlyList<string> GetOutputNames() => Connectable.GetOutputNames();

    internal UsdStage Stage { get; }
}
