// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// The analytic equivalence gate for the ABI v20 bounded deformation block, and
/// the diagnosis gate for what a published rig omitted.
/// </summary>
/// <remarks>
/// <para>
/// hdSilk resolves the supported UsdSkel subset on the CPU in double precision
/// and publishes the deformed points; the bounded rig published beside them is
/// what a backend deformation kernel would consume instead. The whole seam is
/// only worth anything if the two agree, so this gate syncs the real skinned
/// probe stage through the real native delegate and requires the renderer-
/// neutral <see cref="SilkDeformationEvaluator"/> -- the same contract a kernel
/// must satisfy -- to reproduce the CPU-resolved points from the rig's own
/// published bytes.
/// </para>
/// <para>
/// It is deliberately analytic rather than pixel-based. No shader evaluates the
/// block yet, so a pixel comparison would compare the CPU path against itself
/// and prove nothing; this comparison fails exactly when a rig describes a
/// different surface from the one the page already promised, which is the only
/// failure a GPU consumer could not detect for itself.
/// </para>
/// <para>
/// The evaluation runs in single precision, as a kernel would, so the gate also
/// measures whether the published rig survives the precision a GPU consumer has
/// rather than only the precision the producer used.
/// </para>
/// </remarks>
public sealed partial class StormSilkParityCaptureDriverTests
{
    private const int DeformationProbeWidth = 64;
    private const int DeformationProbeHeight = 64;

    [Test]
    [Arguments(1.0)]
    [Arguments(2.0)]
    [Arguments(3.0)]
    public async Task PublishedDeformationRigReproducesTheCpuResolvedPoints(double timeCode)
    {
        string stagePath = ResolveDeformationProbeStage();
        if (stagePath.Length == 0)
        {
            Skip.Test("The hdSilk deformation probe stage is not available.");
            return;
        }

        List<RigComparison> comparisons = CollectRigComparisonsOrSkip(stagePath, timeCode);

        // Non-vacuity: a page that published no rig at all would satisfy every
        // per-rig assertion below while proving nothing about the seam.
        await Assert.That(comparisons.Count)
            .IsGreaterThanOrEqualTo(2)
            .Because(
                "the deformation probe stage binds a blend-shaped mesh and a " +
                "skinned mesh, and both must publish a bounded rig");

        foreach (RigComparison comparison in comparisons)
        {
            await Assert.That(comparison.PointCount)
                .IsGreaterThan(0)
                .Because($"'{comparison.Path}' published an empty rig");
            await Assert.That(comparison.WorstPointError)
                .IsLessThanOrEqualTo(SilkDeformationLimits.VerifyTolerance)
                .Because(
                    $"evaluating the rig of '{comparison.Path}' at timeCode " +
                    $"{timeCode} did not reproduce the CPU-resolved points");
            if (comparison.HasNormals)
            {
                await Assert.That(comparison.WorstNormalError)
                    .IsLessThanOrEqualTo(SilkDeformationLimits.VerifyTolerance)
                    .Because(
                        $"evaluating the rig of '{comparison.Path}' at timeCode " +
                        $"{timeCode} did not reproduce the CPU-resolved normals");
            }
        }
    }

