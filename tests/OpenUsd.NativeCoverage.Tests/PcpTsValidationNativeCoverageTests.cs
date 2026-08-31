// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Geom;

namespace OpenUsd.NativeCoverage.Tests;

public sealed class PcpTsValidationNativeCoverageTests
{
    [Test]
    public async Task PcpTsValidationFacadesRunAgainstARealStage()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(PcpTsValidationFacadesRunAgainstARealStage));

        using var spline = new TsSpline();
        spline.SetData(
        [
            new TsKnot(0, 0, null, 0, 0, 0, 0, TsInterpMode.Linear,
                TsTangentAlgorithm.None, TsTangentAlgorithm.None),
            new TsKnot(5, 10, null, 0, 0, 0, 0, TsInterpMode.Linear,
                TsTangentAlgorithm.None, TsTangentAlgorithm.None),
            new TsKnot(10, 20, null, 0, 0, 0, 0, TsInterpMode.Held,
                TsTangentAlgorithm.None, TsTangentAlgorithm.None)
        ]);
        await Assert.That(spline.Evaluate(2.5)).IsEqualTo(5);
        await Assert.That(spline.Evaluate(7.5)).IsEqualTo(15);
        IReadOnlyList<TsKnot> knots = spline.GetKnots();
        await Assert.That(knots.Count).IsEqualTo(3);
        await Assert.That(knots[1].Time).IsEqualTo(5);
        await Assert.That(knots[1].Value).IsEqualTo(10);

        // GetKnots drops the curve family and both extrapolations, which a UI
        // needs in order to describe the spline it is showing.
        TsSplineData data = spline.GetData();
        await Assert.That(data.Knots).IsEquivalentTo(knots);
        await Assert.That(data.CurveType).IsEqualTo(TsCurveType.Bezier);
        await Assert.That(data.IsTimeValued).IsFalse();
        await Assert.That(data.PreExtrapolation).IsEqualTo(new TsExtrapolation(TsExtrapMode.Held, 0));
        await Assert.That(data.PostExtrapolation).IsEqualTo(new TsExtrapolation(TsExtrapMode.Held, 0));
        await Assert.That(spline.GetData()).IsEqualTo(data)
            .Because("a detached snapshot of unchanged data must compare equal across polls");

        using var sloped = new TsSpline();
        sloped.SetData(
            [
                new TsKnot(0, 0, null, 0, 0, 0, 0, TsInterpMode.Linear,
                    TsTangentAlgorithm.None, TsTangentAlgorithm.None),
                new TsKnot(4, 8, null, 0, 0, 0, 0, TsInterpMode.Linear,
                    TsTangentAlgorithm.None, TsTangentAlgorithm.None)
            ],
            TsCurveType.Hermite,
            new TsExtrapolation(TsExtrapMode.Linear, 0),
            new TsExtrapolation(TsExtrapMode.Sloped, 3));
        TsSplineData slopedData = sloped.GetData();
        await Assert.That(slopedData.CurveType).IsEqualTo(TsCurveType.Hermite);
        await Assert.That(slopedData.PreExtrapolation.Mode).IsEqualTo(TsExtrapMode.Linear);
        await Assert.That(slopedData.PostExtrapolation.Mode).IsEqualTo(TsExtrapMode.Sloped);
        await Assert.That(slopedData.PostExtrapolation.Slope).IsEqualTo(3d);
        await Assert.That(slopedData).IsNotEqualTo(data);

        // SetData(TsSplineData) is the inverse of GetData: the snapshot alone
        // must carry the curve family, both extrapolations, and the
        // time-valued flag back into a spline without losing one of them.
        using var roundTripped = new TsSpline();
        var authored = new TsSplineData(
            TsCurveType.Hermite,
            IsTimeValued: true,
            new TsExtrapolation(TsExtrapMode.Linear, 0),
            new TsExtrapolation(TsExtrapMode.Sloped, 3),
            slopedData.Knots);
        roundTripped.SetData(authored);
        TsSplineData readBack = roundTripped.GetData();
        await Assert.That(readBack.CurveType).IsEqualTo(TsCurveType.Hermite);
        await Assert.That(readBack.IsTimeValued).IsTrue()
            .Because("a time-valued spline must survive a native round trip");
        await Assert.That(readBack.PreExtrapolation).IsEqualTo(authored.PreExtrapolation);
        await Assert.That(readBack.PostExtrapolation).IsEqualTo(authored.PostExtrapolation);
        await Assert.That(readBack.Knots).IsEquivalentTo(authored.Knots);
        await Assert.That(readBack).IsEqualTo(authored);
        await Assert.That(roundTripped.Evaluate(4)).IsEqualTo(8);

        string referencedPath = Path.Combine(directory, "pcp-reference-target.usda");
        string stagePath = Path.Combine(directory, "pcp-reference-host.usda");
        using (UsdStage referenced = UsdStage.Create(referencedPath))
        {
            referenced.DefinePrim("/Target", "Xform");
            referenced.Save();
        }

        using (UsdStage stage = UsdStage.Create(stagePath))
        {
            UsdPrim prim = stage.DefinePrim("/Model", "Xform");
            prim.AddVariantSet("look");
            prim.AddVariant("look", "red");
            prim.AddVariant("look", "blue");
            prim.SetVariantSelection("look", "red");
            prim.AddReference(referencedPath, "/Target");
            PcpPrimIndex index = prim.GetPrimIndex();
            await Assert.That(index.Nodes.Count).IsGreaterThan(1);
            await Assert.That(index.Nodes[0].ArcType).IsEqualTo(PcpArcType.Root);
            int variantIndex = FindNode(index, PcpArcType.Variant);
            int referenceIndex = FindNode(index, PcpArcType.Reference);
            await Assert.That(variantIndex).IsGreaterThan(0);
            await Assert.That(referenceIndex).IsGreaterThan(variantIndex);
            await Assert.That(HasLayer(index.Nodes[variantIndex], stagePath)).IsTrue();
            await Assert.That(HasLayer(index.Nodes[referenceIndex], referencedPath)).IsTrue();

            UsdPrim wrongType = stage.DefinePrim("/WrongType", "Xform");
            await Assert.That(UsdGeomCamera.TryWrap(wrongType, out UsdGeomCamera wrongCamera)).IsFalse();
            await Assert.That(string.IsNullOrEmpty(wrongCamera.Path)).IsTrue();
        }

        IReadOnlyList<UsdValidationValidatorInfo> validators = UsdValidation.GetRegisteredValidators();
        await Assert.That(validators.Count).IsGreaterThan(0);
        string invalidPath = Path.Combine(directory, "validation-invalid-stage.usda");
        using UsdStage invalidStage = UsdStage.Create(invalidPath);
        IReadOnlyList<UsdValidationError> errors = UsdValidation.Validate(invalidStage);
        await Assert.That(errors.Count).IsGreaterThan(0);
        await Assert.That(errors.Any(
                error => error.Severity == UsdValidationSeverity.Error &&
                    error.Message.Contains("defaultPrim", StringComparison.OrdinalIgnoreCase)))
            .IsTrue();
    }

    [Test]
    public async Task OnlyDualValuedKnotsReportAPreValueAndARoundTripPreservesBoth()
    {
        // TsKnot.GetPreValue returns the ordinary value for a knot that is not
        // dual valued, so flagging a pre-value on its success alone made every
        // knot look dual valued. SetData(GetData()) then authored a dual value
        // onto knots that never had one - a silent semantic change on any
        // caller that reads a spline and writes it back.
        NativeCoverageRuntime.CreateTempDirectory(
            nameof(OnlyDualValuedKnotsReportAPreValueAndARoundTripPreservesBoth));

        using var spline = new TsSpline();
        spline.SetData(
            [
                Knot(0, 1),
                Knot(4, 9, preValue: 7),
                Knot(8, 12)
            ],
            TsCurveType.Bezier,
            new TsExtrapolation(TsExtrapMode.Held, 0),
            new TsExtrapolation(TsExtrapMode.Held, 0));

        TsSplineData data = spline.GetData();
        await Assert.That(data.Knots.Count).IsEqualTo(3);
        await Assert.That(data.Knots[0].PreValue).IsNull()
            .Because("an ordinary knot has no authored pre-value");
        await Assert.That(data.Knots[2].PreValue).IsNull();
        await Assert.That(data.Knots[1].PreValue).IsNotNull()
            .Because("the authored dual value must survive readback");
        await Assert.That(data.Knots[1].PreValue!.Value).IsEqualTo(7d);
        await Assert.That(data.Knots[1].Value).IsEqualTo(9d);

        // The value at the dual-valued knot is approached from the left as the
        // pre-value, which is exactly the semantic a fabricated pre-value on
        // an ordinary knot would have introduced everywhere else.
        await Assert.That(spline.Evaluate(4)).IsEqualTo(9d);
        double? justBefore = spline.Evaluate(4 - 1e-9);
        await Assert.That(justBefore.HasValue).IsTrue();
        await Assert.That(justBefore!.Value).IsGreaterThan(6.9d);
        await Assert.That(justBefore.Value).IsLessThan(7.1d);

        // SetData(GetData()) must be an identity, not an authoring change.
        using var copy = new TsSpline();
        copy.SetData(data);
        TsSplineData copied = copy.GetData();
        await Assert.That(copied).IsEqualTo(data)
            .Because("a read-then-write round trip must not mutate spline semantics");
        await Assert.That(copied.Knots[0].PreValue).IsNull();
        await Assert.That(copied.Knots[2].PreValue).IsNull();
        await Assert.That(copied.Knots[1].PreValue!.Value).IsEqualTo(7d);

        // Two round trips are still the same spline, so nothing accumulates.
        using var second = new TsSpline();
        second.SetData(copied);
        await Assert.That(second.GetData()).IsEqualTo(data);
        for (double time = -1; time <= 9; time += 0.5)
        {
            await Assert.That(second.Evaluate(time)).IsEqualTo(spline.Evaluate(time))
                .Because($"the round-tripped spline must evaluate identically at t={time}");
        }
    }

    private static TsKnot Knot(double time, double value, double? preValue = null) =>
        new(
            time,
            value,
            preValue,
            PreTangentWidth: 0,
            PreTangentSlope: 0,
            PostTangentWidth: 0,
            PostTangentSlope: 0,
            TsInterpMode.Linear,
            TsTangentAlgorithm.None,
            TsTangentAlgorithm.None);

    [Test]
    public async Task FloatAndHalfSplinesAreReadAndEvaluatedAsDoubles()
    {
        // Ts stores a spline as double, float, or GfHalf, and every typed
        // accessor refuses a mismatched type instead of converting. Reading
        // only double therefore did not fail: a float or half spline came back
        // with every value, pre-value, and tangent slope silently zeroed and
        // every evaluation reported as a value block. Only a layer can author
        // those types - the managed surface creates double splines - so this
        // reads them from usda text.
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(FloatAndHalfSplinesAreReadAndEvaluatedAsDoubles));
        string stagePath = Path.Combine(directory, "ts-value-types.usda");
        await File.WriteAllTextAsync(
            stagePath,
            """
            #usda 1.0

            def Xform "Splines"
            {
                double doubleSpline.spline = {
                    pre: linear,
                    post: linear,
                    0: 1; post linear,
                    4: 9; post linear,
                }
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

        using UsdStage stage = UsdStage.Open(stagePath);
        UsdPrim prim = stage.GetPrim("/Splines");
        await Assert.That(stage.HasPrim("/Splines")).IsTrue();

        foreach (string name in new[] { "doubleSpline", "floatSpline", "halfSpline" })
        {
            UsdAttribute attribute = prim.GetAttribute(name);
            await Assert.That(attribute.HasSpline()).IsTrue()
                .Because($"{name} carries an authored spline");
            using TsSpline authored = attribute.GetSpline();
            TsSplineData data = authored.GetData();

            await Assert.That(data.Knots.Count).IsEqualTo(2);
            await Assert.That(data.Knots[0].Time).IsEqualTo(0d);
            await Assert.That(data.Knots[0].Value).IsEqualTo(1d)
                .Because($"{name} must not read back as a zeroed knot");
            await Assert.That(data.Knots[1].Time).IsEqualTo(4d);
            await Assert.That(data.Knots[1].Value).IsEqualTo(9d);
            await Assert.That(data.PostExtrapolation.Mode).IsEqualTo(TsExtrapMode.Linear);

            double? middle = authored.Evaluate(2);
            await Assert.That(middle.HasValue).IsTrue()
                .Because($"{name} must evaluate rather than report a value block");
            await Assert.That(middle!.Value).IsEqualTo(5d);
            double? extrapolated = authored.Evaluate(6);
            await Assert.That(extrapolated.HasValue).IsTrue();
            await Assert.That(extrapolated!.Value).IsEqualTo(13d)
                .Because("linear post-extrapolation continues the last segment");
        }
    }

    private static int FindNode(PcpPrimIndex index, PcpArcType arcType)
    {
        for (int i = 0; i < index.Nodes.Count; i++)
        {
            if (index.Nodes[i].ArcType == arcType)
            {
                return i;
            }
        }
        return -1;
    }

    private static bool HasLayer(PcpPrimIndexNode node, string path)
    {
        string expected = Path.GetFileName(path);
        return node.LayerIdentifiers.Any(
            layer => string.Equals(Path.GetFileName(layer), expected, StringComparison.OrdinalIgnoreCase));
    }
}
