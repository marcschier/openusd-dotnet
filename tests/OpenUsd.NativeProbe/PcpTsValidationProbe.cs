// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.NativeProbe;

internal static partial class Program
{
    private static void RunPcpTsValidationProbe(string directory)
    {
        using var spline = new TsSpline();
        spline.SetData(
            [
                new TsKnot(0, 1, null, 0, 0, 0, 0, TsInterpMode.Linear,
                    TsTangentAlgorithm.None, TsTangentAlgorithm.None),
                new TsKnot(10, 11, null, 0, 0, 0, 0, TsInterpMode.Held,
                    TsTangentAlgorithm.None, TsTangentAlgorithm.None)
            ]);
        RequireNear(spline.Evaluate(5) ?? double.NaN, 6, "Ts linear spline evaluation");
        Require(spline.GetKnots().Count == 2, "Ts bulk knot snapshot was not returned.");

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
            prim.AddReference(referencedPath, "/Target");
            PcpPrimIndex index = prim.GetPrimIndex();
            Require(index.Nodes.Count > 1, "Pcp prim index did not include composed nodes.");
            Require(index.Nodes.Any(node => node.ArcType == PcpArcType.Reference), "Pcp reference arc was missing.");
            Require(index.Nodes.Any(node => node.LayerIdentifiers.Count > 0), "Pcp layer identifiers were missing.");
        }

        IReadOnlyList<UsdValidationValidatorInfo> validators = UsdValidation.GetRegisteredValidators();
        Require(validators.Count > 0, "UsdValidation registry returned no validators.");
        string invalidPath = Path.Combine(directory, "validation-invalid-stage.usda");
        using UsdStage invalidStage = UsdStage.Create(invalidPath);
        IReadOnlyList<UsdValidationError> errors = UsdValidation.Validate(invalidStage);
        Require(errors.Count > 0, "UsdValidation returned no errors for an invalid empty stage.");
        Require(
            errors.Any(error => error.Severity == UsdValidationSeverity.Error),
            "UsdValidation returned no error severity.");
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