    [Test]
    public async Task ADeformedPrimEitherPublishesARigOrNamesWhyItDidNot()
    {
        string stagePath = ResolveDeformationProbeStage();
        if (stagePath.Length == 0)
        {
            Skip.Test("The hdSilk deformation probe stage is not available.");
            return;
        }

        List<RigComparison> comparisons = CollectRigComparisonsOrSkip(stagePath, 3.0);

        // A published rig may still name Normals: that reason is about the one
        // optional section the rig omitted, not about the rig, and it is how a
        // mesh whose authored normals a point-indexed deformation cannot carry
        // says so instead of shipping bind-pose normals on a moved surface.
        // Every other reason describes a rig that was refused as a whole, so
        // carrying one beside a published rig would be a contradiction.
        const SilkDeformationUnsupportedFeatures wholeRigRefusals =
            SilkDeformationUnsupportedFeatures.JointBudget |
            SilkDeformationUnsupportedFeatures.InfluenceBudget |
            SilkDeformationUnsupportedFeatures.BlendBudget |
            SilkDeformationUnsupportedFeatures.ByteBudget |
            SilkDeformationUnsupportedFeatures.SkinningMethod |
            SilkDeformationUnsupportedFeatures.Geometry |
            SilkDeformationUnsupportedFeatures.Unverified;

        foreach (RigComparison comparison in comparisons)
        {
            await Assert.That(comparison.Unsupported & wholeRigRefusals)
                .IsEqualTo(SilkDeformationUnsupportedFeatures.None)
                .Because(
                    $"'{comparison.Path}' published a rig and a whole-rig " +
                    "refusal reason at the same time");
            if (comparison.Unsupported.HasFlag(SilkDeformationUnsupportedFeatures.Normals))
            {
                await Assert.That(comparison.HasNormals)
                    .IsFalse()
                    .Because(
                        $"'{comparison.Path}' named its normals unsupported " +
                        "while publishing them anyway");
            }
        }

        // Non-vacuity: the probe stage deliberately binds a mesh whose
        // face-varying normals no point-indexed deformation can carry, so at
        // least one published rig must be reporting that omission. Without it
        // the loop above would pass on a stage where nothing was ever omitted.
        await Assert.That(comparisons.Any(comparison =>
                comparison.Unsupported.HasFlag(
                    SilkDeformationUnsupportedFeatures.Normals)))
            .IsTrue()
            .Because(
                "the deformation probe stage binds a face-varying-normal mesh " +
                "whose omitted normals must be named on its published rig");
    }

    [Test]
    public async Task TheEquivalenceComparisonRejectsAPerturbedEvaluation()
    {
        // Non-vacuity for the gate above. A comparator that always reported no
        // error would pass every rig, including one that described another
        // surface, so the same comparison is shown to reject a single perturbed
        // component at the scale the tolerance permits.
        float[] published = [1.0f, 2.0f, 3.0f];
        float[] evaluated = [1.0f, 2.0f, 3.0f];
        await Assert.That(WorstRelativeError(evaluated, published))
            .IsLessThanOrEqualTo(SilkDeformationLimits.VerifyTolerance);

        evaluated[1] += 0.01f;
        await Assert.That(WorstRelativeError(evaluated, published))
            .IsGreaterThan(SilkDeformationLimits.VerifyTolerance);

        // A rig whose evaluation produced a different point count is a
        // different surface, not a close one.
        await Assert.That(WorstRelativeError([1.0f, 2.0f], published))
            .IsEqualTo(float.PositiveInfinity);
    }

    [Test]
    public async Task TheNormalComparisonRejectsAOneSidedDegeneracy()
    {
        // The evaluator resolves a collapsed normal to the canonical fallback,
        // so the values alone cannot tell a collapsed direction from a computed
        // +Z. These four cases pin that only a two-sided degeneracy is
        // ignorable; a comparison that skipped whenever either side was
        // degenerate would report zero error for both one-sided cases.
        float[] resolved = [0.0f, 0.0f, 1.0f];
        float[] collapsed = [0.0f, 0.0f, 0.0f];
        float[] fallback = [0.0f, 0.0f, 1.0f];

        await Assert.That(WorstNormalError(fallback, [false], resolved))
            .IsLessThanOrEqualTo(SilkDeformationLimits.VerifyTolerance);
        await Assert.That(WorstNormalError(fallback, [true], collapsed))
            .IsLessThanOrEqualTo(SilkDeformationLimits.VerifyTolerance);
        await Assert.That(WorstNormalError(fallback, [true], resolved))
            .IsEqualTo(float.PositiveInfinity);
        await Assert.That(WorstNormalError(fallback, [false], collapsed))
            .IsEqualTo(float.PositiveInfinity);

        // A published normal the CPU deformation left un-normalized still names
        // the same direction, so it must compare equal. Without normalizing the
        // published side this scores 0.5 against a tolerance of 0.0001 and
        // fails a rig that agrees perfectly.
        float[] scaled = [0.0f, 0.0f, 2.0f];
        await Assert.That(WorstNormalError(fallback, [false], scaled))
            .IsLessThanOrEqualTo(SilkDeformationLimits.VerifyTolerance);

        // Direction still decides: a normal of the same length pointing
        // elsewhere is a real disagreement.
        float[] sideways = [2.0f, 0.0f, 0.0f];
        await Assert.That(WorstNormalError(fallback, [false], sideways))
            .IsGreaterThan(SilkDeformationLimits.VerifyTolerance);
    }

