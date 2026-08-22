// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp;

internal enum AnalysisCategory
{
    Camera,
    Lighting,
    RenderSettings,
    Performance,
    Composition,
    Validation,
}

internal sealed record AnalysisCoordinates
{
    public AnalysisCoordinates(long sessionGeneration, long stageRevision)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sessionGeneration);
        ArgumentOutOfRangeException.ThrowIfNegative(stageRevision);
        SessionGeneration = sessionGeneration;
        StageRevision = stageRevision;
    }

    public long SessionGeneration { get; init; }

    public long StageRevision { get; init; }
}

internal sealed record CameraTechnicalSnapshot
{
    public CameraTechnicalSnapshot(
        double subjectCoverage,
        double nearClip,
        double farClip,
        double nearestGeometryDistance,
        double farthestGeometryDistance)
    {
        AnalysisNumericValidation.RequireInRange(
            subjectCoverage,
            0,
            1,
            nameof(subjectCoverage));
        AnalysisNumericValidation.RequireFinite(nearClip, nameof(nearClip));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nearClip);
        AnalysisNumericValidation.RequireFinite(farClip, nameof(farClip));
        if (farClip <= nearClip)
        {
            throw new ArgumentOutOfRangeException(
                nameof(farClip),
                "The far clip distance must be greater than the near clip distance.");
        }

        AnalysisNumericValidation.RequireFinite(
            nearestGeometryDistance,
            nameof(nearestGeometryDistance));
        ArgumentOutOfRangeException.ThrowIfNegative(nearestGeometryDistance);
        AnalysisNumericValidation.RequireFinite(
            farthestGeometryDistance,
            nameof(farthestGeometryDistance));
        if (farthestGeometryDistance < nearestGeometryDistance)
        {
            throw new ArgumentOutOfRangeException(
                nameof(farthestGeometryDistance),
                "The farthest geometry distance cannot be less than the nearest distance.");
        }

        SubjectCoverage = subjectCoverage;
        NearClip = nearClip;
        FarClip = farClip;
        NearestGeometryDistance = nearestGeometryDistance;
        FarthestGeometryDistance = farthestGeometryDistance;
    }

    public double SubjectCoverage { get; }

    public double NearClip { get; }

    public double FarClip { get; }

    public double NearestGeometryDistance { get; }

    public double FarthestGeometryDistance { get; }
}

internal sealed record RenderSettingsSnapshot
{
    public RenderSettingsSnapshot(
        int samplesPerPixel,
        bool lightingEnabled,
        bool shadowsEnabled,
        string qualityPreset)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samplesPerPixel);
        ArgumentException.ThrowIfNullOrWhiteSpace(qualityPreset);
        SamplesPerPixel = samplesPerPixel;
        LightingEnabled = lightingEnabled;
        ShadowsEnabled = shadowsEnabled;
        QualityPreset = qualityPreset;
    }

    public int SamplesPerPixel { get; }

    public bool LightingEnabled { get; }

    public bool ShadowsEnabled { get; }

    public string QualityPreset { get; }
}

internal sealed record SceneAnalysisSnapshot
{
    public SceneAnalysisSnapshot(
        int viewportWidth,
        int viewportHeight,
        CameraTechnicalSnapshot camera,
        RenderSettingsSnapshot renderSettings,
        IEnumerable<string>? validationIssues = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportHeight);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(renderSettings);

        string[] issues = (validationIssues ?? []).ToArray();
        if (issues.Any(static issue => string.IsNullOrWhiteSpace(issue)))
        {
            throw new ArgumentException(
                "Validation issues cannot contain null or blank values.",
                nameof(validationIssues));
        }

        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        Camera = camera;
        RenderSettings = renderSettings;
        ValidationIssues = Array.AsReadOnly(
            issues.Order(StringComparer.Ordinal).ToArray());
    }

    public int ViewportWidth { get; }

    public int ViewportHeight { get; }

    public CameraTechnicalSnapshot Camera { get; }

    public RenderSettingsSnapshot RenderSettings { get; }

    public IReadOnlyList<string> ValidationIssues { get; }
}

internal sealed record AnalysisInput
{
    public AnalysisInput(
        AnalysisCoordinates coordinates,
        SceneAnalysisSnapshot scene,
        PerformanceSnapshot performance,
        CompositionSnapshot composition,
        string rendererId)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(performance);
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentException.ThrowIfNullOrWhiteSpace(rendererId);
        Coordinates = coordinates;
        Scene = scene;
        Performance = performance;
        Composition = composition;
        RendererId = rendererId;
    }

    public AnalysisCoordinates Coordinates { get; init; }

    public SceneAnalysisSnapshot Scene { get; init; }

    public PerformanceSnapshot Performance { get; init; }

    public CompositionSnapshot Composition { get; init; }

    public string RendererId { get; init; }
}

internal static class AnalysisNumericValidation
{
    internal static void RequireFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "The value must be finite.");
        }
    }

    internal static void RequireInRange(
        double value,
        double minimum,
        double maximum,
        string parameterName)
    {
        RequireFinite(value, parameterName);
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"The value must be in the inclusive range [{minimum}, {maximum}].");
        }
    }
}
