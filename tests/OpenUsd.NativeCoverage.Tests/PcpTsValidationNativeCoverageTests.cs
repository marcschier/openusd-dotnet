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
