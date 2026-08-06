// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Geom;
using OpenUsd.Interop;
using OpenUsd.Viewer;

namespace OpenUsd.NativeCoverage.Tests;

public sealed class InspectionV2NativeCoverageTests
{
    [Test]
    public async Task OrientedBoundsPreserveTheWorldBoundMatrix()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(OrientedBoundsPreserveTheWorldBoundMatrix));
        using UsdStage stage = UsdStage.Create(Path.Combine(directory, "obb.usda"));

        UsdGeomCube cube = stage.DefineCube("/RotatedCube");
        cube.Size = 2;
        double c = Math.Sqrt(0.5);
        cube.Xformable.SetLocalTransform(new UsdMatrix4d(
            c, c, 0, 0,
            -c, c, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1));

        UsdBounds3d aligned = cube.Prim.GetWorldBounds();
        UsdOrientedBounds3d oriented = cube.Prim.GetWorldOrientedBounds();

        await Assert.That(aligned.IsEmpty).IsFalse();
        await Assert.That(oriented.IsEmpty).IsFalse();
        await Assert.That(oriented.Matrix == UsdMatrix4d.Identity).IsFalse();
        await Assert.That(Volume(oriented.Range) < Volume(aligned)).IsTrue();
    }

    [Test]
    public async Task PrimClassificationDistinguishesDefOverAndClass()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(PrimClassificationDistinguishesDefOverAndClass));
        using UsdStage stage = UsdStage.Create(Path.Combine(directory, "specifier.usda"));

        UsdPrim defined = stage.DefinePrim("/Defined", "Xform");
        UsdPrim undefined = stage.OverridePrim("/Undefined");
        UsdPrim abstractClass = stage.CreateClassPrim("/AbstractClass");

        UsdPrimClassification defClassification = defined.GetClassification();
        UsdPrimClassification overClassification = undefined.GetClassification();
        UsdPrimClassification classClassification = abstractClass.GetClassification();

        await Assert.That(defClassification.Specifier).IsEqualTo(UsdPrimSpecifier.Def);
        await Assert.That(overClassification.Specifier).IsEqualTo(UsdPrimSpecifier.Over);
        await Assert.That(classClassification.Specifier).IsEqualTo(UsdPrimSpecifier.Class);
        await Assert.That(defClassification.IsDefined).IsTrue();
        await Assert.That(overClassification.IsDefined).IsFalse();
        await Assert.That(classClassification.IsAbstract).IsTrue();
        await Assert.That(
                new[] { defClassification.Specifier, overClassification.Specifier, classClassification.Specifier }
                    .Distinct()
                    .Count())
            .IsEqualTo(3);
    }

    [Test]
    public async Task AttributeSplineRoundTripsThroughUsdAttribute()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(AttributeSplineRoundTripsThroughUsdAttribute));
        string stagePath = Path.Combine(directory, "attribute-spline.usda");

        using (UsdStage stage = UsdStage.Create(stagePath))
        {
            UsdAttribute attribute = stage.DefinePrim("/Animated", "Xform").GetAttribute("animated");
            using var authored = new TsSpline();
            authored.SetData(
            [
                new TsKnot(0, 1, null, 0, 0, 0, 0, TsInterpMode.Linear,
                    TsTangentAlgorithm.None, TsTangentAlgorithm.None),
                new TsKnot(5, 7, null, 0, 0, 0, 0, TsInterpMode.Curve,
                    TsTangentAlgorithm.AutoEase, TsTangentAlgorithm.AutoEase),
                new TsKnot(10, 3, null, 0, 0, 0, 0, TsInterpMode.Held,
                    TsTangentAlgorithm.None, TsTangentAlgorithm.None)
            ]);
            attribute.SetSpline(authored);
            await Assert.That(attribute.HasSpline()).IsTrue();
            stage.Save();
        }

        using UsdStage reopened = UsdStage.Open(stagePath);
        UsdAttribute readAttribute = reopened.GetPrim("/Animated").GetAttribute("animated");
        await Assert.That(readAttribute.HasSpline()).IsTrue();
        using TsSpline readSpline = readAttribute.GetSpline();
        IReadOnlyList<TsKnot> knots = readSpline.GetKnots();

        await Assert.That(knots.Count).IsEqualTo(3);
        await Assert.That(knots[0].Time).IsEqualTo(0);
        await Assert.That(knots[0].Value).IsEqualTo(1);
        await Assert.That(knots[0].NextInterpolation).IsEqualTo(TsInterpMode.Linear);
        await Assert.That(knots[1].Time).IsEqualTo(5);
        await Assert.That(knots[1].Value).IsEqualTo(7);
        await Assert.That(knots[1].NextInterpolation).IsEqualTo(TsInterpMode.Curve);
        await Assert.That(knots[^1].Time).IsEqualTo(10);
        await Assert.That(knots[^1].Value).IsEqualTo(3);
        await Assert.That(knots[^1].NextInterpolation).IsEqualTo(TsInterpMode.Held);
    }

    [Test]
    public async Task TfDebugSymbolsCanBeListedToggledAndRejectUnknownNames()
    {
        NativeCoverageRuntime.EnsureNativeLoaded();

        const string symbol = "TF_ERROR_MARK_TRACKING";
        IReadOnlyList<string> names = TfDebug.GetSymbolNames();
        await Assert.That(names.Contains(symbol, StringComparer.Ordinal)).IsTrue();
        await Assert.That(string.IsNullOrWhiteSpace(TfDebug.GetSymbolDescription(symbol))).IsFalse();

        bool before = TfDebug.GetSymbolEnabled(symbol);
        try
        {
            await Assert.That(TfDebug.SetSymbolEnabled(symbol, !before)).IsTrue();
            await Assert.That(TfDebug.GetSymbolEnabled(symbol)).IsEqualTo(!before);
            await Assert.That(() => TfDebug.SetSymbolEnabled("__OPENUSD_DOTNET_UNKNOWN_DEBUG_SYMBOL__", true))
                .Throws<OpenUsdNativeException>();
        }
        finally
        {
            TfDebug.SetSymbolEnabled(symbol, before);
        }
    }

    [Test]
    public async Task ViewerTfDebugPanelModelTogglesThroughAbiAndReloadsAbiState()
    {
        NativeCoverageRuntime.EnsureNativeLoaded();

        const string symbol = "TF_ERROR_MARK_TRACKING";
        var model = new ViewerTfDebugPanelModel();
        bool before = TfDebug.GetSymbolEnabled(symbol);
        try
        {
            ViewerTfDebugFlag changed = model.SetEnabled(symbol, !before);

            await Assert.That(changed.Enabled).IsEqualTo(!before);
            await Assert.That(TfDebug.GetSymbolEnabled(symbol)).IsEqualTo(!before);

            // Read-through is asserted across every symbol rather than by
            // toggling one back. TF_ERROR_MARK_TRACKING latches on Linux, so an
            // assertion that it returns to its prior value tests the symbol
            // rather than the model. Comparing the whole loaded set against the
            // ABI still fails if the model reports cached local state, which is
            // the property under test.
            ViewerTfDebugFlag[] reloaded = model.Load();
            await Assert.That(reloaded.Length).IsGreaterThan(0);
            foreach (ViewerTfDebugFlag flag in reloaded)
            {
                await Assert.That(flag.Enabled).IsEqualTo(TfDebug.GetSymbolEnabled(flag.Name));
            }
        }
        finally
        {
            TfDebug.SetSymbolEnabled(symbol, before);
        }
    }

    private static double Volume(UsdBounds3d bounds) =>
        bounds.Size.X * bounds.Size.Y * bounds.Size.Z;
}
