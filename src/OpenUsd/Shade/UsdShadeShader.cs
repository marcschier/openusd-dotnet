// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace OpenUsd.Shade;

/// <summary>A validated UsdShadeShader schema view.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdShadeShader : IUsdStageBound
{
    internal UsdShadeShader(UsdStage stage, string path)
    {
        Stage = stage;
        Path = path;
    }

    /// <summary>Gets the shader prim path.</summary>
    public string Path { get; }

    /// <summary>Gets the underlying prim descriptor.</summary>
    public UsdPrim Prim => Stage.GetPrim(Path);

    /// <summary>Gets generic connectability operations for the shader.</summary>
    public UsdShadeConnectable Connectable => new(Stage, Path);

    /// <summary>Gets or sets the shader source identifier.</summary>
    public string SourceId
    {
        get => Stage.Native.GetShaderSourceId(Path);
        set => Stage.Native.SetShaderSourceId(Path, value);
    }

    /// <summary>Tries to wrap a UsdShadeShader prim.</summary>
    public static bool TryWrap(UsdPrim prim, out UsdShadeShader value)
    {
        if (UsdShadeSchema.TryValidate(
                prim,
                static (native, path) => native.IsShadeShader(path),
                out UsdStage? stage))
        {
            value = new UsdShadeShader(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Wraps a UsdShadeShader prim or throws for a wrong schema.</summary>
    public static UsdShadeShader Wrap(UsdPrim prim) =>
        new(
            UsdShadeSchema.Validate(
                prim,
                static (native, path) => native.IsShadeShader(path),
                nameof(UsdShadeShader)),
            prim.Path);

    /// <summary>Creates or validates a float input.</summary>
    public UsdShadeInput CreateInputFloat(string name) =>
        CreateInput(name, UsdShadeValueType.Float);

    /// <summary>Creates or validates a color3f input.</summary>
    public UsdShadeInput CreateInputColor3f(string name) =>
        CreateInput(name, UsdShadeValueType.Color3f);

    /// <summary>Creates or validates a vector3f input.</summary>
    public UsdShadeInput CreateInputVector3f(string name) =>
        CreateInput(name, UsdShadeValueType.Vector3f);

    /// <summary>Creates or validates a normal3f input.</summary>
    public UsdShadeInput CreateInputNormal3f(string name) =>
        CreateInput(name, UsdShadeValueType.Normal3f);

    /// <summary>Creates or validates a token input.</summary>
    public UsdShadeInput CreateInputToken(string name) =>
        CreateInput(name, UsdShadeValueType.Token);

    /// <summary>Creates or validates a string input.</summary>
    public UsdShadeInput CreateInputString(string name) =>
        CreateInput(name, UsdShadeValueType.String);

    /// <summary>Creates or validates an asset-path input.</summary>
    public UsdShadeInput CreateInputAsset(string name) =>
        CreateInput(name, UsdShadeValueType.Asset);

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

    /// <summary>Gets authored input names.</summary>
    public IReadOnlyList<string> GetInputNames() => Connectable.GetInputNames();

    /// <summary>Gets authored output names.</summary>
    public IReadOnlyList<string> GetOutputNames() => Connectable.GetOutputNames();

    private UsdShadeInput CreateInput(string name, UsdShadeValueType valueType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Stage.Native.CreateShadeInput(Path, name, UsdShadeSchema.ToNative(valueType));
        return new UsdShadeInput(Stage, Path, name, valueType);
    }
}
