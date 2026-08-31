// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Covers the Viewer's Ts spline projection: the sampling rule, the knot cap,
/// snapshot value equality, the Value-tab text, and the stage-backed builder.
/// </summary>
public sealed class ViewerSplineModelTests
{
    [Test]
    public async Task SampleTimesSpanTheKnotsAndBothExtrapolationRegions()
    {
        TsSplineData data = CreateData(
            Knot(0, 0),
            Knot(10, 10));

        double[] times = ViewerSplineSnapshot.GetSampleTimes(data);

        await Assert.That(times.Length).IsEqualTo(ViewerSplineSnapshot.SampleCount);
        await Assert.That(times[0]).IsEqualTo(-1d);
        await Assert.That(times[^1]).IsEqualTo(11d);
        await Assert.That(times[0]).IsLessThan(0d)
            .Because("the preview must reach into the pre-extrapolation region");
        await Assert.That(times[^1]).IsGreaterThan(10d)
            .Because("the preview must reach into the post-extrapolation region");
        for (int index = 1; index < times.Length; index++)
        {
            await Assert.That(times[index]).IsGreaterThan(times[index - 1]);
        }
    }

    [Test]
    public async Task SingleKnotSplinesStillSampleAFixedMarginOnBothSides()
    {
        TsSplineData data = CreateData(Knot(4, 2));

        double[] times = ViewerSplineSnapshot.GetSampleTimes(data);

        await Assert.That(times.Length).IsEqualTo(ViewerSplineSnapshot.SampleCount);
        await Assert.That(times[0]).IsEqualTo(3d);
        await Assert.That(times[^1]).IsEqualTo(5d);
    }

    [Test]
    public async Task EmptyAndNonFiniteSplinesProduceNoSamples()
    {
        await Assert.That(ViewerSplineSnapshot.GetSampleTimes(CreateData())).IsEmpty();
        await Assert.That(
                ViewerSplineSnapshot.GetSampleTimes(
                    CreateData(Knot(double.NaN, 0), Knot(1, 1))))
            .IsEmpty();
        await Assert.That(
                ViewerSplineSnapshot.GetSampleTimes(
                    CreateData(Knot(0, 0), Knot(double.PositiveInfinity, 1))))
            .IsEmpty();
    }

    [Test]
    public async Task AKnotSpanThatOverflowsProducesNoSampleRatherThanANonFiniteTime()
    {
        // Both knot times are finite and authorable, but their span, the
        // margin, and the sampled extent all overflow. The native evaluator
        // rejects a non-finite time, so producing one here would turn a
        // legal spline into a failed inspector snapshot.
        double[] widest = ViewerSplineSnapshot.GetSampleTimes(
            CreateData(Knot(double.MinValue, 0), Knot(double.MaxValue, 1)));

        await Assert.That(widest).IsEmpty();

        // A span that is finite but whose ten percent margin still overflows
        // the double range at the ends must be rejected on the same rule.
        double[] margined = ViewerSplineSnapshot.GetSampleTimes(
            CreateData(Knot(0, 0), Knot(double.MaxValue, 1)));

        await Assert.That(margined).IsEmpty();

        foreach (double[] times in new[] { widest, margined })
        {
            foreach (double time in times)
            {
                await Assert.That(double.IsFinite(time)).IsTrue();
            }
        }
    }

    [Test]
    public async Task EverySampledTimeOfAWideButRepresentableSplineStaysFinite()
    {
        double[] times = ViewerSplineSnapshot.GetSampleTimes(
            CreateData(Knot(-1e300, 0), Knot(1e300, 1)));

        await Assert.That(times.Length).IsEqualTo(ViewerSplineSnapshot.SampleCount);
        foreach (double time in times)
        {
            await Assert.That(double.IsFinite(time)).IsTrue();
        }
    }

    [Test]
    public async Task DenseSplinesAreTruncatedButStillReportTheAuthoredKnotCount()
    {
        var knots = new TsKnot[ViewerSplineSnapshot.MaxKnots + 7];
        for (int index = 0; index < knots.Length; index++)
        {
            knots[index] = Knot(index, index);
        }

        ViewerSplineSnapshot snapshot = ViewerSplineSnapshot.Create(CreateData(knots), []);

        await Assert.That(snapshot.Knots.Length).IsEqualTo(ViewerSplineSnapshot.MaxKnots);
        await Assert.That(snapshot.KnotCount).IsEqualTo(knots.Length);
        await Assert.That(snapshot.IsTruncated).IsTrue();
        await Assert.That(ViewerSplineFormatter.FormatSummary(snapshot))
            .Contains($"{knots.Length} knot(s) (showing first {ViewerSplineSnapshot.MaxKnots})");
    }

