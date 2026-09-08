// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Package.Tests;

internal static class PropertyInspectionConsumerSource
{
    internal const string Text =
        """"
        using System;
        using System.IO;
        using System.Linq;
        using OpenUsd;
        using OpenUsd.Interop;

        namespace PackageExecutionConsumer;

        internal static class PropertyInspectionConsumer
        {
            internal static bool Run()
            {
                string path = Path.Combine(AppContext.BaseDirectory,
                    "properties-" + Guid.NewGuid().ToString("N") + ".usda");
                const string source = """
                    #usda 1.0
                    def Xform "Subject"
                    {
                        custom double3 precise = (1.25, 2.5, 3.75)
                        custom double curve
                        custom int[] numbers = [1, 2, 3, 4]
                        custom double sampled.timeSamples = { 1: 2, 3: 6 }
                        custom float connected = 4
                        custom float connected.connect = </Graph.outputs:value>
                        custom asset missing = @missing-property-inspection.bin@
                        custom rel target = </Other>
                    }
                    """;
                File.WriteAllText(path, source);
                try
                {
                    UsdPrimPropertySnapshot snapshot;
                    UsdStageScheduler scheduler = UsdStageScheduler.Open(path);
                    try
                    {
                        snapshot = scheduler.InvokeAsync(stage =>
                        {
                            using (var spline = new TsSpline())
                            {
                                spline.SetData(new[]
                                {
                                    new TsKnot(1, 10, null, 0, 0, 0, 0, TsInterpMode.Linear,
                                        TsTangentAlgorithm.None, TsTangentAlgorithm.None),
                                    new TsKnot(3, 20, null, 0, 0, 0, 0, TsInterpMode.Held,
                                        TsTangentAlgorithm.None, TsTangentAlgorithm.None)
                                });
                                stage.GetPrim("/Subject").GetAttribute("curve").SetSpline(spline);
                            }
                            ulong serial = stage.ChangeSerial;
                            UsdPrimPropertySnapshot result = stage.GetPrimPropertySnapshot("/Subject", 1,
                                new UsdPropertyInspectionLimits(previewElements: 2));
                            if (result.ChangeSerial != serial || stage.ChangeSerial != serial)
                            {
                                throw new InvalidOperationException("Property query changed the source.");
                            }
                            return result;
                        }).GetAwaiter().GetResult();
                        bool refused = scheduler.InvokeAsync(stage =>
                        {
                            try
                            {
                                _ = stage.GetPrimPropertySnapshot("/Subject",
                                    limits: new UsdPropertyInspectionLimits(maximumPropertyCount: 1));
                                return false;
                            }
                            catch (OpenUsdNativeException error)
                            {
                                return error.Message.Contains("property count", StringComparison.Ordinal);
                            }
                        }).GetAwaiter().GetResult();
                        if (!refused)
                        {
                            return false;
                        }
                    }
                    finally
                    {
                        scheduler.DisposeAsync().GetAwaiter().GetResult();
                    }
                    var numbers = (UsdAttributePropertySnapshot)snapshot.Properties.Single(p => p.Name == "numbers");
                    var precise = (UsdAttributePropertySnapshot)snapshot.Properties.Single(p => p.Name == "precise");
                    var curve = (UsdAttributePropertySnapshot)snapshot.Properties.Single(p => p.Name == "curve");
                    var sampled = (UsdAttributePropertySnapshot)snapshot.Properties.Single(p => p.Name == "sampled");
                    var connected = (UsdAttributePropertySnapshot)snapshot.Properties
                        .Single(p => p.Name == "connected");
                    var missing = (UsdAttributePropertySnapshot)snapshot.Properties.Single(p => p.Name == "missing");
                    return !snapshot.IsComplete && snapshot.PrimPath == "/Subject" && snapshot.TimeCode == 1 &&
                        numbers.Value.ElementCount == 4 && numbers.Value.Elements.Count == 2 &&
                        numbers.Value.Elements[1] == "2" &&
                        numbers.Value.Status == UsdPropertyPreviewStatus.Truncated &&
                        numbers.Value.ReasonKind == UsdPropertyPreviewReasonKind.PreviewLimit &&
                        precise.TypeName == "double3" && precise.Value.Elements.Single() == "(1.25, 2.5, 3.75)" &&
                        precise.Value.ReasonKind == UsdPropertyPreviewReasonKind.None &&
                        curve.ResolveSource == UsdAttributeResolveSource.Deferred &&
                        curve.Value.ReasonKind == UsdPropertyPreviewReasonKind.Spline &&
                        curve.TimeSamples.ReasonKind == UsdPropertyPreviewReasonKind.Spline &&
                        curve.Connections.ReasonKind == UsdPropertyPreviewReasonKind.None &&
                        sampled.Value.Elements.Single() == "2" && sampled.TimeSamples.Count == 2 &&
                        sampled.TimeSamples.Times[1] == 3 && sampled.ValueSource?.SpecPath == "/Subject.sampled" &&
                        connected.Value.Elements.Single() == "4" &&
                        connected.Connections.Paths.Single() == "/Graph.outputs:value" &&
                        missing.Value.Assets.Single().IsMissing == true &&
                        !(snapshot.Properties is UsdPropertySnapshot[]) && !(numbers.Value.Elements is string[]) &&
                        File.ReadAllText(path) == source;
                }
                finally
                {
                    File.Delete(path);
                }
            }
        }
        """";
}
