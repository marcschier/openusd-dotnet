// Copyright (c) marcschier. Licensed under the MIT License.

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
    RenderHdrColorFormat HdrColorFormat);

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
            GetExrUnsupportedReason(product),
            unsupported);
    }

    private static string? GetExrUnsupportedReason(UsdRenderProductSpecification product) =>
        product.DataWindowNdc.X != 0 || product.DataWindowNdc.Y != 0 ||
        product.DataWindowNdc.Z != 1 || product.DataWindowNdc.W != 1 ||
        product.PixelAspectRatio != 1
            ? "EXR output currently refuses cropped, overscanned or non-square-pixel products"
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
