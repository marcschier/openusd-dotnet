// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.NativeProbe;

internal static class PropertyInspectionProbe
{
    internal static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"properties-aot-{Guid.NewGuid():N}.usda");
        string assetPath = Path.ChangeExtension(path, ".bin");
        string source = $$"""
            #usda 1.0
            def Xform "Subject"
            {
                custom double3 precise = (1.25, 2.5, 3.75)
                custom int[] numbers = [1, 2, 3, 4]
                custom double sampled.timeSamples = { 1: 2, 3: 6 }
                custom float connected = 4
                custom float connected.connect = </Graph.outputs:value>
                custom asset file = @{{Path.GetFileName(assetPath)}}@
                custom rel target = </Other>
            }
            """;
        await File.WriteAllTextAsync(path, source).ConfigureAwait(false);
        await File.WriteAllTextAsync(assetPath, "native property asset").ConfigureAwait(false);
        try
        {
            UsdPrimPropertySnapshot snapshot;
            UsdPropertyValuePreview nested;
            await using (UsdStageScheduler scheduler = UsdStageScheduler.Open(path))
            {
                snapshot = await scheduler.InvokeAsync(stage =>
                {
                    ulong serial = stage.ChangeSerial;
                    UsdPrimPropertySnapshot result = stage.GetPrimPropertySnapshot("/Subject", 1,
                        new UsdPropertyInspectionLimits(previewElements: 2));
                    if (stage.ChangeSerial != serial || result.ChangeSerial != serial)
                    {
                        throw new InvalidOperationException("The property query changed its source stage.");
                    }
                    return result;
                }).ConfigureAwait(false);
                nested = await scheduler.InvokeAsync(stage =>
                    ((UsdAttributePropertySnapshot)stage.GetPrimPropertySnapshot("/Subject").Properties
                        .Single(property => property.Name == "precise")).Value).ConfigureAwait(false);
                bool refused = await scheduler.InvokeAsync(stage =>
                {
                    try
                    {
                        _ = stage.GetPrimPropertySnapshot("/Subject",
                            limits: new UsdPropertyInspectionLimits(maximumPropertyCount: 1));
                        return false;
                    }
                    catch (OpenUsdNativeException exception)
                    {
                        return exception.Message.Contains("property count", StringComparison.Ordinal);
                    }
                }).ConfigureAwait(false);
                if (!refused)
                {
                    throw new InvalidOperationException("The property row quota was not enforced.");
                }
            }
            var numbers = (UsdAttributePropertySnapshot)snapshot.Properties
                .Single(property => property.Name == "numbers");
            var sampled = (UsdAttributePropertySnapshot)snapshot.Properties
                .Single(property => property.Name == "sampled");
            var connected = (UsdAttributePropertySnapshot)snapshot.Properties
                .Single(property => property.Name == "connected");
            var asset = (UsdAttributePropertySnapshot)snapshot.Properties.Single(property => property.Name == "file");
            if (snapshot.PrimPath != "/Subject" || snapshot.TimeCode != 1 || snapshot.IsComplete ||
                numbers.Value.ElementCount != 4 || numbers.Value.Elements.Count != 2 ||
                numbers.Value.Elements[1] != "2" || numbers.Value.Status != UsdPropertyPreviewStatus.Truncated ||
                sampled.TimeSamples.Count != 2 || sampled.TimeSamples.Times[1] != 3 ||
                sampled.Value.Elements.Single() != "2" || sampled.ValueSource?.SpecPath != "/Subject.sampled" ||
                connected.Value.Elements.Single() != "4" ||
                connected.Connections.Paths.Single() != "/Graph.outputs:value" ||
                nested.Elements.Single() != "(1.25, 2.5, 3.75)" ||
                asset.Value.Assets.Single().IsMissing != false ||
                !File.Exists(asset.Value.Assets.Single().ResolvedPath) ||
                snapshot.Properties is UsdPropertySnapshot[] || nested.Elements is string[] ||
                await File.ReadAllTextAsync(path).ConfigureAwait(false) != source)
            {
                throw new InvalidOperationException("The detached native property snapshot lost its semantics.");
            }
            Console.WriteLine("PROPERTY_SNAPSHOT_MANAGED_OK: typed prefixes, samples, graph, native assets, " +
                "quota, detached lifetime");
            await VerifyCompositionRefusalAsync(directory).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(path);
            File.Delete(assetPath);
        }
    }

    private static async Task VerifyCompositionRefusalAsync(string directory)
    {
        string path = Path.Combine(directory, $"property-composition-aot-{Guid.NewGuid():N}.usda");
        await File.WriteAllTextAsync(path,
            """
            #usda 1.0
            def "Subject" (references = </Missing>)
            {
                custom double answer = 7
            }
            """).ConfigureAwait(false);
        try
        {
            using UsdStage stage = UsdStage.Open(path);
            bool refused = false;
            try
            {
                _ = stage.GetPrimPropertySnapshot("/Subject");
            }
            catch (OpenUsdNativeException error)
            {
                refused = error.Status == OpenUsdNativeStatus.NativeError &&
                    error.Message.Contains("composition", StringComparison.Ordinal);
            }
            if (!refused)
            {
                throw new InvalidOperationException("Stored composition errors produced a false complete inventory.");
            }
            Console.WriteLine("PROPERTY_COMPOSITION_MANAGED_OK: stored errors refuse complete inventory");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
