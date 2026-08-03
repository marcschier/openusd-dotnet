// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Geom;

namespace OpenUsd.NativeProbe;

internal static partial class Program
{
    private static void RunPcpTsValidationProbe(string directory)
    {
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
        RequireNear(spline.Evaluate(2.5) ?? double.NaN, 5, "Ts first inter-knot evaluation");
        RequireNear(spline.Evaluate(7.5) ?? double.NaN, 15, "Ts second inter-knot evaluation");
        IReadOnlyList<TsKnot> knots = spline.GetKnots();
        Require(knots.Count == 3, "Ts bulk knot snapshot was not returned.");
        RequireNear(knots[1].Time, 5, "Ts middle knot time");
        RequireNear(knots[1].Value, 10, "Ts middle knot value");

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
            Require(index.Nodes.Count > 1, "Pcp prim index did not include composed nodes.");
            Require(index.Nodes[0].ArcType == PcpArcType.Root, "Pcp root arc was not first.");
            int variantIndex = FindNode(index, PcpArcType.Variant);
            int referenceIndex = FindNode(index, PcpArcType.Reference);
            Require(variantIndex > 0, "Pcp variant arc was missing.");
            Require(referenceIndex > variantIndex, "Pcp reference arc was not weaker than the variant arc.");
            Require(
                HasLayer(index.Nodes[variantIndex], stagePath),
                "Pcp variant arc did not report the host layer.");
            Require(
                HasLayer(index.Nodes[referenceIndex], referencedPath),
                "Pcp reference arc did not report the referenced layer.");

            UsdPrim wrongType = stage.DefinePrim("/WrongType", "Xform");
            Require(
                !UsdGeomCamera.TryWrap(wrongType, out UsdGeomCamera wrongCamera) &&
                string.IsNullOrEmpty(wrongCamera.Path),
                "TryWrap unexpectedly returned a usable camera for an Xform prim.");
        }

        IReadOnlyList<UsdValidationValidatorInfo> validators = UsdValidation.GetRegisteredValidators();
        Require(validators.Count > 0, "UsdValidation registry returned no validators.");
        string invalidPath = Path.Combine(directory, "validation-invalid-stage.usda");
        using UsdStage invalidStage = UsdStage.Create(invalidPath);
        IReadOnlyList<UsdValidationError> errors = UsdValidation.Validate(invalidStage);
        Require(errors.Count > 0, "UsdValidation returned no errors for an invalid empty stage.");
        Require(
            errors.Any(
                error => error.Severity == UsdValidationSeverity.Error &&
                    error.Message.Contains("defaultPrim", StringComparison.OrdinalIgnoreCase)),
            "UsdValidation did not report the expected missing defaultPrim error.");
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

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireNear(double actual, double expected, string label)
    {
        if (Math.Abs(actual - expected) > 1e-9)
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
