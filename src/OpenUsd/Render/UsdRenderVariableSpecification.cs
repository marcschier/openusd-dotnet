// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Render;

/// <summary>An immutable requested render variable, including unsupported source and data types.</summary>
public sealed class UsdRenderVariableSpecification : IUsdDetachedResult
{
    /// <summary>Creates a detached variable, copying unevaluated setting names.</summary>
    public UsdRenderVariableSpecification(
        string path,
        string dataType,
        string sourceName,
        string sourceType,
        IReadOnlyList<string> namespacedSettingNames)
    {
        UsdPath.ValidateAbsolutePrimPath(path);
        ArgumentNullException.ThrowIfNull(dataType);
        ArgumentNullException.ThrowIfNull(sourceName);
        ArgumentNullException.ThrowIfNull(sourceType);
        Path = path;
        DataType = dataType;
        SourceName = sourceName;
        SourceType = sourceType;
        NamespacedSettingNames = UsdRenderSpecification.Copy(namespacedSettingNames, nameof(namespacedSettingNames));
    }

    /// <summary>Gets the variable prim path.</summary>
    public string Path { get; }

    /// <summary>Gets the requested USD data-type token.</summary>
    public string DataType { get; }

    /// <summary>Gets the requested source expression without evaluating it.</summary>
    public string SourceName { get; }

    /// <summary>Gets the source-type token without substituting unsupported outputs with beauty.</summary>
    public string SourceType { get; }

    /// <summary>Gets nonstandard authored property names whose values are not evaluated by this snapshot.</summary>
    public IReadOnlyList<string> NamespacedSettingNames { get; }
}