    private readonly record struct RigComparison(
        string Path,
        int PointCount,
        bool HasNormals,
        float WorstPointError,
        float WorstNormalError,
        SilkDeformationUnsupportedFeatures Unsupported);

    private static List<RigComparison> CollectRigComparisonsOrSkip(
        string stagePath,
        double timeCode)
    {
        try
        {
            PrependHdSilkNativeSearchPath();
            return CollectRigComparisons(stagePath, timeCode);
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or DirectoryNotFoundException)
        {
            SkipOrFail("hdSilk deformation equivalence", exception.ToString());
            throw new InvalidOperationException("SkipOrFail returned unexpectedly.", exception);
        }
    }

    private static List<RigComparison> CollectRigComparisons(
        string stagePath,
        double timeCode)
    {
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(
            ResolvePluginPath(),
            stagePath);
        using OpenUsdSilkPage page = session.Sync(
            DeformationProbeWidth,
            DeformationProbeHeight,
            timeCode,
            new CameraState(Matrix4x4.Identity, Matrix4x4.Identity, []));

        List<RigComparison> comparisons = [];
        using SilkCommandEnumerator commands = page.GetEnumerator();
        while (commands.MoveNext())
        {
            if (commands.Current.Type != SilkCommandType.MeshUpsert)
            {
                continue;
            }
            SilkMeshUpsertCommand mesh = commands.Current.AsMeshUpsert();
            if (!mesh.HasDeformation)
            {
                continue;
            }

            SilkMeshDeformationData rig = mesh.CopyDeformation()!;
            float[] published = new float[mesh.PointCount * 3];
            for (int point = 0; point < mesh.PointCount; point++)
            {
                for (int component = 0; component < 3; component++)
                {
                    published[(point * 3) + component] =
                        mesh.GetPointComponent(point, component);
                }
            }
            float[] publishedNormals = ReadPublishedNormals(mesh);
            comparisons.Add(Compare(mesh.Path, rig, published, publishedNormals));
        }
        return comparisons;
    }

    private static float[] ReadPublishedNormals(SilkMeshUpsertCommand mesh)
    {
        for (int index = 0; index < mesh.AttributeCount; index++)
        {
            SilkMeshAttributeEntry attribute = mesh.GetAttribute(index);
            if (attribute.Semantic != SilkAttributeSemantic.Normal ||
                attribute.ComponentCount != 3 ||
                attribute.Interpolation != SilkAttributeInterpolation.Vertex ||
                attribute.ElementCount != mesh.PointCount)
            {
                continue;
            }
            float[] normals = new float[mesh.PointCount * 3];
            for (int point = 0; point < mesh.PointCount; point++)
            {
                for (int component = 0; component < 3; component++)
                {
                    normals[(point * 3) + component] =
                        attribute.GetComponent(point, component);
                }
            }
            return normals;
        }
        return [];
    }

    private static RigComparison Compare(
        string path,
        SilkMeshDeformationData rig,
        float[] publishedPoints,
        float[] publishedNormals)
    {
        float[] evaluatedPoints = new float[rig.BindPointCount * 3];
        SilkDeformationEvaluator.EvaluatePoints(rig, evaluatedPoints);
        float worstPointError = WorstRelativeError(evaluatedPoints, publishedPoints);

        float worstNormalError = 0;
        bool hasNormals = rig.HasBindNormals && publishedNormals.Length == evaluatedPoints.Length;
        if (hasNormals)
        {
            float[] evaluatedNormals = new float[rig.BindPointCount * 3];
            bool[] degenerate = new bool[rig.BindPointCount];
            _ = SilkDeformationEvaluator.TryEvaluateNormals(
                rig,
                evaluatedNormals,
                degenerate);
            worstNormalError = WorstNormalError(
                evaluatedNormals,
                degenerate,
                publishedNormals);
        }

        return new RigComparison(
            path,
            rig.BindPointCount,
            hasNormals,
            worstPointError,
            worstNormalError,
            rig.UnsupportedFeatures);
    }