    [Test]
    public async Task SnapshotsOfUnchangedSplinesCompareEqual()
    {
        TsSplineData data = CreateData(Knot(0, 0), Knot(1, 1));
        ViewerSplineSampleSnapshot[] samples =
        [
            new ViewerSplineSampleSnapshot(0, 0),
            new ViewerSplineSampleSnapshot(1, 1)
        ];

        ViewerSplineSnapshot left = ViewerSplineSnapshot.Create(data, samples);
        ViewerSplineSnapshot right = ViewerSplineSnapshot.Create(
            CreateData(Knot(0, 0), Knot(1, 1)),
            [
                new ViewerSplineSampleSnapshot(0, 0),
                new ViewerSplineSampleSnapshot(1, 1)
            ]);

        await Assert.That(left).IsEqualTo(right);
        await Assert.That(left.GetHashCode()).IsEqualTo(right.GetHashCode());
        await Assert.That(left).IsNotEqualTo(
            ViewerSplineSnapshot.Create(
                CreateData(Knot(0, 0), Knot(1, 2)),
                samples));
        await Assert.That(left).IsNotEqualTo(
            ViewerSplineSnapshot.Create(
                data,
                [
                    new ViewerSplineSampleSnapshot(0, 0),
                    new ViewerSplineSampleSnapshot(1, 9)
                ]));
    }

    [Test]
    public async Task AttributeSnapshotEqualityIncludesTheSpline()
    {
        ViewerSplineSnapshot spline = ViewerSplineSnapshot.Create(
            CreateData(Knot(0, 0), Knot(1, 1)),
            []);
        var left = CreateAttribute(spline);
        var right = CreateAttribute(
            ViewerSplineSnapshot.Create(CreateData(Knot(0, 0), Knot(1, 1)), []));

        await Assert.That(left).IsEqualTo(right);
        await Assert.That(left).IsNotEqualTo(CreateAttribute(null));
        await Assert.That(left).IsNotEqualTo(
            CreateAttribute(
                ViewerSplineSnapshot.Create(CreateData(Knot(0, 0), Knot(1, 4)), [])));
    }

    [Test]
    public async Task SummaryReportsCurveTypeAndBothExtrapolations()
    {
        ViewerSplineSnapshot snapshot = ViewerSplineSnapshot.Create(
            new TsSplineData(
                TsCurveType.Hermite,
                IsTimeValued: true,
                new TsExtrapolation(TsExtrapMode.Held, 0),
                new TsExtrapolation(TsExtrapMode.Sloped, 2.5),
                [Knot(0, 0)]),
            []);

        string summary = ViewerSplineFormatter.FormatSummary(snapshot);

        await Assert.That(summary).Contains("1 knot(s)");
        await Assert.That(summary).Contains("curve=Hermite");
        await Assert.That(summary).Contains("pre=Held");
        await Assert.That(summary).Contains("post=Sloped(2.5)");
        await Assert.That(summary).Contains("time-valued");
        await Assert.That(summary).DoesNotContain("showing first");
    }

    [Test]
    public async Task KnotRowsCarryValuesTangentsAndInterpolation()
    {
        var knot = new TsKnot(
            2,
            3,
            PreValue: 1.5,
            PreTangentWidth: 0.25,
            PreTangentSlope: -1,
            PostTangentWidth: 0.5,
            PostTangentSlope: 4,
            TsInterpMode.Curve,
            TsTangentAlgorithm.AutoEase,
            TsTangentAlgorithm.None);

        string row = ViewerSplineFormatter.FormatKnot(knot, 0);

        await Assert.That(row).StartsWith("1. t=2");
        await Assert.That(row).Contains("value=3");
        await Assert.That(row).Contains("preValue=1.5");
        await Assert.That(row).Contains("next=Curve");
        await Assert.That(row).Contains("preTangent=width=0.25,slope=-1,algorithm=AutoEase");
        await Assert.That(row).Contains("postTangent=width=0.5,slope=4,algorithm=None");
    }

