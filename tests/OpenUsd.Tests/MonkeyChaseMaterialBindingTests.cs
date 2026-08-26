// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Shade;

namespace OpenUsd.Tests;

public sealed class MonkeyChaseMaterialBindingTests
{
    [Test]
    public async Task EveryAuthoredMaterialBindingAppliesTheSchemaAndResolves()
    {
        using UsdStage stage = OpenStageOrSkip();
        var invalid = new List<string>();
        int bindingCount = 0;

        foreach (UsdPrim prim in stage.Traverse())
        {
            if (!prim.GetRelationshipNames().Contains("material:binding", StringComparer.Ordinal))
            {
                continue;
            }

            bindingCount++;
            string[] targets = prim.GetRelationshipTargets("material:binding");
            bool appliesSchema = prim.GetAppliedSchemas()
                .Contains("MaterialBindingAPI", StringComparer.Ordinal);
            bool resolves = targets.Length == 1 &&
                stage.GetDirectlyBoundMaterial(prim).Path == targets[0] &&
                stage.GetBoundMaterial(prim).Path == targets[0];
            if (!appliesSchema || !resolves)
            {
                invalid.Add(prim.Path);
            }
        }

        await Assert.That(bindingCount).IsEqualTo(66);
        await Assert.That(invalid).IsEmpty()
            .Because($"Invalid material bindings: {string.Join(", ", invalid)}");
    }

    private static UsdStage OpenStageOrSkip()
    {
        try
        {
            return UsdStage.Open(Path.Combine(
                FindRepositoryRoot(),
                "test-assets",
                "mcp-monkey-car-city.usda"));
        }
        catch (DllNotFoundException exception)
        {
            Skip.Test($"openusd_dotnet native runtime is unavailable: {exception.Message}");
            throw;
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
