// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using OpenUsd.Render;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal sealed record ViewerAuthoredRenderProductQuery(
    string? SettingsPath,
    string? SelectedProductPath,
    ViewerRenderSequenceOutputOptions Outputs,
    string OutputParent);

internal sealed record ViewerAuthoredRenderProductRequest(
    ViewerRenderSequenceRange Range,
    string OutputParent,
    string? SettingsPath,
    string ProductPath,
    RenderHdrColorFormat HdrColorFormat,
    RenderProductOverrides? Overrides = null);

internal sealed record ViewerAuthoredRenderProductProgress(
    string Phase,
    int CompletedFrames,
    int TotalFrames,
    double TimeCode);

internal sealed class ViewerAuthoredRenderProductItem
{
    internal ViewerAuthoredRenderProductItem(
        string path,
        string label,
        string cameraPath,
        ViewportDimensions dimensions,
        string variableSummary,
        string? exrUnsupportedReason,
        string? unsupportedReason)
    {
        Path = path;
        Label = label;
        CameraPath = cameraPath;
        Dimensions = dimensions;
        VariableSummary = variableSummary;
        ExrUnsupportedReason = exrUnsupportedReason;
        UnsupportedReason = unsupportedReason;
    }

    internal string Path { get; }

    internal string Label { get; }

    internal string CameraPath { get; }

    internal ViewportDimensions Dimensions { get; }

    internal string VariableSummary { get; }

    internal string? ExrUnsupportedReason { get; }

    internal string? UnsupportedReason { get; }

    internal bool CanRender => UnsupportedReason is null;

    public override string ToString() => Label;
}

internal sealed class ViewerAuthoredRenderProductSnapshot
{
    internal ViewerAuthoredRenderProductSnapshot(
        string settingsPath,
        IReadOnlyList<ViewerAuthoredRenderProductItem> products,
        string? error = null)
    {
        SettingsPath = settingsPath;
        Products = products;
        Error = error;
    }

    internal string SettingsPath { get; }

    internal IReadOnlyList<ViewerAuthoredRenderProductItem> Products { get; }

    internal string? Error { get; }

    internal static ViewerAuthoredRenderProductSnapshot Empty(string? error) => new(string.Empty, [], error);
}

internal static class ViewerAuthoredRenderProductSelection
{
    private const int MaximumProducts = 256;

    internal static string? ValidateOutputParent(string outputParent)
    {
        if (string.IsNullOrWhiteSpace(outputParent) || !Path.IsPathFullyQualified(outputParent))
        {
            return "Choose an existing absolute local output folder.";
        }
        return Directory.Exists(Path.GetFullPath(outputParent))
            ? null
            : "The chosen output folder must already exist.";
    }

    internal static ViewerAuthoredRenderProductSnapshot CreateSnapshot(
        UsdRenderSpecification? specification,
        string? selectedProductPath)
    {
        if (specification is null)
        {
            return ViewerAuthoredRenderProductSnapshot.Empty(
                "The active stage has no authored default render settings; enter an explicit settings path.");
        }
        if (specification.Products.Count > MaximumProducts)
        {
            return new ViewerAuthoredRenderProductSnapshot(specification.SettingsPath, [],
                $"The selected settings contain {specification.Products.Count} products; the Viewer catalog limit is " +
                $"{MaximumProducts}. Select render settings with fewer products; no partial catalog was admitted.");
        }
        return new ViewerAuthoredRenderProductSnapshot(
            specification.SettingsPath,
            [.. specification.Products.Select((product, index) =>
                CreateItem(specification, product, index, product.Path == selectedProductPath))]);
    }

    private static ViewerAuthoredRenderProductItem CreateItem(
        UsdRenderSpecification specification,
        UsdRenderProductSpecification product,
        int index,
        bool selected)
    {
        string variables = string.Join(", ", product.RenderVariableIndices.Select(variableIndex =>
        {
            UsdRenderVariableSpecification variable = specification.RenderVariables[variableIndex];
            return $"{variable.SourceType}:{variable.SourceName}/{variable.DataType}";
        }));
        string? unsupported = GetStaticUnsupportedReason(specification, index);
        string label = $"{product.Path}  {product.Width}x{product.Height}  camera {product.CameraPath}";
        if (selected)
        {
            label = $"* {label}";
        }
        if (unsupported is not null)
        {
            label += $"  - unsupported: {unsupported}";
        }
        return new ViewerAuthoredRenderProductItem(
            product.Path,
            label,
            product.CameraPath,
            new ViewportDimensions(product.Width, product.Height),
            variables.Length == 0 ? "no variables" : variables,
            GetExrUnsupportedReason(product.PixelAspectRatio),
            unsupported);
    }