    [Test]
    public async Task SampleRowsNameValueBlocksRatherThanShowingZero()
    {
        ViewerSplineSnapshot snapshot = ViewerSplineSnapshot.Create(
            CreateData(Knot(0, 0)),
            [
                new ViewerSplineSampleSnapshot(-1, null),
                new ViewerSplineSampleSnapshot(0, 0.5)
            ]);

        await Assert.That(ViewerSplineFormatter.FormatSamples(snapshot))
            .IsEqualTo("-1=<value block>, 0=0.5");
        await Assert.That(
                ViewerSplineFormatter.FormatSamples(
                    ViewerSplineSnapshot.Create(CreateData(Knot(0, 0)), [])))
            .IsEqualTo("<none>");
    }

    [Test]
    public async Task OneBlockCarriesTheWholeSplineAndSpendsOnlyTheBudgetItIsGiven()
    {
        var knots = new TsKnot[4];
        for (int index = 0; index < knots.Length; index++)
        {
            knots[index] = Knot(index, index);
        }
        ViewerSplineSnapshot snapshot = ViewerSplineSnapshot.Create(
            CreateData(knots),
            [new ViewerSplineSampleSnapshot(0, 0)]);

        ViewerSplineBlock full = ViewerSplineFormatter.FormatBlock(snapshot, 32);

        await Assert.That(full.KnotsShown).IsEqualTo(4);
        await Assert.That(full.Text).StartsWith("Spline: 4 knot(s)");
        await Assert.That(full.Text).Contains("1. t=0");
        await Assert.That(full.Text).Contains("4. t=3");
        await Assert.That(full.Text).Contains("Evaluated: 0=0");
        await Assert.That(full.Text).DoesNotContain("not shown");

        ViewerSplineBlock partial = ViewerSplineFormatter.FormatBlock(snapshot, 2);

        await Assert.That(partial.KnotsShown).IsEqualTo(2);
        await Assert.That(partial.Text).Contains("2. t=1");
        await Assert.That(partial.Text).DoesNotContain("3. t=2");
        await Assert.That(partial.Text).Contains("... 2 more knot(s) not shown");
        await Assert.That(partial.Text).Contains("Evaluated: 0=0");

        ViewerSplineBlock exhausted = ViewerSplineFormatter.FormatBlock(snapshot, 0);

        await Assert.That(exhausted.KnotsShown).IsEqualTo(0);
        await Assert.That(exhausted.Text).Contains("... 4 more knot(s) not shown");
        await Assert.That(exhausted.Text).DoesNotContain("1. t=0");
    }

    [Test]
    public async Task ASharedBudgetBoundsEveryKnotLineOneInspectorCanRender()
    {
        // The per-attribute cap alone leaves a prim with many splined
        // attributes unbounded, which is the case this walks: 40 attributes
        // that each want the full 32-knot cap, against the whole-inspector
        // budget the Value tab actually spends.
        const int budget = 64;
        var knots = new TsKnot[ViewerSplineSnapshot.MaxKnots];
        for (int index = 0; index < knots.Length; index++)
        {
            knots[index] = Knot(index, index);
        }
        ViewerSplineSnapshot snapshot = ViewerSplineSnapshot.Create(CreateData(knots), []);

        int remaining = budget;
        int rendered = 0;
        int blocks = 0;
        for (int attribute = 0; attribute < 40; attribute++)
        {
            ViewerSplineBlock block = ViewerSplineFormatter.FormatBlock(snapshot, remaining);
            remaining -= block.KnotsShown;
            rendered += block.KnotsShown;
            blocks++;
            await Assert.That(remaining).IsGreaterThanOrEqualTo(0);
        }

        await Assert.That(rendered).IsEqualTo(budget);
        await Assert.That(blocks).IsEqualTo(40)
            .Because("every attribute still gets exactly one control and one summary");
    }