    /// <summary>
    /// Compares evaluated normals against the published ones as directions,
    /// treating a degeneracy on exactly one side as total disagreement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The evaluator resolves a collapsed normal to the canonical
    /// <c>(0, 0, 1)</c> fallback, which is indistinguishable from a computed
    /// <c>+Z</c>, so the flags rather than the values decide. Skipping whenever
    /// either side is degenerate would hide exactly the case that matters: a
    /// rig that collapsed a direction the CPU deformation kept.
    /// </para>
    /// <para>
    /// Both sides are normalized before the components are compared. The
    /// evaluated side is already unit length by contract, but the published
    /// side is whatever the CPU deformation produced, and a joint whose upper
    /// three-by-three is not orthogonal -- any scale or shear -- leaves it
    /// longer or shorter than one. Comparing a unit vector against a scaled one
    /// would report a difference proportional to the scale for a rig that
    /// agrees perfectly about direction, which is the only thing a normal
    /// carries. The producer's own verification normalizes the resolved side
    /// for the same reason.
    /// </para>
    /// </remarks>
    private static float WorstNormalError(
        float[] evaluated,
        bool[] degenerate,
        float[] published)
    {
        if (evaluated.Length != published.Length ||
            degenerate.Length * 3 != evaluated.Length)
        {
            return float.PositiveInfinity;
        }
        float worst = 0;
        Span<float> direction = stackalloc float[3];
        for (int point = 0; point < degenerate.Length; point++)
        {
            int source = point * 3;
            bool publishedDegenerate = SilkDeformationEvaluator.IsDegenerateNormal(
                published[source],
                published[source + 1],
                published[source + 2]);
            if (publishedDegenerate && degenerate[point])
            {
                continue;
            }
            if (publishedDegenerate != degenerate[point])
            {
                return float.PositiveInfinity;
            }
            double lengthSquared =
                ((double)published[source] * published[source]) +
                ((double)published[source + 1] * published[source + 1]) +
                ((double)published[source + 2] * published[source + 2]);
            double inverseLength = 1.0 / Math.Sqrt(lengthSquared);
            for (int component = 0; component < 3; component++)
            {
                direction[component] =
                    (float)(published[source + component] * inverseLength);
            }
            for (int component = 0; component < 3; component++)
            {
                float error = ComponentError(
                    evaluated[source + component],
                    direction[component]);
                if (error > worst)
                {
                    worst = error;
                }
            }
        }
        return worst;
    }

    private static float WorstRelativeError(float[] evaluated, float[] published)
    {
        if (evaluated.Length != published.Length)
        {
            return float.PositiveInfinity;
        }
        float worst = 0;
        for (int index = 0; index < evaluated.Length; index++)
        {
            float error = ComponentError(evaluated[index], published[index]);
            if (error > worst)
            {
                worst = error;
            }
        }
        return worst;
    }

    private static float ComponentError(float evaluated, float published)
    {
        if (!float.IsFinite(evaluated) || !float.IsFinite(published))
        {
            return float.PositiveInfinity;
        }
        float scale = Math.Max(1.0f, Math.Abs(published));
        return Math.Abs(evaluated - published) / scale;
    }

    private static string ResolveDeformationProbeStage()
    {
        string? root = FindRepositoryRoot();
        if (root is null)
        {
            return string.Empty;
        }
        string path = Path.Combine(
            root,
            "test-assets",
            "hdsilk-deformation-probe.usda");
        return File.Exists(path) ? path : string.Empty;
    }
}
