// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Mcp;

internal sealed class AnalysisCameraAnalyzer : IProposalAnalyzer
{
    public AnalysisCategory Category => AnalysisCategory.Camera;

    public IEnumerable<ProposalDraft> Analyze(AnalysisInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        CameraTechnicalSnapshot camera = input.Scene.Camera;
        PerformanceSnapshot frame = input.Performance;
        double score = ComputeScore(camera, frame);
        ProposalEvidence[] commonEvidence =
        [
            Evidence("technicalScore", score),
            Evidence("subjectCoverage", camera.SubjectCoverage),
            Evidence("finitePixelRatio", frame.FinitePixelRatio),
            new ProposalEvidence("drawSucceeded", frame.DrawSucceeded ? "true" : "false"),
        ];

        if (camera.SubjectCoverage is < 0.35 or > 0.90)
        {
            yield return Draft(
                "camera.framing",
                "Improve subject framing",
                ProposalApplicability.DiagnosticOnly,
                ProposalRisk.Low,
                "Keep the subject within the technical coverage range of 35% to 90%.",
                new ProposalPayload(
                    "inspect-camera-framing",
                    [new("targetCoverage", "0.65")]),
                commonEvidence,
                [new("subjectCoverage", camera.SubjectCoverage, 0.65, "ratio")]);
        }

        bool clippingInvalid =
            !double.IsFinite(camera.NearClip) ||
            !double.IsFinite(camera.FarClip) ||
            camera.NearClip <= 0 ||
            camera.FarClip <= camera.NearClip ||
            camera.NearestGeometryDistance < camera.NearClip ||
            camera.FarthestGeometryDistance > camera.FarClip;
        if (clippingInvalid)
        {
            double proposedNear = Math.Max(0.001, camera.NearestGeometryDistance * 0.5);
            double proposedFar = Math.Max(
                ScaleWithFiniteCeiling(proposedNear, 2),
                ScaleWithFiniteCeiling(camera.FarthestGeometryDistance, 1.1));
            yield return Draft(
                "camera.clipping",
                "Correct camera clipping range",
                ProposalApplicability.DiagnosticOnly,
                ProposalRisk.Medium,
                "The authored clipping range is invalid or excludes measured scene geometry.",
                new ProposalPayload(
                    "inspect-camera-clipping",
                    [
                        new("near", Format(proposedNear)),
                        new("far", Format(proposedFar)),
                    ]),
                [
                    .. commonEvidence,
                    Evidence("nearClip", camera.NearClip),
                    Evidence("farClip", camera.FarClip),
                    Evidence("nearestGeometryDistance", camera.NearestGeometryDistance),
                    Evidence("farthestGeometryDistance", camera.FarthestGeometryDistance),
                ]);
        }

        if (frame.BackgroundPixelRatio > 0.80)
        {
            yield return Draft(
                "camera.background",
                "Reduce empty background",
                ProposalApplicability.DiagnosticOnly,
                ProposalRisk.Low,
                "More than 80% of the frame is background; tighter framing can improve readability.",
                new ProposalPayload(
                    "inspect-camera-framing",
                    [new("targetCoverage", "0.65")]),
                [.. commonEvidence, Evidence("backgroundPixelRatio", frame.BackgroundPixelRatio)],
                [new("backgroundPixelRatio", frame.BackgroundPixelRatio, 0.35, "ratio")]);
        }

        if (!frame.DrawSucceeded || frame.FinitePixelRatio < 0.999)
        {
            yield return Draft(
                "camera.frame-integrity",
                "Investigate frame integrity",
                ProposalApplicability.DiagnosticOnly,
                ProposalRisk.High,
                "A failed draw or non-finite pixel output makes camera quality conclusions unreliable.",
                new ProposalPayload("diagnose-frame-integrity"),
                commonEvidence);
        }
    }

    public static double ComputeScore(
        CameraTechnicalSnapshot camera,
        PerformanceSnapshot frame)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(frame);

        double coverageScore = 1 - Math.Min(1, Math.Abs(camera.SubjectCoverage - 0.65) / 0.65);
        double clippingScore =
            double.IsFinite(camera.NearClip) &&
            double.IsFinite(camera.FarClip) &&
            camera.NearClip > 0 &&
            camera.FarClip > camera.NearClip &&
            camera.NearestGeometryDistance >= camera.NearClip &&
            camera.FarthestGeometryDistance <= camera.FarClip
                ? 1
                : 0;
        double backgroundScore = 1 - frame.BackgroundPixelRatio;
        double drawScore = frame.DrawSucceeded ? 1 : 0;

        return Math.Round(
            100 * ((coverageScore * 0.30) +
                   (clippingScore * 0.25) +
                   (backgroundScore * 0.15) +
                   (frame.FinitePixelRatio * 0.20) +
                   (drawScore * 0.10)),
            2,
            MidpointRounding.AwayFromZero);
    }

    private static ProposalDraft Draft(
        string code,
        string title,
        ProposalApplicability applicability,
        ProposalRisk risk,
        string explanation,
        ProposalPayload payload,
        IEnumerable<ProposalEvidence> evidence,
        IEnumerable<ProposalExpectedMetric>? metrics = null) =>
        new(
            AnalysisCategory.Camera,
            code,
            title,
            applicability,
            risk,
            explanation,
            payload,
            evidence,
            metrics);

    private static ProposalEvidence Evidence(string name, double value) =>
        new(name, Format(value));

    private static string Format(double value) =>
        value.ToString("G17", CultureInfo.InvariantCulture);

    private static double ScaleWithFiniteCeiling(double value, double scale)
    {
        double scaled = value * scale;
        return double.IsFinite(scaled) ? scaled : double.MaxValue;
    }
}