    [Test]
    public async Task AnUnreadableSplineIsProjectedAndNamedRatherThanLost()
    {
        ViewerSplineSnapshot unreadable =
            ViewerSplineSnapshot.CreateUnreadable("Unsupported spline value type.");

        await Assert.That(unreadable.Error).IsEqualTo("Unsupported spline value type.");
        await Assert.That(unreadable.IsNotRead).IsFalse();
        await Assert.That(unreadable.KnotCount).IsEqualTo(0);
        await Assert.That(unreadable.Knots).IsEmpty();
        await Assert.That(unreadable.Samples).IsEmpty();
        await Assert.That(ViewerSplineFormatter.FormatSummary(unreadable))
            .IsEqualTo("unreadable (Unsupported spline value type.)");

        ViewerSplineBlock block = ViewerSplineFormatter.FormatBlock(unreadable, 32);

        await Assert.That(block.KnotsShown).IsEqualTo(0);
        await Assert.That(block.Text)
            .IsEqualTo("Spline: unreadable (Unsupported spline value type.)");
        await Assert.That(unreadable).IsEqualTo(
            ViewerSplineSnapshot.CreateUnreadable("Unsupported spline value type."));
        await Assert.That(unreadable)
            .IsNotEqualTo(ViewerSplineSnapshot.CreateUnreadable("Another reason."));
        await Assert.That(unreadable).IsNotEqualTo(
            ViewerSplineSnapshot.Create(CreateData(), []));
    }

    [Test]
    public async Task ABlankNativeMessageStillProducesAStableReason()
    {
        // The message comes from a native error buffer, which is allowed to be
        // empty, and CreateUnreadable runs inside a catch block: throwing on a
        // blank message would replace a contained failure with the uncontained
        // one the containment exists to prevent.
        foreach (string? message in new[] { null, string.Empty, "   ", "\t\r\n" })
        {
            ViewerSplineSnapshot snapshot = ViewerSplineSnapshot.CreateUnreadable(message);

            await Assert.That(snapshot.Error)
                .IsEqualTo(ViewerSplineSnapshot.UnknownErrorMessage);
            await Assert.That(ViewerSplineFormatter.FormatSummary(snapshot))
                .IsEqualTo($"unreadable ({ViewerSplineSnapshot.UnknownErrorMessage})");
        }

        // A real reason is kept, trimmed, and never replaced by the fallback.
        await Assert.That(ViewerSplineSnapshot.CreateUnreadable("  boom  ").Error)
            .IsEqualTo("boom");
    }

    [Test]
    public async Task ASkippedSplineIsNamedAsUnreadRatherThanUnreadable()
    {
        ViewerSplineSnapshot notRead = ViewerSplineSnapshot.CreateNotRead("budget spent");

        await Assert.That(notRead.IsNotRead).IsTrue();
        await Assert.That(ViewerSplineFormatter.FormatSummary(notRead))
            .IsEqualTo("not read (budget spent)")
            .Because("a spline nobody tried to read must not be reported as unreadable");
        await Assert.That(ViewerSplineFormatter.FormatBlock(notRead, 32).Text)
            .IsEqualTo("Spline: not read (budget spent)");
        await Assert.That(notRead)
            .IsNotEqualTo(ViewerSplineSnapshot.CreateUnreadable("budget spent"))
            .Because("skipped and failed are different states with the same words");
        await Assert.That(ViewerSplineSnapshot.CreateNotRead("   ").Error)
            .IsEqualTo(ViewerSplineSnapshot.UnknownErrorMessage);
    }

