// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Render;

/// <summary>An immutable, stage-detached standard OpenUSD render specification.</summary>
/// <remarks>
/// Native computation uses uniform/default-time settings, including camera aperture.
/// This describes requested outputs, not renderer support or animated per-frame camera evaluation.
/// Namespaced setting values are not evaluated; their names remain explicit unsupported metadata.
/// </remarks>
public sealed class UsdRenderSpecification : IUsdDetachedResult
{
    /// <summary>Creates a detached specification, copying every collection.</summary>
    public UsdRenderSpecification(
        string settingsPath,
        IReadOnlyList<UsdRenderProductSpecification> products,
        IReadOnlyList<UsdRenderVariableSpecification> renderVariables,
        IReadOnlyList<string> includedPurposes,
        IReadOnlyList<string> materialBindingPurposes,
        string renderingColorSpace,
        IReadOnlyList<string> namespacedSettingNames)
    {
        UsdPath.ValidateAbsolutePrimPath(settingsPath);
        ArgumentNullException.ThrowIfNull(renderingColorSpace);
        SettingsPath = settingsPath;
        Products = Copy(products, nameof(products));
        RenderVariables = Copy(renderVariables, nameof(renderVariables));
        IncludedPurposes = Copy(includedPurposes, nameof(includedPurposes));
        MaterialBindingPurposes = Copy(materialBindingPurposes, nameof(materialBindingPurposes));
        RenderingColorSpace = renderingColorSpace;
        NamespacedSettingNames = Copy(namespacedSettingNames, nameof(namespacedSettingNames));

        foreach (UsdRenderProductSpecification product in Products)
        {
            foreach (int index in product.RenderVariableIndices)
            {
                if ((uint)index >= (uint)RenderVariables.Count)
                {
                    throw new ArgumentException(
                        "A product refers to a render variable outside the snapshot.", nameof(products));
                }
            }
        }
    }

    /// <summary>Gets the composed settings prim path.</summary>
    public string SettingsPath { get; }

    /// <summary>Gets requested products in native relationship order; an empty list means no outputs.</summary>
    public IReadOnlyList<UsdRenderProductSpecification> Products { get; }

    /// <summary>Gets variables shared by products, in native first-use order.</summary>
    public IReadOnlyList<UsdRenderVariableSpecification> RenderVariables { get; }

    /// <summary>Gets the composed scene-purpose filter.</summary>
    public IReadOnlyList<string> IncludedPurposes { get; }

    /// <summary>Gets the composed ordered material-binding purposes.</summary>
    public IReadOnlyList<string> MaterialBindingPurposes { get; }

    /// <summary>Gets the default-time rendering color-space token, which may be empty.</summary>
    public string RenderingColorSpace { get; }

    /// <summary>Gets nonstandard authored property names whose values are not evaluated by this snapshot.</summary>
    public IReadOnlyList<string> NamespacedSettingNames { get; }

    internal static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        T[] copy = values.ToArray();
        foreach (T value in copy)
        {
            if (value is null)
            {
                throw new ArgumentException("Snapshot collections cannot contain null values.", parameterName);
            }
        }
        return new OwnedReadOnlyList<T>(copy);
    }
}
