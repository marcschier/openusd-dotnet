// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Render;

/// <summary>An immutable product after native settings inheritance and aspect conformance.</summary>
public sealed class UsdRenderProductSpecification : IUsdDetachedResult
{
    /// <summary>Creates a detached product, copying its ordered indices and unevaluated setting names.</summary>
    public UsdRenderProductSpecification(
        string path,
        string name,
        string productType,
        string cameraPath,
        int width,
        int height,
        float pixelAspectRatio,
        string aspectRatioConformPolicy,
        UsdVec2f apertureSize,
        UsdVec4f dataWindowNdc,
        bool disableMotionBlur,
        bool disableDepthOfField,
        IReadOnlyList<int> renderVariableIndices,
        IReadOnlyList<string> namespacedSettingNames)
    {
        UsdPath.ValidateAbsolutePrimPath(path);
        UsdPath.ValidateAbsolutePrimPath(cameraPath);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(productType);
        ArgumentNullException.ThrowIfNull(aspectRatioConformPolicy);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (!float.IsFinite(pixelAspectRatio) || pixelAspectRatio <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelAspectRatio));
        }
        if (!float.IsFinite(apertureSize.X) || !float.IsFinite(apertureSize.Y) ||
            apertureSize.X <= 0 || apertureSize.Y <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(apertureSize));
        }
        if (!float.IsFinite(dataWindowNdc.X) || !float.IsFinite(dataWindowNdc.Y) ||
            !float.IsFinite(dataWindowNdc.Z) || !float.IsFinite(dataWindowNdc.W) ||
            !float.IsFinite(dataWindowNdc.Z - dataWindowNdc.X) ||
            !float.IsFinite(dataWindowNdc.W - dataWindowNdc.Y) ||
            dataWindowNdc.X >= dataWindowNdc.Z || dataWindowNdc.Y >= dataWindowNdc.W)
        {
            throw new ArgumentOutOfRangeException(nameof(dataWindowNdc));
        }

        Path = path;
        Name = name;
        ProductType = productType;
        CameraPath = cameraPath;
        Width = width;
        Height = height;
        PixelAspectRatio = pixelAspectRatio;
        AspectRatioConformPolicy = aspectRatioConformPolicy;
        ApertureSize = apertureSize;
        DataWindowNdc = dataWindowNdc;
        DisableMotionBlur = disableMotionBlur;
        DisableDepthOfField = disableDepthOfField;
        RenderVariableIndices = UsdRenderSpecification.Copy(renderVariableIndices, nameof(renderVariableIndices));
        NamespacedSettingNames = UsdRenderSpecification.Copy(namespacedSettingNames, nameof(namespacedSettingNames));
        foreach (int index in RenderVariableIndices)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(renderVariableIndices));
        }
    }

    /// <summary>Gets the product prim path.</summary>
    public string Path { get; }

    /// <summary>Gets the authored product name without resolving or opening an output file.</summary>
    public string Name { get; }

    /// <summary>Gets the product type token without claiming renderer support for it.</summary>
    public string ProductType { get; }

    /// <summary>Gets the effective camera prim path.</summary>
    public string CameraPath { get; }

    /// <summary>Gets the pixel width.</summary>
    public int Width { get; }

    /// <summary>Gets the pixel height.</summary>
    public int Height { get; }

    /// <summary>Gets the native-conformed pixel aspect ratio.</summary>
    public float PixelAspectRatio { get; }

    /// <summary>Gets the policy applied by native OpenUSD.</summary>
    public string AspectRatioConformPolicy { get; }

    /// <summary>Gets native-conformed camera aperture at default time, not animated frame time.</summary>
    public UsdVec2f ApertureSize { get; }

    /// <summary>Gets the bottom-left/top-right data window; crop and overscan are preserved.</summary>
    public UsdVec4f DataWindowNdc { get; }

    /// <summary>Gets whether motion blur is disabled by the effective settings.</summary>
    public bool DisableMotionBlur { get; }

    /// <summary>Gets whether depth of field is disabled by the effective settings.</summary>
    public bool DisableDepthOfField { get; }

    /// <summary>Gets ordered indices into the specification's shared variable table.</summary>
    public IReadOnlyList<int> RenderVariableIndices { get; }

    /// <summary>Gets nonstandard authored property names whose values are not evaluated by this snapshot.</summary>
    public IReadOnlyList<string> NamespacedSettingNames { get; }
}