    [Test]
    public async Task TheInspectorProjectsFloatAndHalfSplinesWithTheirAuthoredValues()
    {
        // A float or half spline used to project as knots of value zero,
        // because the native readback asked for a double and Ts refuses a
        // mismatched type instead of converting. Only a layer can author
        // those value types, so this reads them from usda text.
        string directory = CreateTemporaryDirectory(
            nameof(TheInspectorProjectsFloatAndHalfSplinesWithTheirAuthoredValues));
        string path = Path.Combine(directory, "viewer-spline-types.usda");
        try
        {
            await File.WriteAllTextAsync(
                path,
                """
                #usda 1.0

                def Xform "Splines"
                {
                    float floatSpline.spline = {
                        pre: linear,
                        post: linear,
                        0: 1; post linear,
                        4: 9; post linear,
                    }
                    half halfSpline.spline = {
                        pre: linear,
                        post: linear,
                        0: 1; post linear,
                        4: 9; post linear,
                    }
                }

                """);

            using UsdStage stage = OpenStageOrSkip(path);
            ViewerPrimInspectorSnapshot inspector =
                ViewerStageSnapshotBuilder.BuildInspector(stage, "/Splines");

            foreach (string name in new[] { "floatSpline", "halfSpline" })
            {
                ViewerAttributeSnapshot attribute = inspector.Attributes.First(
                    candidate => candidate.Name == name);
                ViewerSplineSnapshot? projected = attribute.Spline;

                await Assert.That(projected).IsNotNull().Because($"{name} has an authored spline");
                await Assert.That(projected!.Error).IsNull();
                await Assert.That(projected.KnotCount).IsEqualTo(2);
                await Assert.That(projected.Knots[0].Value).IsEqualTo(1d)
                    .Because($"{name} must not project as zeroed knots");
                await Assert.That(projected.Knots[1].Value).IsEqualTo(9d);
                await Assert.That(projected.Samples.Length)
                    .IsEqualTo(ViewerSplineSnapshot.SampleCount);
                await Assert.That(projected.Samples.All(sample => sample.Value.HasValue)).IsTrue()
                    .Because($"{name} must evaluate rather than report value blocks");
                await Assert.That(ViewerSplineFormatter.FormatBlock(projected, 32).Text)
                    .Contains("value=1");
            }
        }
        finally
        {
            DeleteTemporaryDirectory(path);
        }
    }

    [Test]
    public async Task OnlyABoundedNumberOfSplinesIsReadPerInspector()
    {
        // Reading a spline costs two native calls plus one evaluation per
        // preview sample, and nothing bounds how many splined attributes a
        // prim may carry, so the scheduler callback would grow with the prim.
        // This authors more splines than the builder is allowed to read and
        // requires the extra ones to be named as unread rather than dropped,
        // fabricated, or silently evaluated anyway.
        int authored = ViewerStageSnapshotBuilder.MaxReadSplinesPerInspector + 5;
        var layer = new StringBuilder("#usda 1.0\n\ndef Xform \"Splines\"\n{\n");
        for (int index = 0; index < authored; index++)
        {
            layer.Append(CultureInfo.InvariantCulture, $"    double s{index:D2}.spline = {{\n");
            layer.Append("        post: linear,\n        0: 1; post linear,\n");
            layer.Append("        4: 9; post linear,\n    }\n");
        }
        layer.Append("}\n");

        string directory = CreateTemporaryDirectory(
            nameof(OnlyABoundedNumberOfSplinesIsReadPerInspector));
        string path = Path.Combine(directory, "viewer-spline-budget.usda");
        try
        {
            await File.WriteAllTextAsync(path, layer.ToString());
            using UsdStage stage = OpenStageOrSkip(path);

            ViewerPrimInspectorSnapshot inspector =
                ViewerStageSnapshotBuilder.BuildInspector(stage, "/Splines");
            ViewerSplineSnapshot[] splines = [.. inspector.Attributes
                .Where(attribute => attribute.Spline is not null)
                .Select(attribute => attribute.Spline!)];

            await Assert.That(splines.Length).IsEqualTo(authored)
                .Because("every splined attribute is still listed");
            ViewerSplineSnapshot[] read = [.. splines.Where(spline => !spline.IsNotRead)];
            ViewerSplineSnapshot[] skipped = [.. splines.Where(spline => spline.IsNotRead)];

            await Assert.That(read.Length)
                .IsEqualTo(ViewerStageSnapshotBuilder.MaxReadSplinesPerInspector);
            await Assert.That(skipped.Length).IsEqualTo(5);
            await Assert.That(read.All(spline => spline.KnotCount == 2)).IsTrue();
            await Assert.That(read.All(
                    spline => spline.Samples.Length == ViewerSplineSnapshot.SampleCount))
                .IsTrue();
            await Assert.That(skipped.All(spline => spline.Samples.Length == 0)).IsTrue()
                .Because("a spline that was never read cannot carry evaluated samples");
            await Assert.That(skipped.All(spline => spline.KnotCount == 0)).IsTrue();
            await Assert.That(ViewerSplineFormatter.FormatSummary(skipped[0]))
                .Contains(
                    $"not read (this prim's budget of " +
                    $"{ViewerStageSnapshotBuilder.MaxReadSplinesPerInspector} read spline(s)");

            // The budget is per inspector, not per process: inspecting the
            // same prim again must read the same number, not fewer, and must
            // skip the same attributes. (The inspector record itself has no
            // value equality, so this compares the attribute projections,
            // which do.)
            ViewerPrimInspectorSnapshot again =
                ViewerStageSnapshotBuilder.BuildInspector(stage, "/Splines");
            await Assert.That(again.Attributes.Count(
                    attribute => attribute.Spline is { IsNotRead: false }))
                .IsEqualTo(ViewerStageSnapshotBuilder.MaxReadSplinesPerInspector);
            await Assert.That(again.Attributes.Length).IsEqualTo(inspector.Attributes.Length);
            for (int index = 0; index < again.Attributes.Length; index++)
            {
                await Assert.That(again.Attributes[index]).IsEqualTo(inspector.Attributes[index])
                    .Because("the truncation must be deterministic across polls");
            }
        }
        finally
        {
            DeleteTemporaryDirectory(path);
        }
    }