    internal static string? GetExrUnsupportedReason(double pixelAspectRatio) =>
        !double.IsFinite(pixelAspectRatio) || pixelAspectRatio < 1d / 65536 || pixelAspectRatio > 65536
            ? "EXR output stores pixel aspect only within 1/65536..65536"
            : null;

    private static string? GetStaticUnsupportedReason(
        UsdRenderSpecification specification,
        int productIndex)
    {
        try
        {
            _ = RenderProductJobPlan.ValidateStaticRequest(new RenderProductRequest(specification, productIndex));
            return null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return exception.Message;
        }
    }
}

internal static class ViewerAuthoredRenderProductOverrides
{
    internal const int MaximumRasterSide = 8192;

    internal static IReadOnlyList<string> ConformPolicies { get; } =
    [
        "expandAperture",
        "cropAperture",
        "adjustApertureWidth",
        "adjustApertureHeight",
        "adjustPixelAspectRatio"
    ];

    internal static RenderProductOverrides Parse(
        string? cameraPath,
        string? width,
        string? height,
        string? pixelAspectRatio,
        string? conformPolicy,
        string? dataWindowNdc)
    {
        string? camera = Normalize(cameraPath);
        if (camera is not null)
        {
            try
            {
                UsdPath.ValidateAbsolutePrimPath(camera, nameof(cameraPath));
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException("The override camera must be an absolute prim path.", exception);
            }
        }
        string? widthText = Normalize(width);
        string? heightText = Normalize(height);
        ViewportDimensions? resolution = null;
        if (widthText is not null || heightText is not null)
        {
            if (!TryParseSide(widthText, out int parsedWidth) || !TryParseSide(heightText, out int parsedHeight))
            {
                throw new ArgumentException(
                    $"Override width and height must be supplied together, each 1-{MaximumRasterSide} pixels.");
            }
            resolution = new ViewportDimensions(parsedWidth, parsedHeight);
        }
        double? aspect = null;
        if (Normalize(pixelAspectRatio) is { } aspectText)
        {
            if (!double.TryParse(aspectText, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                !double.IsFinite(value) || value <= 0)
            {
                throw new ArgumentException("The override pixel aspect must be a finite positive number.");
            }
            aspect = value;
        }
        string? policy = Normalize(conformPolicy);
        if (policy is not null && !ConformPolicies.Contains(policy, StringComparer.Ordinal))
        {
            throw new ArgumentException("Choose a standard OpenUSD aspect-ratio conform policy.");
        }
        UsdVec4f? window = null;
        if (Normalize(dataWindowNdc) is { } windowText)
        {
            window = ParseDataWindow(windowText);
        }
        return camera is null && resolution is null && aspect is null && policy is null && window is null
            ? RenderProductOverrides.None
            : new RenderProductOverrides(camera, resolution, aspect, policy, window);
    }

    internal static string Describe(RenderProductOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var parts = new List<string>(5);
        if (overrides.CameraPath is { } camera)
        {
            parts.Add($"camera {camera}");
        }
        if (overrides.Resolution is { } size)
        {
            parts.Add(FormattableString.Invariant($"raster {size.Width} x {size.Height}"));
        }
        if (overrides.PixelAspectRatio is { } aspect)
        {
            parts.Add(FormattableString.Invariant($"pixel aspect {aspect}"));
        }
        if (overrides.AspectRatioConformPolicy is { } policy)
        {
            parts.Add($"conform {policy}");
        }
        if (overrides.DataWindowNdc is { } window)
        {
            parts.Add(FormattableString.Invariant(
                $"data window ({window.X}, {window.Y}, {window.Z}, {window.W})"));
        }
        return parts.Count == 0 ? "none (authored values)" : string.Join("; ", parts);
    }

    private static UsdVec4f ParseDataWindow(string text)
    {
        string[] values = text.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var bounds = new float[4];
        if (values.Length != 4)
        {
            throw new ArgumentException(
                "The override data window needs four NDC bounds: minX minY maxX maxY.");
        }
        for (int index = 0; index < 4; index++)
        {
            if (!float.TryParse(values[index], NumberStyles.Float, CultureInfo.InvariantCulture, out bounds[index]) ||
                !float.IsFinite(bounds[index]))
            {
                throw new ArgumentException(
                    "The override data window needs four finite NDC bounds: minX minY maxX maxY.");
            }
        }
        if (bounds[0] >= bounds[2] || bounds[1] >= bounds[3])
        {
            throw new ArgumentException("The override data window minimum must be below its maximum on both axes.");
        }
        return new UsdVec4f(bounds[0], bounds[1], bounds[2], bounds[3]);
    }

    private static bool TryParseSide(string? text, out int value)
    {
        value = 0;
        return text is not null &&
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
            value is >= 1 and <= MaximumRasterSide;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