    [Test]
    public async Task InspectorProjectsAnAuthoredSplineFromARealStage()
    {
        string path = Path.Combine(
            CreateTemporaryDirectory(nameof(InspectorProjectsAnAuthoredSplineFromARealStage)),
            "viewer-spline.usda");
        try
        {
            using UsdStage stage = CreateStageOrSkip(path);
            UsdPrim prim = stage.DefinePrim("/Spline", "Xform");
            prim.SetDouble("splined", 0);
            prim.SetDouble("plain", 1.5);
            UsdAttribute attribute = prim.GetAttribute("splined");
            using (var spline = new TsSpline())
            {
                spline.SetData(
                    [
                        Knot(0, 0, TsInterpMode.Linear),
                        Knot(10, 20, TsInterpMode.Linear)
                    ],
                    TsCurveType.Bezier,
                    new TsExtrapolation(TsExtrapMode.Linear, 0),
                    new TsExtrapolation(TsExtrapMode.Held, 0));
                attribute.SetSpline(spline);
            }

            ViewerPrimInspectorSnapshot inspector =
                ViewerStageSnapshotBuilder.BuildInspector(stage, "/Spline");
            ViewerAttributeSnapshot? splined = inspector.Attributes
                .FirstOrDefault(candidate => candidate.Name == "splined");

            await Assert.That(splined).IsNotNull();
            ViewerSplineSnapshot? projected = splined!.Spline;
            await Assert.That(projected).IsNotNull();
            await Assert.That(projected!.KnotCount).IsEqualTo(2);
            await Assert.That(projected.Knots[0].Time).IsEqualTo(0d);
            await Assert.That(projected.Knots[1].Value).IsEqualTo(20d);
            await Assert.That(projected.PreExtrapolation.Mode).IsEqualTo(TsExtrapMode.Linear);
            await Assert.That(projected.PostExtrapolation.Mode).IsEqualTo(TsExtrapMode.Held);
            await Assert.That(projected.Samples.Length)
                .IsEqualTo(ViewerSplineSnapshot.SampleCount);
            await Assert.That(projected.Samples[^1].Value).IsEqualTo(20d)
                .Because("held post-extrapolation keeps the last authored value");
            double? firstSample = projected.Samples[0].Value;
            await Assert.That(firstSample.HasValue).IsTrue();
            await Assert.That(firstSample!.Value).IsLessThan(0d)
                .Because("linear pre-extrapolation continues the first segment downward");

            // Two polls of an unchanged spline compare equal, which is what
            // makes the projection diffable by a caller that wants to skip
            // work. The Value tab itself rebuilds unconditionally today, so
            // this asserts the equality only, not a skipped rebuild.
            ViewerPrimInspectorSnapshot again =
                ViewerStageSnapshotBuilder.BuildInspector(stage, "/Spline");
            await Assert.That(again.Attributes.First(
                    candidate => candidate.Name == "splined"))
                .IsEqualTo(splined);

            ViewerAttributeSnapshot plain = inspector.Attributes.First(
                candidate => candidate.Name == "plain");
            await Assert.That(plain.Spline).IsNull();
        }
        finally
        {
            DeleteTemporaryDirectory(path);
        }
    }

    [Test]
    public async Task TheValueTabRendersTheProjectedSplineAndTheBuilderOwnsEveryEvaluation()
    {
        string root = FindRepositoryRoot();
        string window = await File.ReadAllTextAsync(
            Path.Combine(root, "src", "OpenUsd.Viewer", "MainWindow.axaml.cs"));
        string models = await File.ReadAllTextAsync(
            Path.Combine(root, "src", "OpenUsd.Viewer", "ViewerDocumentModels.cs"));

        // The Value tab must consume the projection, never a live TsSpline: a
        // native handle reaching the UI thread would outlive stage access.
        await Assert.That(window).Contains("if (attribute.Spline is ViewerSplineSnapshot spline)");
        await Assert.That(window).Contains(
            "ViewerSplineBlock block = ViewerSplineFormatter.FormatBlock(spline, _splineKnotBudget);");
        await Assert.That(window).Contains("_splineKnotBudget -= block.KnotsShown;");
        await Assert.That(window).Contains("_splineKnotBudget = SplineKnotRowBudget;")
            .Because("the whole-inspector knot budget must be reset for each rebuild");
        await Assert.That(window).DoesNotContain("GetSpline()");
        await Assert.That(window).DoesNotContain("TsSpline ");

        // Every evaluation happens in the snapshot builder, which runs inside
        // the scheduler callback, and the native spline is disposed there.
        await Assert.That(models).Contains("using TsSpline spline = attribute.GetSpline();");
        await Assert.That(models).Contains("spline.Evaluate(times[index])");
        await Assert.That(models).Contains("if (!attribute.HasSpline())");
        await Assert.That(models).Contains("catch (OpenUsdNativeException exception)")
            .Because("one unreadable spline must not fail the whole inspector snapshot");
        await Assert.That(models).Contains("ViewerSplineSnapshot.CreateUnreadable(exception.Message)");
        await Assert.That(models).Contains("int splineBudget = MaxReadSplinesPerInspector;")
            .Because("native spline work must be bounded per inspector snapshot");
        await Assert.That(models).Contains("ViewerSplineSnapshot.CreateNotRead(");
        await Assert.That(models.IndexOf("BuildSpline(attribute, ref splineBudget)", StringComparison.Ordinal))
            .IsGreaterThan(0)
            .Because("the attribute projection must carry the spline snapshot");
    }

    private static string FindRepositoryRoot()
    {
        string currentDirectory = Environment.CurrentDirectory;
        if (File.Exists(Path.Combine(currentDirectory, "OpenUsd.slnx")))
        {
            return currentDirectory;
        }

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the OpenUSD repository root.");
    }

    private static ViewerAttributeSnapshot CreateAttribute(ViewerSplineSnapshot? spline) =>
        new(
            "splined",
            "double",
            HasAuthoredValue: true,
            IsBlocked: false,
            TimeSampleCount: 0,
            TimeSamples: "<none>",
            Value: "0",
            Spline: spline);

    private static TsSplineData CreateData(params TsKnot[] knots) =>
        new(
            TsCurveType.Bezier,
            IsTimeValued: false,
            new TsExtrapolation(TsExtrapMode.Held, 0),
            new TsExtrapolation(TsExtrapMode.Held, 0),
            knots);

    private static TsKnot Knot(
        double time,
        double value,
        TsInterpMode interpolation = TsInterpMode.Linear) =>
        new(
            time,
            value,
            PreValue: null,
            PreTangentWidth: 0,
            PreTangentSlope: 0,
            PostTangentWidth: 0,
            PostTangentSlope: 0,
            interpolation,
            TsTangentAlgorithm.None,
            TsTangentAlgorithm.None);

    private static UsdStage CreateStageOrSkip(string path)
    {
        try
        {
            return UsdStage.Create(path);
        }
        catch (DllNotFoundException exception)
        {
            Skip.Test($"openusd_dotnet native runtime is unavailable: {exception.Message}");
            throw;
        }
    }

    private static UsdStage OpenStageOrSkip(string path)
    {
        try
        {
            return UsdStage.Open(path);
        }
        catch (DllNotFoundException exception)
        {
            Skip.Test($"openusd_dotnet native runtime is unavailable: {exception.Message}");
            throw;
        }
    }

    private static string CreateTemporaryDirectory(string name)
    {
        string directory = Path.Combine(
            Path.GetDirectoryName(typeof(ViewerSplineModelTests).Assembly.Location)!,
            "viewer-spline-tests",
            $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string stagePath)
    {
        string? directory = Path.GetDirectoryName(stagePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
