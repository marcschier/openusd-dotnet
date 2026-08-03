// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Geom;
using OpenUsd.Interop;
using OpenUsd.Lux;
using OpenUsd.Shade;
using OpenUsd.Skel;

namespace OpenUsd.NativeProbe;

internal static partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (StageAccessEndProbe.TryRunChild(args, out int childExitCode))
        {
            return childExitCode;
        }
        if (ManagedSafetyProbe.TryRun(args, out int safetyExitCode))
        {
            return safetyExitCode;
        }

        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: OpenUsd.NativeProbe <plugin-path> <stage-path>");
            return 2;
        }

        try
        {
            ManagedSafetyProbe.Run();
            uint abiVersion = OpenUsdNativeRuntime.AbiVersion;
            Console.WriteLine($"ABI: {abiVersion}");
            if (abiVersion != OpenUsdNativeContract.AbiVersion)
            {
                Console.Error.WriteLine(
                    $"ABI mismatch: managed={OpenUsdNativeContract.AbiVersion}, native={abiVersion}");
                return 3;
            }
            if ((OpenUsdNativeRuntime.Capabilities &
                 OpenUsdNativeContract.RequiredCapabilities) !=
                OpenUsdNativeContract.RequiredCapabilities)
            {
                Console.Error.WriteLine("Required ABI v10 capabilities are missing.");
                return 4;
            }

            bool embeddedNullRejected = false;
            try
            {
                using OpenUsdNativeStage _ = OpenUsdNativeRuntime.OpenStage("invalid\0path");
            }
            catch (ArgumentException)
            {
                embeddedNullRejected = true;
            }
            if (!embeddedNullRejected)
            {
                Console.Error.WriteLine("Embedded-null direct strings were not rejected.");
                return 5;
            }

            Console.WriteLine($"OpenUSD: {OpenUsdNativeRuntime.Version}");
            nuint pluginCount = OpenUsdNativeRuntime.RegisterPlugins(args[0]);
            Console.WriteLine($"Registered plugins: {pluginCount}");
            using OpenUsdNativeStage stage = OpenUsdNativeRuntime.OpenStage(args[1]);
            Console.WriteLine($"Root layer: {stage.RootLayerIdentifier}");
            await StageAccessEndProbe.RunParentAsync(stage).ConfigureAwait(false);

            for (int i = 0; i < 128; i++)
            {
                using OpenUsdNativeStage stressStage = OpenUsdNativeRuntime.OpenStage(args[1]);
            }

            string directory = Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
            string authoredPath = Path.Combine(directory, "managed-authored.usda");
            File.Delete(authoredPath);

            ulong initialSerial;
            using (OpenUsdNativeStage authored = OpenUsdNativeRuntime.CreateStage(authoredPath))
            {
                initialSerial = authored.ChangeSerial;
                authored.DefinePrim("/World", "Xform");
                authored.DefinePrim("/World/Node", "Xform");
                authored.SetDouble("/World/Node", "custom:temperature", 42.5);
                authored.SetDouble("/World/Node", "custom:temperature", 43.5, timeCode: 10);
                authored.SetDoubleArray("/World/Node", "custom:samples", [1, 2, 3, 5, 8]);
                authored.SetDoubleArray("/World/Node", "custom:empty", []);
                using OpenUsdNativeLayer rootLayer = authored.GetRootLayer();
                Console.WriteLine($"Authored layer: {rootLayer.Identifier}");
                rootLayer.Save();
                authored.Save();
                if (authored.ChangeSerial <= initialSerial)
                {
                    Console.Error.WriteLine("Stage change notices did not advance the serial.");
                    return 4;
                }
            }

            using (OpenUsdNativeStage reopened = OpenUsdNativeRuntime.OpenStage(authoredPath))
            {
                string[] primPaths = reopened.GetPrimPaths();
                Console.WriteLine($"Prim paths: {string.Join(", ", primPaths)}");
                if (!primPaths.Contains("/World/Node", StringComparer.Ordinal) ||
                    reopened.GetDouble("/World/Node", "custom:temperature") != 42.5 ||
                    reopened.GetDouble("/World/Node", "custom:temperature", timeCode: 10) != 43.5 ||
                    !reopened.GetDoubleArray("/World/Node", "custom:samples")
                        .SequenceEqual([1, 2, 3, 5, 8]) ||
                    reopened.GetDoubleArray("/World/Node", "custom:empty").Length != 0)
                {
                    Console.Error.WriteLine("Authored stage did not round-trip.");
                    return 5;
                }
            }

            string facadePath = Path.Combine(directory, "facade-authored.usda");
            File.Delete(facadePath);
            using (UsdStage facade = UsdStage.Create(facadePath))
            {
                UsdPrim sensor = facade.DefinePrim("/World/Sensor", "Xform");
                sensor.SetDouble("custom:value", 12.5);
                sensor.SetDoubleArray("custom:series", [2, 4, 8, 16]);
                facade.Save();
            }
            using (UsdStage facade = UsdStage.Open(facadePath))
            {
                UsdPrim sensor = facade.GetPrim("/World/Sensor");
                if (sensor.GetDouble("custom:value") != 12.5 ||
                    !sensor.GetDoubleArray("custom:series").SequenceEqual([2, 4, 8, 16]) ||
                    !facade.Traverse().Any(static prim => prim.Path == "/World/Sensor"))
                {
                    Console.Error.WriteLine("Idiomatic facade did not round-trip.");
                    return 6;
                }
            }

            string schedulerPath = Path.Combine(directory, "scheduler-authored.usda");
            File.Delete(schedulerPath);
            await using (var scheduler = UsdStageScheduler.Create(schedulerPath))
            {
                await scheduler.InvokeAsync(s =>
                {
                    s.DefinePrim("/World/Signal", "Xform");
                }).ConfigureAwait(false);

                Task[] writes = Enumerable.Range(0, 100)
                    .Select(value => scheduler.InvokeAsync(s =>
                    {
                        s.GetPrim("/World/Signal").SetDouble("custom:value", value);
                    }).AsTask())
                    .ToArray();
                await Task.WhenAll(writes).ConfigureAwait(false);

                ulong serial = await scheduler.InvokeAsync(s =>
                {
                    s.Save();
                    return s.ChangeSerial;
                }).ConfigureAwait(false);
                if (serial == 0)
                {
                    Console.Error.WriteLine("Scheduled edits did not produce change notices.");
                    return 7;
                }
            }
            using (UsdStage scheduled = UsdStage.Open(schedulerPath))
            {
                if (scheduled.GetPrim("/World/Signal").GetDouble("custom:value") != 99)
                {
                    Console.Error.WriteLine("Scheduled writes were not applied in order.");
                    return 8;
                }
            }
            await StageChangeFeedProbe.RunAsync(directory).ConfigureAwait(false);
            await StageBoundEscapeProbe.RunAsync(directory).ConfigureAwait(false);
            await SchedulerAsyncResultProbe.RunAsync(directory).ConfigureAwait(false);
            await SharedStageProbe.RunAsync(args[0], directory).ConfigureAwait(false);

            // Typed values: bool, int64, string, token, vec3f, color3f.
            string valuesPath = Path.Combine(directory, "values-authored.usda");
            File.Delete(valuesPath);
            using (UsdStage valueStage = UsdStage.Create(valuesPath))
            {
                UsdPrim prim = valueStage.DefinePrim("/World/Values", "Xform");
                prim.SetBool("custom:flag", true);
                prim.SetBool("custom:flagSampled", false, timeCode: 5);
                prim.SetInt64("custom:count", 123456789012345);
                prim.SetString("custom:label", "hello world");
                prim.SetToken("custom:kind", "Alpha");
                prim.SetVec3f("custom:direction", new UsdVec3f(1, 2, 3));
                prim.SetColor3f("custom:tint", new UsdVec3f(0.25f, 0.5f, 0.75f));
                valueStage.Save();
            }
            using (UsdStage valueStage = UsdStage.Open(valuesPath))
            {
                UsdPrim prim = valueStage.GetPrim("/World/Values");
                if (!prim.GetBool("custom:flag") ||
                    prim.GetBool("custom:flagSampled", 5) ||
                    prim.GetInt64("custom:count") != 123456789012345 ||
                    prim.GetString("custom:label") != "hello world" ||
                    prim.GetToken("custom:kind") != "Alpha" ||
                    prim.GetVec3f("custom:direction") != new UsdVec3f(1, 2, 3) ||
                    prim.GetColor3f("custom:tint") != new UsdVec3f(0.25f, 0.5f, 0.75f))
                {
                    Console.Error.WriteLine("Typed value round trip failed.");
                    return 9;
                }
            }

            // Prim lifecycle: existence, removal, activation, visibility, and purpose.
            string lifecyclePath = Path.Combine(directory, "lifecycle-authored.usda");
            File.Delete(lifecyclePath);
            using (UsdStage lifecycleStage = UsdStage.Create(lifecyclePath))
            {
                lifecycleStage.DefinePrim("/World", "Xform");
                UsdPrim a = lifecycleStage.DefinePrim("/World/A", "Xform");
                lifecycleStage.DefinePrim("/World/B", "Xform");
                a.SetVisibility("invisible");
                a.SetPurpose("guide");
                a.SetActive(false);
                lifecycleStage.RemovePrim("/World/B");
                lifecycleStage.Save();
            }
            using (UsdStage lifecycleStage = UsdStage.Open(lifecyclePath))
            {
                UsdPrim a = lifecycleStage.GetPrim("/World/A");
                if (!lifecycleStage.HasPrim("/World/A") ||
                    lifecycleStage.HasPrim("/World/B") ||
                    a.IsActive() ||
                    a.GetVisibility() != "invisible" ||
                    a.GetPurpose() != "guide" ||
                    lifecycleStage.Traverse().Any(static prim => prim.Path == "/World/A"))
                {
                    Console.Error.WriteLine("Prim lifecycle state did not round-trip.");
                    return 10;
                }
            }

            // Relationships: create, set/get, and clear a packed bulk target buffer.
            string relationshipPath = Path.Combine(directory, "relationship-authored.usda");
            File.Delete(relationshipPath);
            using (UsdStage relStage = UsdStage.Create(relationshipPath))
            {
                UsdPrim a = relStage.DefinePrim("/World/A", "Xform");
                relStage.DefinePrim("/World/B", "Xform");
                relStage.DefinePrim("/World/C", "Xform");
                a.CreateRelationship("myRel");
                a.SetRelationshipTargets("myRel", ["/World/B", "/World/C"]);
                relStage.Save();
            }
            using (UsdStage relStage = UsdStage.Open(relationshipPath))
            {
                UsdPrim a = relStage.GetPrim("/World/A");
                if (!a.GetRelationshipTargets("myRel").SequenceEqual(["/World/B", "/World/C"]))
                {
                    Console.Error.WriteLine("Relationship targets did not round-trip.");
                    return 11;
                }
                a.ClearRelationshipTargets("myRel");
                relStage.Save();
            }
            using (UsdStage relStage = UsdStage.Open(relationshipPath))
            {
                if (relStage.GetPrim("/World/A").GetRelationshipTargets("myRel").Length != 0)
                {
                    Console.Error.WriteLine("Relationship targets were not cleared.");
                    return 12;
                }
            }

            // Composition: references and payloads against small temporary USDA assets.
            string referencedPath = Path.Combine(directory, "referenced.usda");
            File.Delete(referencedPath);
            using (UsdStage refStage = UsdStage.Create(referencedPath))
            {
                UsdPrim refPrim = refStage.DefinePrim("/Ref", "Xform");
                refPrim.SetDouble("custom:refValue", 7.5);
                refStage.Save();
            }

            string payloadAssetPath = Path.Combine(directory, "payload.usda");
            File.Delete(payloadAssetPath);
            using (UsdStage payloadStage = UsdStage.Create(payloadAssetPath))
            {
                UsdPrim payloadPrim = payloadStage.DefinePrim("/Payload", "Xform");
                payloadPrim.SetDouble("custom:payloadValue", 3.5);
                payloadStage.Save();
            }

            string compositionPath = Path.Combine(directory, "composition-authored.usda");
            File.Delete(compositionPath);
            using (UsdStage compStage = UsdStage.Create(compositionPath))
            {
                UsdPrim refTarget = compStage.DefinePrim("/World/RefTarget", "Xform");
                refTarget.AddReference(referencedPath, "/Ref");
                UsdPrim payloadTarget = compStage.DefinePrim("/World/PayloadTarget", "Xform");
                payloadTarget.AddPayload(payloadAssetPath, "/Payload");
                UsdPrim proto = compStage.DefinePrim("/World/Proto", "Xform");
                proto.SetInstanceable(true);
                compStage.Save();
            }
            using (UsdStage compStage = UsdStage.Open(compositionPath))
            {
                if (compStage.GetPrim("/World/RefTarget").GetDouble("custom:refValue") != 7.5 ||
                    compStage.GetPrim("/World/PayloadTarget").GetDouble("custom:payloadValue") != 3.5 ||
                    !compStage.GetPrim("/World/Proto").IsInstanceable())
                {
                    Console.Error.WriteLine("Reference, payload, or instanceable state did not round-trip.");
                    return 13;
                }

                compStage.GetPrim("/World/RefTarget").ClearReferences();
                compStage.Save();
            }
            using (UsdStage compStage = UsdStage.Open(compositionPath))
            {
                bool referenceCleared;
                try
                {
                    compStage.GetPrim("/World/RefTarget").GetDouble("custom:refValue");
                    referenceCleared = false;
                }
                catch (OpenUsdNativeException)
                {
                    referenceCleared = true;
                }

                if (!compStage.HasPrim("/World/RefTarget") || !referenceCleared)
                {
                    Console.Error.WriteLine("References were not cleared.");
                    return 14;
                }
            }

            // Variants: variant set/variant creation, selection, and enumeration.
            string variantPath = Path.Combine(directory, "variant-authored.usda");
            File.Delete(variantPath);
            using (UsdStage variantStage = UsdStage.Create(variantPath))
            {
                UsdPrim host = variantStage.DefinePrim("/World/VariantHost", "Xform");
                host.AddVariantSet("look");
                host.AddVariant("look", "red");
                host.AddVariant("look", "blue");
                host.SetVariantSelection("look", "red");
                variantStage.Save();
            }
            using (UsdStage variantStage = UsdStage.Open(variantPath))
            {
                UsdPrim host = variantStage.GetPrim("/World/VariantHost");
                string[] names = host.GetVariantNames("look");
                if (names.Length != 2 ||
                    !names.Contains("red", StringComparer.Ordinal) ||
                    !names.Contains("blue", StringComparer.Ordinal) ||
                    host.GetVariantSelection("look") != "red")
                {
                    Console.Error.WriteLine("Variant set state did not round-trip.");
                    return 15;
                }

                host.SetVariantSelection("look", null);
                variantStage.Save();
            }
            using (UsdStage variantStage = UsdStage.Open(variantPath))
            {
                bool selectionCleared;
                try
                {
                    variantStage.GetPrim("/World/VariantHost").GetVariantSelection("look");
                    selectionCleared = false;
                }
                catch (OpenUsdNativeException)
                {
                    selectionCleared = true;
                }

                if (!selectionCleared)
                {
                    Console.Error.WriteLine("Clearing the variant selection did not take effect.");
                    return 16;
                }
            }

            // Sublayers: add/remove/list on the root layer using a small temporary USDA asset.
            string sublayerAssetPath = Path.Combine(directory, "sublayer.usda");
            File.Delete(sublayerAssetPath);
            using (UsdStage subStage = UsdStage.Create(sublayerAssetPath))
            {
                subStage.DefinePrim("/World", "Xform");
                subStage.DefinePrim("/World/FromSublayer", "Xform");
                subStage.Save();
            }

            string sublayerHostPath = Path.Combine(directory, "sublayer-host-authored.usda");
            File.Delete(sublayerHostPath);
            using (UsdStage sublayerStage = UsdStage.Create(sublayerHostPath))
            {
                using (UsdLayer rootLayer = sublayerStage.GetRootLayer())
                {
                    rootLayer.AddSublayer(sublayerAssetPath);
                }
                sublayerStage.Save();
            }
            using (UsdStage sublayerStage = UsdStage.Open(sublayerHostPath))
            {
                using UsdLayer rootLayer = sublayerStage.GetRootLayer();
                string[] sublayers = rootLayer.GetSublayerPaths();
                string normalizedAssetPath = sublayerAssetPath.Replace('\\', '/');
                if (!sublayerStage.Traverse().Any(static prim => prim.Path == "/World/FromSublayer") ||
                    !sublayers.Any(path => path.Replace('\\', '/') == normalizedAssetPath))
                {
                    Console.Error.WriteLine("Sublayer composition did not round-trip.");
                    return 17;
                }

                rootLayer.RemoveSublayer(
                    sublayers.First(path => path.Replace('\\', '/') == normalizedAssetPath));
                sublayerStage.Save();
            }
            using (UsdStage sublayerStage = UsdStage.Open(sublayerHostPath))
            {
                using UsdLayer rootLayer = sublayerStage.GetRootLayer();
                if (rootLayer.GetSublayerPaths().Length != 0)
                {
                    Console.Error.WriteLine("Sublayer removal did not take effect.");
                    return 18;
                }
            }

            // Metadata: safe tagged string/bool/int64/double operations on prims and layers.
            string metadataPath = Path.Combine(directory, "metadata-authored.usda");
            File.Delete(metadataPath);
            using (UsdStage metadataStage = UsdStage.Create(metadataPath))
            {
                UsdPrim prim = metadataStage.DefinePrim("/World/Metadata", "Xform");
                prim.SetMetadata("owner", "unit-test");
                prim.SetMetadata("enabled", true);
                prim.SetMetadata("revision", 42L);
                prim.SetMetadata("weight", 3.5);
                using (UsdLayer rootLayer = metadataStage.GetRootLayer())
                {
                    rootLayer.SetMetadata("buildId", "abc123");
                    rootLayer.SetMetadata("verified", true);
                    rootLayer.SetMetadata("count", 7L);
                    rootLayer.SetMetadata("ratio", 0.5);
                }
                metadataStage.Save();
            }
            using (UsdStage metadataStage = UsdStage.Open(metadataPath))
            {
                UsdPrim prim = metadataStage.GetPrim("/World/Metadata");
                using UsdLayer rootLayer = metadataStage.GetRootLayer();

                bool typeMismatchRejected;
                try
                {
                    prim.GetMetadataBool("owner");
                    typeMismatchRejected = false;
                }
                catch (OpenUsdNativeException)
                {
                    typeMismatchRejected = true;
                }

                if (prim.GetMetadataString("owner") != "unit-test" ||
                    !prim.GetMetadataBool("enabled") ||
                    prim.GetMetadataInt64("revision") != 42 ||
                    prim.GetMetadataDouble("weight") != 3.5 ||
                    rootLayer.GetMetadataString("buildId") != "abc123" ||
                    !rootLayer.GetMetadataBool("verified") ||
                    rootLayer.GetMetadataInt64("count") != 7 ||
                    rootLayer.GetMetadataDouble("ratio") != 0.5 ||
                    !typeMismatchRejected)
                {
                    Console.Error.WriteLine(
                        "Metadata state did not round-trip, or a type mismatch was not rejected.");
                    return 19;
                }

                prim.ClearMetadata("owner");
                rootLayer.ClearMetadata("buildId");
                metadataStage.Save();
            }
            using (UsdStage metadataStage = UsdStage.Open(metadataPath))
            {
                UsdPrim prim = metadataStage.GetPrim("/World/Metadata");
                using UsdLayer rootLayer = metadataStage.GetRootLayer();

                bool primMetadataCleared;
                try
                {
                    prim.GetMetadataString("owner");
                    primMetadataCleared = false;
                }
                catch (OpenUsdNativeException)
                {
                    primMetadataCleared = true;
                }

                bool layerMetadataCleared;
                try
                {
                    rootLayer.GetMetadataString("buildId");
                    layerMetadataCleared = false;
                }
                catch (OpenUsdNativeException)
                {
                    layerMetadataCleared = true;
                }

                if (!primMetadataCleared || !layerMetadataCleared)
                {
                    Console.Error.WriteLine("Cleared metadata was still readable.");
                    return 20;
                }
            }

            // Core stage controls: timing, default prim, session layer, reload, and export.
            string corePath = Path.Combine(directory, "core-api-authored.usda");
            string stageExportPath = Path.Combine(directory, "core-api-flattened.usda");
            string layerExportPath = Path.Combine(directory, "core-api-layer.usda");
            File.Delete(corePath);
            File.Delete(stageExportPath);
            File.Delete(layerExportPath);
            using (UsdStage coreStage = UsdStage.Create(corePath))
            {
                bool missingDefaultRejected;
                try
                {
                    coreStage.GetDefaultPrim();
                    missingDefaultRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.NotFound)
                {
                    missingDefaultRejected = true;
                }

                coreStage.DefinePrim("/World", "Xform");
                bool unknownDefaultRejected;
                try
                {
                    coreStage.SetDefaultPrim("/Missing");
                    unknownDefaultRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.NotFound)
                {
                    unknownDefaultRejected = true;
                }

                coreStage.StartTimeCode = 1.25;
                coreStage.EndTimeCode = 48.5;
                coreStage.FramesPerSecond = 30;
                coreStage.TimeCodesPerSecond = 60;
                coreStage.SetDefaultPrim("/World");

                using UsdLayer sessionLayer = coreStage.GetSessionLayer();
                if (!missingDefaultRejected ||
                    !unknownDefaultRejected ||
                    coreStage.GetDefaultPrim().Path != "/World" ||
                    coreStage.SessionLayerIdentifier != sessionLayer.Identifier ||
                    string.IsNullOrWhiteSpace(sessionLayer.Identifier))
                {
                    Console.Error.WriteLine("Default prim or session layer controls failed.");
                    return 21;
                }

                coreStage.Save();
                using (UsdLayer rootLayer = coreStage.GetRootLayer())
                {
                    rootLayer.Export(layerExportPath);
                    if (!rootLayer.Reload(force: true))
                    {
                        Console.Error.WriteLine("Forced root layer reload was skipped.");
                        return 22;
                    }
                }
                coreStage.Export(stageExportPath);
                coreStage.Reload();
            }

            using (UsdStage coreStage = UsdStage.Open(corePath))
            {
                bool invalidRateRejected;
                try
                {
                    coreStage.FramesPerSecond = 0;
                    invalidRateRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.InvalidArgument)
                {
                    invalidRateRejected = true;
                }

                if (coreStage.StartTimeCode != 1.25 ||
                    coreStage.EndTimeCode != 48.5 ||
                    coreStage.FramesPerSecond != 30 ||
                    coreStage.TimeCodesPerSecond != 60 ||
                    coreStage.GetDefaultPrim().Path != "/World" ||
                    !invalidRateRejected ||
                    !File.Exists(stageExportPath) ||
                    !File.Exists(layerExportPath))
                {
                    Console.Error.WriteLine("Core stage controls did not round-trip.");
                    return 23;
                }

                coreStage.ClearDefaultPrim();
                coreStage.Save();
            }

            using (UsdStage coreStage = UsdStage.Open(corePath))
            {
                bool clearedDefaultRejected;
                try
                {
                    coreStage.GetDefaultPrim();
                    clearedDefaultRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.NotFound)
                {
                    clearedDefaultRejected = true;
                }

                if (!clearedDefaultRejected)
                {
                    Console.Error.WriteLine("Cleared default prim was still readable.");
                    return 24;
                }
            }

            // Bulk scene inspection: specifier operations, type/schema data, children, and properties.
            string inspectionPath = Path.Combine(directory, "scene-inspection-authored.usda");
            File.Delete(inspectionPath);
            using (UsdStage inspectionStage = UsdStage.Create(inspectionPath))
            {
                UsdPrim world = inspectionStage.DefinePrim("/World", "Xform");
                UsdPrim defined = inspectionStage.DefinePrim("/World/Defined", "Xform");
                inspectionStage.DefinePrim("/World/Defined/Grandchild", "Xform");
                UsdPrim over = inspectionStage.OverridePrim("/World/Over");
                UsdPrim classPrim = inspectionStage.CreateClassPrim("/Template");
                defined.SetDouble("custom:value", 7.5);
                defined.CreateRelationship("custom:link");

                bool nestedClassRejected;
                try
                {
                    inspectionStage.CreateClassPrim("/World/NestedClass");
                    nestedClassRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.InvalidArgument)
                {
                    nestedClassRejected = true;
                }

                string[] childPaths = world.GetChildren().Select(static child => child.Path).ToArray();
                string[] attributeNames = defined.GetAttributeNames();
                string[] relationshipNames = defined.GetRelationshipNames();
                _ = defined.GetAppliedSchemas();
                if (!nestedClassRejected ||
                    defined.TypeName != "Xform" ||
                    over.TypeName.Length != 0 ||
                    classPrim.TypeName.Length != 0 ||
                    !childPaths.Contains("/World/Defined", StringComparer.Ordinal) ||
                    !childPaths.Contains("/World/Over", StringComparer.Ordinal) ||
                    childPaths.Contains("/World/Defined/Grandchild", StringComparer.Ordinal) ||
                    !attributeNames.Contains("custom:value", StringComparer.Ordinal) ||
                    !relationshipNames.Contains("custom:link", StringComparer.Ordinal) ||
                    relationshipNames.Contains("custom:value", StringComparer.Ordinal) ||
                    attributeNames.Contains("custom:link", StringComparer.Ordinal))
                {
                    Console.Error.WriteLine("Managed bulk scene inspection failed.");
                    return 25;
                }
                inspectionStage.Save();
            }

            using (UsdStage inspectionStage = UsdStage.Open(inspectionPath))
            {
                bool missingTypeRejected;
                try
                {
                    _ = inspectionStage.GetPrim("/Missing").TypeName;
                    missingTypeRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.NotFound)
                {
                    missingTypeRejected = true;
                }

                bool missingChildrenRejected;
                try
                {
                    _ = inspectionStage.GetPrim("/Missing").GetChildren();
                    missingChildrenRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.NotFound)
                {
                    missingChildrenRejected = true;
                }

                using OpenUsdNativeStage nativeStage = OpenUsdNativeRuntime.OpenStage(inspectionPath);
                bool invalidPathRejected;
                try
                {
                    _ = nativeStage.GetPrimTypeName("relative");
                    invalidPathRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.InvalidArgument)
                {
                    invalidPathRejected = true;
                }

                UsdPrim defined = inspectionStage.GetPrim("/World/Defined");
                string[] childPaths = inspectionStage.GetPrim("/World")
                    .GetChildren()
                    .Select(static child => child.Path)
                    .ToArray();
                if (!missingTypeRejected ||
                    !missingChildrenRejected ||
                    !invalidPathRejected ||
                    defined.TypeName != "Xform" ||
                    !defined.GetAttributeNames().Contains("custom:value", StringComparer.Ordinal) ||
                    !defined.GetRelationshipNames().Contains("custom:link", StringComparer.Ordinal) ||
                    !childPaths.Contains("/World/Over", StringComparer.Ordinal))
                {
                    Console.Error.WriteLine("Scene inspection did not round-trip or report errors.");
                    return 26;
                }
            }

            // Property wrappers, value state, bulk samples, and tagged scalar reads.
            string propertyPath = Path.Combine(directory, "property-model-authored.usda");
            File.Delete(propertyPath);
            using (UsdStage propertyStage = UsdStage.Create(propertyPath))
            {
                UsdPrim prim = propertyStage.DefinePrim("/World/Properties", "Xform");
                propertyStage.DefinePrim("/World/Target", "Xform");
                prim.SetBool("custom:enabled", true);
                prim.SetInt64("custom:count", 42);
                prim.SetDouble("custom:number", 3.5);
                prim.SetDouble("custom:number", 4.5, timeCode: 1);
                prim.SetDouble("custom:number", 5.5, timeCode: 2);
                prim.SetString("custom:label", "hello");
                prim.SetToken("custom:kind", "Beacon");
                prim.SetVec3f("custom:vector", new UsdVec3f(1, 2, 3));
                prim.SetColor3f("custom:color", new UsdVec3f(0.25f, 0.5f, 0.75f));
                prim.SetDoubleArray("custom:array", [1, 2]);
                prim.SetDouble("custom:blockable", 9);
                prim.CreateRelationship("custom:link");
                UsdRelationship relationship = prim.GetRelationship("custom:link");
                relationship.SetTargets(["/World/Target"]);

                UsdAttribute number = prim.GetAttribute("custom:number");
                UsdAttributeValueState numberState = number.GetValueState();
                UsdScalarValue numberValue = number.GetValue(timeCode: 2);
                UsdScalarValue boolValue = prim.GetAttribute("custom:enabled").GetValue();
                UsdScalarValue countValue = prim.GetAttribute("custom:count").GetValue();
                UsdScalarValue labelValue = prim.GetAttribute("custom:label").GetValue();
                UsdScalarValue tokenValue = prim.GetAttribute("custom:kind").GetValue();
                UsdScalarValue vectorValue = prim.GetAttribute("custom:vector").GetValue();
                UsdScalarValue colorValue = prim.GetAttribute("custom:color").GetValue();
                UsdScalarValue arrayValue = prim.GetAttribute("custom:array").GetValue();

                bool wrongTagRejected;
                try
                {
                    _ = numberValue.BoolValue;
                    wrongTagRejected = false;
                }
                catch (InvalidOperationException)
                {
                    wrongTagRejected = true;
                }

                bool typedMismatchRejected;
                try
                {
                    _ = prim.GetBool("custom:number");
                    typedMismatchRejected = false;
                }
                catch (OpenUsdNativeException)
                {
                    typedMismatchRejected = true;
                }
                bool roleGetterMismatchRejected;
                try
                {
                    _ = prim.GetVec3f("custom:color");
                    roleGetterMismatchRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.InvalidArgument)
                {
                    roleGetterMismatchRejected = true;
                }
                bool roleSetterMismatchRejected;
                try
                {
                    prim.SetColor3f("custom:vector", new UsdVec3f(1, 1, 1));
                    roleSetterMismatchRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.InvalidArgument)
                {
                    roleSetterMismatchRejected = true;
                }

                bool missingAttributeRejected;
                try
                {
                    _ = prim.GetAttribute("missing").GetValue();
                    missingAttributeRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.NotFound)
                {
                    missingAttributeRejected = true;
                }

                UsdAttribute blockable = prim.GetAttribute("custom:blockable");
                blockable.BlockValue();
                UsdAttributeValueState blockedState = blockable.GetValueState();
                bool blockedValueRejected;
                try
                {
                    _ = blockable.GetValue();
                    blockedValueRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.NotFound)
                {
                    blockedValueRejected = true;
                }
                bool blockedTypedValueRejected;
                try
                {
                    _ = prim.GetDouble("custom:blockable");
                    blockedTypedValueRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.NotFound)
                {
                    blockedTypedValueRejected = true;
                }
                blockable.ClearValue();
                UsdAttributeValueState clearedState = blockable.GetValueState();
                bool unvaluedTypedValueRejected;
                try
                {
                    _ = prim.GetDouble("custom:blockable");
                    unvaluedTypedValueRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.NotFound)
                {
                    unvaluedTypedValueRejected = true;
                }
                UsdScalarValue invalidValue = default;
                bool invalidScalarRejected;
                try
                {
                    _ = invalidValue.DoubleValue;
                    invalidScalarRejected = false;
                }
                catch (InvalidOperationException)
                {
                    invalidScalarRejected = true;
                }

                if (number.TypeName != "double" ||
                    !numberState.HasAuthoredValueOpinion ||
                    numberState.IsBlocked ||
                    !number.GetTimeSamples().SequenceEqual([1.0, 2.0]) ||
                    numberValue.Kind != UsdScalarKind.Number ||
                    numberValue.DoubleValue != 5.5 ||
                    boolValue.Kind != UsdScalarKind.Boolean ||
                    !boolValue.BoolValue ||
                    countValue.Kind != UsdScalarKind.Signed64 ||
                    countValue.Int64Value != 42 ||
                    labelValue.Kind != UsdScalarKind.Text ||
                    labelValue.StringValue != "hello" ||
                    tokenValue.Kind != UsdScalarKind.Token ||
                    tokenValue.TokenValue != "Beacon" ||
                    vectorValue.Kind != UsdScalarKind.Vector3 ||
                    vectorValue.Vec3fValue != new UsdVec3f(1, 2, 3) ||
                    colorValue.Kind != UsdScalarKind.Color3 ||
                    colorValue.Color3fValue != new UsdVec3f(0.25f, 0.5f, 0.75f) ||
                    arrayValue.Kind != UsdScalarKind.DoubleArray ||
                    !arrayValue.DoubleArrayValue.SequenceEqual([1.0, 2.0]) ||
                    !wrongTagRejected ||
                    !typedMismatchRejected ||
                    !roleGetterMismatchRejected ||
                    !roleSetterMismatchRejected ||
                    !missingAttributeRejected ||
                    !blockedState.HasAuthoredValueOpinion ||
                    !blockedState.IsBlocked ||
                    !blockedValueRejected ||
                    !blockedTypedValueRejected ||
                    !unvaluedTypedValueRejected ||
                    invalidValue.Kind != UsdScalarKind.Invalid ||
                    !invalidScalarRejected ||
                    clearedState.HasAuthoredValueOpinion ||
                    clearedState.IsBlocked ||
                    !prim.GetAttributes().Any(static attribute => attribute.Name == "custom:number") ||
                    !prim.GetRelationships().Any(static item => item.Name == "custom:link") ||
                    !relationship.GetTargets().SequenceEqual(["/World/Target"]))
                {
                    Console.Error.WriteLine("Managed property model failed.");
                    return 27;
                }
                propertyStage.Save();
            }

            using (UsdStage propertyStage = UsdStage.Open(propertyPath))
            {
                UsdPrim prim = propertyStage.GetPrim("/World/Properties");
                UsdAttribute number = prim.GetAttribute("custom:number");
                if (number.TypeName != "double" ||
                    !number.GetTimeSamples().SequenceEqual([1.0, 2.0]) ||
                    number.GetValue(timeCode: 1).DoubleValue != 4.5 ||
                    prim.GetAttribute("custom:label").GetValue().StringValue != "hello" ||
                    !prim.GetRelationship("custom:link").GetTargets().SequenceEqual(["/World/Target"]))
                {
                    Console.Error.WriteLine("Property model did not round-trip.");
                    return 28;
                }
            }

            // Contiguous geometry values: mesh topology, points, UVs, weights, and transforms.
            string geometryPath = Path.Combine(directory, "geometry-values-authored.usda");
            File.Delete(geometryPath);
            int[] faceCounts = [4];
            int[] faceIndices = [0, 1, 2, 3];
            UsdVec3f[] points =
            [
                new(-1, -1, 0),
                new(1, -1, 0),
                new(1, 1, 0),
                new(-1, 1, 0)
            ];
            UsdVec3f[] sampledPoints =
            [
                new(-2, -1, 0),
                new(2, -1, 0),
                new(2, 1, 0),
                new(-2, 1, 0)
            ];
            UsdVec2f[] uvs =
            [
                new(0, 0),
                new(1, 0),
                new(1, 1),
                new(0, 1)
            ];
            float[] largeWeights = Enumerable.Range(0, 65_536)
                .Select(static value => value * 0.5f)
                .ToArray();
            UsdMatrix4d transform = UsdMatrix4d.CreateTranslation(10, 20, 30);

            using (UsdStage geometryStage = UsdStage.Create(geometryPath))
            {
                UsdPrim mesh = geometryStage.DefinePrim("/World/Mesh", "Xform");
                mesh.SetInt32Array("faceVertexCounts", faceCounts);
                mesh.SetInt32Array("faceVertexIndices", faceIndices);
                mesh.SetVec3fArray("points", points);
                mesh.SetVec3fArray("points", sampledPoints, timeCode: 10);
                mesh.SetVec2fArray("custom:uvs", uvs);
                mesh.SetVec2fArray("custom:emptyUvs", []);
                mesh.SetFloatArray("custom:weights", largeWeights);
                mesh.SetMatrix4d("xformOp:transform", transform);

                if (!mesh.GetInt32Array("faceVertexCounts").SequenceEqual(faceCounts) ||
                    !mesh.GetInt32Array("faceVertexIndices").SequenceEqual(faceIndices) ||
                    !mesh.GetVec3fArray("points", 10).SequenceEqual(sampledPoints) ||
                    !mesh.GetVec2fArray("custom:uvs").SequenceEqual(uvs) ||
                    mesh.GetVec2fArray("custom:emptyUvs").Length != 0 ||
                    !mesh.GetFloatArray("custom:weights").SequenceEqual(largeWeights) ||
                    mesh.GetMatrix4d("xformOp:transform") != transform ||
                    transform.ExtractTranslation() != new UsdVec3d(10, 20, 30) ||
                    transform.TransformPoint(new UsdVec3d(1, 2, 3)) !=
                        new UsdVec3d(11, 22, 33))
                {
                    Console.Error.WriteLine("Geometry values did not round-trip before save.");
                    return 45;
                }

                bool mismatchRejected;
                try
                {
                    _ = mesh.GetFloatArray("faceVertexCounts");
                    mismatchRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.InvalidArgument)
                {
                    mismatchRejected = true;
                }

                UsdScalarValue genericCounts = mesh.GetAttribute("faceVertexCounts").GetValue();
                UsdScalarValue genericPoints = mesh.GetAttribute("points").GetValue(timeCode: 10);
                UsdScalarValue genericUvs = mesh.GetAttribute("custom:uvs").GetValue();
                UsdScalarValue genericWeights = mesh.GetAttribute("custom:weights").GetValue();
                UsdScalarValue genericTransform = mesh.GetAttribute("xformOp:transform").GetValue();
                if (!mismatchRejected ||
                    genericCounts.Kind != UsdScalarKind.Int32Array ||
                    !genericCounts.Int32ArrayValue.SequenceEqual(faceCounts) ||
                    genericPoints.Kind != UsdScalarKind.Vec3fArray ||
                    !genericPoints.Vec3fArrayValue.SequenceEqual(sampledPoints) ||
                    genericUvs.Kind != UsdScalarKind.Vec2fArray ||
                    !genericUvs.Vec2fArrayValue.SequenceEqual(uvs) ||
                    genericWeights.Kind != UsdScalarKind.FloatArray ||
                    !genericWeights.FloatArrayValue.SequenceEqual(largeWeights) ||
                    genericTransform.Kind != UsdScalarKind.Matrix4d ||
                    genericTransform.Matrix4dValue != transform)
                {
                    Console.Error.WriteLine("Tagged geometry values or mismatch errors failed.");
                    return 46;
                }
                geometryStage.Save();
            }

            using (UsdStage geometryStage = UsdStage.Open(geometryPath))
            {
                UsdPrim mesh = geometryStage.GetPrim("/World/Mesh");
                if (!mesh.GetInt32Array("faceVertexCounts").SequenceEqual(faceCounts) ||
                    !mesh.GetVec3fArray("points").SequenceEqual(points) ||
                    !mesh.GetVec3fArray("points", 10).SequenceEqual(sampledPoints) ||
                    !mesh.GetVec2fArray("custom:uvs").SequenceEqual(uvs) ||
                    mesh.GetFloatArray("custom:weights").Length != largeWeights.Length ||
                    mesh.GetMatrix4d("xformOp:transform") != transform)
                {
                    Console.Error.WriteLine("Saved geometry values did not round-trip.");
                    return 47;
                }
            }
            Console.WriteLine("Geometry values passed.");

            string usdGeomPath = Path.Combine(directory, "usdgeom-authored.usda");
            File.Delete(usdGeomPath);
            UsdVec3f[] geomPoints =
            [
                new(-1, -1, 0),
                new(1, -1, 0),
                new(1, 1, 0),
                new(-1, 1, 0)
            ];
            UsdVec3f[] sampledGeomPoints =
            [
                new(-2, -1, 0),
                new(2, -1, 0),
                new(2, 1, 0),
                new(-2, 1, 0)
            ];
            UsdVec3f[] geomNormals =
            [
                new(0, 0, 1),
                new(0, 0, 1),
                new(0, 0, 1),
                new(0, 0, 1)
            ];
            UsdMatrix4d xformValue = UsdMatrix4d.CreateTranslation(5, 6, 7);
            UsdMatrix4d cameraTransform = UsdMatrix4d.CreateTranslation(0, 2, 10);

            using (UsdStage geomStage = UsdStage.Create(usdGeomPath))
            {
                UsdGeomXform world = geomStage.DefineXform("/World");
                UsdGeomMesh mesh = geomStage.DefineMesh("/World/Mesh");
                UsdGeomCamera camera = geomStage.DefineCamera("/World/Camera");

                world.Xformable.SetLocalTransform(xformValue);
                world.Xformable.SetResetXformStack(true);
                world.Imageable.SetPurpose(UsdGeomPurpose.Render);
                mesh.Imageable.SetVisibility(UsdGeomVisibility.Invisible);
                mesh.SetTopology([4], [0, 1, 2, 3]);
                mesh.SetPoints(geomPoints);
                mesh.SetPoints(sampledGeomPoints, timeCode: 10);
                mesh.SetNormals([new UsdVec3f(0, 0, 1)], UsdGeomInterpolation.Constant);
                mesh.SetNormals([new UsdVec3f(0, 0, 1)], UsdGeomInterpolation.Uniform);
                mesh.SetNormals(geomNormals, UsdGeomInterpolation.Varying);
                mesh.SetNormals(geomNormals, UsdGeomInterpolation.FaceVarying);
                mesh.SetNormals(geomNormals, UsdGeomInterpolation.Vertex);
                mesh.SubdivisionScheme = UsdGeomSubdivisionScheme.None;
                mesh.Orientation = UsdGeomOrientation.LeftHanded;
                mesh.DoubleSided = true;
                mesh.SetExtent(new UsdExtent3f(
                    new UsdVec3f(-2, -1, 0),
                    new UsdVec3f(2, 1, 0)));

                camera.Projection = UsdGeomCameraProjection.Orthographic;
                camera.FocalLength = 0;
                camera.HorizontalAperture = 24;
                camera.VerticalAperture = 18;
                camera.ClippingRange = new UsdVec2f(0.1f, 1000);
                camera.SetTransform(cameraTransform);

                camera.Projection = UsdGeomCameraProjection.Perspective;
                bool perspectiveZeroFocalRejected;
                try
                {
                    camera.FocalLength = 0;
                    perspectiveZeroFocalRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.InvalidArgument)
                {
                    perspectiveZeroFocalRejected = true;
                }
                camera.Projection = UsdGeomCameraProjection.Orthographic;
                bool negativeFocalRejected;
                try
                {
                    camera.FocalLength = -1;
                    negativeFocalRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.InvalidArgument)
                {
                    negativeFocalRejected = true;
                }

                bool wrongSchemaRejected = !UsdGeomMesh.TryWrap(world.Prim, out _);
                bool wrapRejected;
                try
                {
                    _ = UsdGeomMesh.Wrap(world.Prim);
                    wrapRejected = false;
                }
                catch (ArgumentException)
                {
                    wrapRejected = true;
                }

                bool malformedTopologyRejected;
                try
                {
                    mesh.SetTopology([3], [0, 1, 2, 3]);
                    malformedTopologyRejected = false;
                }
                catch (ArgumentException)
                {
                    malformedTopologyRejected = true;
                }
                bool normalCardinalityRejected;
                try
                {
                    mesh.SetNormals(
                        [new UsdVec3f(0, 0, 1)],
                        UsdGeomInterpolation.Vertex,
                        timeCode: 10);
                    normalCardinalityRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.InvalidArgument)
                {
                    normalCardinalityRejected = true;
                }
                bool arrayRoleMismatchRejected;
                try
                {
                    _ = mesh.Prim.GetVec3fArray("points");
                    arrayRoleMismatchRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.InvalidArgument)
                {
                    arrayRoleMismatchRejected = true;
                }

                if (!wrongSchemaRejected ||
                    !wrapRejected ||
                    !perspectiveZeroFocalRejected ||
                    !negativeFocalRejected ||
                    !malformedTopologyRejected ||
                    !normalCardinalityRejected ||
                    !arrayRoleMismatchRejected ||
                    world.Xformable.GetLocalTransform() != xformValue ||
                    !world.Xformable.GetResetXformStack() ||
                    mesh.Imageable.GetVisibility() != UsdGeomVisibility.Invisible ||
                    mesh.Imageable.GetPurpose() != UsdGeomPurpose.Render ||
                    !mesh.GetFaceVertexCounts().SequenceEqual([4]) ||
                    !mesh.GetFaceVertexIndices().SequenceEqual([0, 1, 2, 3]) ||
                    !mesh.GetPoints(timeCode: 10).SequenceEqual(sampledGeomPoints) ||
                    !mesh.GetNormals().SequenceEqual(geomNormals) ||
                    mesh.NormalsInterpolation != UsdGeomInterpolation.Vertex ||
                    mesh.SubdivisionScheme != UsdGeomSubdivisionScheme.None ||
                    mesh.Orientation != UsdGeomOrientation.LeftHanded ||
                    !mesh.DoubleSided ||
                    mesh.GetExtent().Maximum != new UsdVec3f(2, 1, 0) ||
                    mesh.Prim.GetAttribute("points").TypeName != "point3f[]" ||
                    mesh.Prim.GetAttribute("normals").TypeName != "normal3f[]" ||
                    camera.Projection != UsdGeomCameraProjection.Orthographic ||
                    camera.FocalLength != 0 ||
                    camera.HorizontalAperture != 24 ||
                    camera.VerticalAperture != 18 ||
                    camera.ClippingRange != new UsdVec2f(0.1f, 1000) ||
                    camera.GetTransform() != cameraTransform)
                {
                    Console.Error.WriteLine("Managed UsdGeom facade failed before save.");
                    return 48;
                }
                geomStage.Save();
            }

            using (UsdStage geomStage = UsdStage.Open(usdGeomPath))
            {
                UsdGeomMesh mesh = UsdGeomMesh.Wrap(geomStage.GetPrim("/World/Mesh"));
                UsdGeomCamera camera = UsdGeomCamera.Wrap(geomStage.GetPrim("/World/Camera"));
                if (!mesh.GetPoints().SequenceEqual(geomPoints) ||
                    !mesh.GetPoints(timeCode: 10).SequenceEqual(sampledGeomPoints) ||
                    mesh.Imageable.GetPurpose() != UsdGeomPurpose.Render ||
                    camera.Projection != UsdGeomCameraProjection.Orthographic ||
                    camera.FocalLength != 0 ||
                    camera.GetTransform() != cameraTransform)
                {
                    Console.Error.WriteLine("Saved managed UsdGeom facade did not round-trip.");
                    return 49;
                }
            }
            Console.WriteLine("UsdGeom facade passed.");
            WorldTransformProbe.Run(directory);
            Console.WriteLine("World transforms passed.");
            string cameraStatePath = Path.Combine(directory, "camera-state-authored.usda");
            File.WriteAllText(
                cameraStatePath,
                """
                #usda 1.0

                def Camera "Camera"
                {
                    float2 clippingRange = (0.1, 1000)
                    float2 clippingRange.timeSamples = {
                        0: (0.1, 1000),
                        10: (-5, 250),
                    }
                    float fStop = 2.8
                    float fStop.timeSamples = {
                        0: 2.8,
                        10: 5.6,
                    }
                    float focalLength = 50
                    float focalLength.timeSamples = {
                        0: 50,
                        10: 0,
                    }
                    float focusDistance = 10
                    float focusDistance.timeSamples = {
                        0: 10,
                        10: 25,
                    }
                    float horizontalAperture = 24
                    float horizontalAperture.timeSamples = {
                        0: 24,
                        10: 40,
                    }
                    float horizontalApertureOffset = 2
                    float horizontalApertureOffset.timeSamples = {
                        0: 2,
                        10: 4,
                    }
                    token projection = "perspective"
                    token projection.timeSamples = {
                        0: "perspective",
                        10: "orthographic",
                    }
                    float verticalAperture = 18
                    float verticalAperture.timeSamples = {
                        0: 18,
                        10: 20,
                    }
                    float verticalApertureOffset = -1
                    float verticalApertureOffset.timeSamples = {
                        0: -1,
                        10: -2,
                    }
                }
                """);
            using (UsdStage cameraStateStage = UsdStage.Open(cameraStatePath))
            {
                UsdGeomCamera camera = UsdGeomCamera.Wrap(
                    cameraStateStage.GetPrim("/Camera"));
                UsdGeomCameraState defaultState = camera.GetState();
                UsdGeomCameraState first = camera.GetState(0);
                UsdGeomCameraState second = camera.GetState(10);
                if (defaultState != first ||
                    first.Projection != UsdGeomCameraProjection.Perspective ||
                    Math.Abs(first.WindowLeft - -0.2d) > 1e-12d ||
                    Math.Abs(first.WindowRight - 0.28d) > 1e-12d ||
                    Math.Abs(first.WindowBottom - -0.2d) > 1e-12d ||
                    Math.Abs(first.WindowTop - 0.16d) > 1e-12d ||
                    second.Projection != UsdGeomCameraProjection.Orthographic ||
                    Math.Abs(second.WindowLeft - -1.6d) > 1e-12d ||
                    Math.Abs(second.WindowRight - 2.4d) > 1e-12d ||
                    Math.Abs(second.WindowBottom - -1.2d) > 1e-12d ||
                    Math.Abs(second.WindowTop - 0.8d) > 1e-12d ||
                    second.ClippingNear != -5d ||
                    second.ClippingFar != 250d ||
                    second.FocalLength != 0d ||
                    second.HorizontalApertureOffset != 4d ||
                    second.VerticalApertureOffset != -2d ||
                    second.FocusDistance != 25d ||
                    Math.Abs(second.FStop - 5.6d) > 1e-6d)
                {
                    Console.Error.WriteLine(
                        "Time-sampled camera state did not match authored Gf optics.");
                    return 114;
                }
            }
            File.Delete(cameraStatePath);
            Console.WriteLine("Camera states passed.");
            RunWorldBoundsProbe(directory);
            Console.WriteLine("World bounds passed.");
            CompositionEnumerationProbe.Run(directory);
            Console.WriteLine("Composition enumeration passed.");
            string usdShadePath = Path.Combine(directory, "usdshade-authored.usda");
            File.Delete(usdShadePath);
            var textureAsset = new UsdAssetPath("textures/albédo.png");
            using (UsdStage shadeStage = UsdStage.Create(usdShadePath))
            {
                UsdGeomMesh mesh = shadeStage.DefineMesh("/World/Mesh");
                mesh.SetTopology([3], [0, 1, 2]);
                mesh.SetPoints([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)]);

                UsdPreviewSurface preview = UsdPreviewSurface.Create(
                    shadeStage,
                    "/World/Looks/Material",
                    "/World/Looks/Material/PreviewSurface");
                preview.SetDiffuseColor(new UsdVec3f(0.2f, 0.4f, 0.8f));
                preview.SetEmissiveColor(new UsdVec3f(0.01f, 0.02f, 0.03f));
                preview.SetMetallic(0.25f);
                preview.SetRoughness(0.6f);
                preview.SetOpacity(0.9f);
                preview.SetOpacityThreshold(0.1f);
                preview.SetNormal(new UsdVec3f(0, 0, 1));
                preview.SetDisplacement(0.05f);

                UsdUvTexture texture = UsdUvTexture.Create(
                    shadeStage,
                    "/World/Looks/Material/Texture",
                    textureAsset);
                preview.ConnectDiffuseColor(texture.Rgb);
                preview.Material.Bind(mesh.Prim);

                bool wrongSchemaRejected =
                    !UsdShadeMaterial.TryWrap(mesh.Prim, out _);
                bool wrapRejected;
                try
                {
                    _ = UsdShadeShader.Wrap(mesh.Prim);
                    wrapRejected = false;
                }
                catch (ArgumentException)
                {
                    wrapRejected = true;
                }

                bool typeMismatchRejected;
                try
                {
                    preview.Shader.CreateInputToken("roughness");
                    typeMismatchRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.InvalidArgument)
                {
                    typeMismatchRejected = true;
                }

                bool connectionMismatchRejected;
                try
                {
                    preview.Shader.GetInput("roughness").ConnectToSource(texture.Rgb);
                    connectionMismatchRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.InvalidArgument)
                {
                    connectionMismatchRejected = true;
                }

                UsdShadeConnection diffuseConnection =
                    preview.Shader.GetInput("diffuseColor").GetConnectedSource();
                UsdShadeConnection surfaceConnection =
                    preview.Material.GetSurfaceOutput().GetConnectedSource();
                if (!wrongSchemaRejected ||
                    !wrapRejected ||
                    !typeMismatchRejected ||
                    !connectionMismatchRejected ||
                    preview.Shader.SourceId != "UsdPreviewSurface" ||
                    texture.Shader.SourceId != "UsdUVTexture" ||
                    texture.Rgb.ValueType != UsdShadeValueType.Float3 ||
                    texture.File.GetAssetPath() != textureAsset ||
                    preview.Shader.GetInput("roughness").GetFloat() != 0.6f ||
                    diffuseConnection.SourcePrimPath != texture.Shader.Path ||
                    diffuseConnection.SourceName != "rgb" ||
                    diffuseConnection.SourceType != UsdShadeAttributeType.Output ||
                    surfaceConnection.SourcePrimPath != preview.Shader.Path ||
                    surfaceConnection.SourceName != "surface" ||
                    shadeStage.GetDirectlyBoundMaterial(mesh.Prim).Path != preview.Material.Path)
                {
                    Console.Error.WriteLine("Managed UsdShade facade failed before save.");
                    return 50;
                }

                preview.Material.Unbind(mesh.Prim);
                bool unboundRejected;
                try
                {
                    _ = shadeStage.GetDirectlyBoundMaterial(mesh.Prim);
                    unboundRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.NotFound)
                {
                    unboundRejected = true;
                }
                if (!unboundRejected)
                {
                    Console.Error.WriteLine("Direct material unbind did not remove the binding.");
                    return 51;
                }
                preview.Material.Bind(mesh.Prim);
                shadeStage.Save();
            }

            using (UsdStage shadeStage = UsdStage.Open(usdShadePath))
            {
                UsdGeomMesh mesh = UsdGeomMesh.Wrap(shadeStage.GetPrim("/World/Mesh"));
                UsdShadeMaterial material =
                    UsdShadeMaterial.Wrap(shadeStage.GetPrim("/World/Looks/Material"));
                UsdShadeShader previewShader =
                    UsdShadeShader.Wrap(
                        shadeStage.GetPrim("/World/Looks/Material/PreviewSurface"));
                UsdShadeShader textureShader =
                    UsdShadeShader.Wrap(shadeStage.GetPrim("/World/Looks/Material/Texture"));
                UsdShadeConnection connection =
                    previewShader.GetInput("diffuseColor").GetConnectedSource();
                if (previewShader.SourceId != "UsdPreviewSurface" ||
                    textureShader.SourceId != "UsdUVTexture" ||
                    textureShader.GetInput("file").GetAssetPath() != textureAsset ||
                    connection.SourcePrimPath != textureShader.Path ||
                    material.GetSurfaceOutput().GetConnectedSource().SourcePrimPath !=
                        previewShader.Path ||
                    shadeStage.GetDirectlyBoundMaterial(mesh.Prim).Path != material.Path)
                {
                    Console.Error.WriteLine("Saved managed UsdShade facade did not round-trip.");
                    return 52;
                }
            }
            Console.WriteLine("UsdShade facade passed.");

            string usdLuxPath = Path.Combine(directory, "usdlux-authored.usda");
            File.Delete(usdLuxPath);
            UsdMatrix4d lightTransform = UsdMatrix4d.CreateTranslation(3, 4, 5);
            var domeTexture = new UsdAssetPath("textures/stúdio.hdr");
            using (UsdStage luxStage = UsdStage.Create(usdLuxPath))
            {
                UsdLuxDistantLight distant =
                    luxStage.DefineDistantLight("/World/Lights/Sun");
                UsdLuxSphereLight sphere =
                    luxStage.DefineSphereLight("/World/Lights/Bulb");
                UsdLuxRectLight rect =
                    luxStage.DefineRectLight("/World/Lights/Panel");
                UsdLuxDiskLight disk =
                    luxStage.DefineDiskLight("/World/Lights/Disk");
                UsdLuxDomeLight dome =
                    luxStage.DefineDomeLight("/World/Lights/Environment");
                UsdLuxCylinderLight cylinder =
                    luxStage.DefineCylinderLight("/World/Lights/Tube");

                distant.Light.Intensity = 4.5f;
                distant.Light.Exposure = 2;
                distant.Light.Color = new UsdVec3f(1, 0.8f, 0.6f);
                distant.Light.EnableColorTemperature = true;
                distant.Light.ColorTemperature = 5500;
                distant.Light.Diffuse = 0.75f;
                distant.Light.Specular = 0.5f;
                distant.Angle = 0.75f;

                sphere.Light.Normalize = true;
                sphere.Radius = 0.25f;
                UsdLuxShaping shaping = sphere.Light.ApplyShaping();
                shaping.Focus = 2.5f;
                shaping.ConeAngle = 35;
                shaping.ConeSoftness = 0.2f;

                rect.Width = 3;
                rect.Height = 2;
                rect.TextureFile = new UsdAssetPath("textures/panel.exr");
                rect.Xformable.SetLocalTransform(lightTransform);
                disk.Radius = 1.25f;
                dome.TextureFile = domeTexture;
                cylinder.Radius = 0.1f;
                cylinder.Length = 2.5f;

                bool wrongSchemaRejected =
                    !UsdLuxDistantLight.TryWrap(sphere.Prim, out _);
                bool wrapRejected;
                try
                {
                    _ = UsdLuxDomeLight.Wrap(rect.Prim);
                    wrapRejected = false;
                }
                catch (ArgumentException)
                {
                    wrapRejected = true;
                }

                bool missingRejected;
                try
                {
                    _ = UsdLuxDistantLight.Wrap(luxStage.GetPrim("/World/Lights/Missing"));
                    missingRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.NotFound)
                {
                    missingRejected = true;
                }

                bool invalidPathRejected;
                try
                {
                    _ = luxStage.DefineSphereLight("relative/light");
                    invalidPathRejected = false;
                }
                catch (ArgumentException)
                {
                    invalidPathRejected = true;
                }

                bool nonFiniteRejected;
                try
                {
                    distant.Light.Intensity = float.NaN;
                    nonFiniteRejected = false;
                }
                catch (ArgumentOutOfRangeException)
                {
                    nonFiniteRejected = true;
                }
                bool domainRejected;
                try
                {
                    sphere.Radius = -1;
                    domainRejected = false;
                }
                catch (ArgumentOutOfRangeException)
                {
                    domainRejected = true;
                }
                try
                {
                    distant.Light.ColorTemperature = 999;
                    domainRejected = false;
                }
                catch (ArgumentOutOfRangeException) when (domainRejected)
                {
                }
                try
                {
                    shaping.ConeSoftness = 1.01f;
                    domainRejected = false;
                }
                catch (ArgumentOutOfRangeException) when (domainRejected)
                {
                }
                try
                {
                    distant.Angle = 360;
                    domainRejected = false;
                }
                catch (ArgumentOutOfRangeException) when (domainRejected)
                {
                }

                bool shapingRequiresApply;
                try
                {
                    _ = rect.Light.GetShaping();
                    shapingRequiresApply = false;
                }
                catch (InvalidOperationException)
                {
                    shapingRequiresApply = true;
                }

                if (!wrongSchemaRejected ||
                    !wrapRejected ||
                    !missingRejected ||
                    !invalidPathRejected ||
                    !nonFiniteRejected ||
                    !domainRejected ||
                    !shapingRequiresApply ||
                    distant.Light.Intensity != 4.5f ||
                    distant.Light.Color != new UsdVec3f(1, 0.8f, 0.6f) ||
                    distant.Angle != 0.75f ||
                    !sphere.Light.Normalize ||
                    sphere.Radius != 0.25f ||
                    !sphere.Light.HasShaping ||
                    shaping.Focus != 2.5f ||
                    shaping.ConeAngle != 35 ||
                    shaping.ConeSoftness != 0.2f ||
                    rect.Width != 3 ||
                    rect.Height != 2 ||
                    rect.Xformable.GetLocalTransform() != lightTransform ||
                    rect.TextureFile.Path != "textures/panel.exr" ||
                    disk.Radius != 1.25f ||
                    dome.TextureFile != domeTexture ||
                    cylinder.Radius != 0.1f ||
                    cylinder.Length != 2.5f)
                {
                    Console.Error.WriteLine("Managed UsdLux facade failed before save.");
                    return 53;
                }
                luxStage.Save();
            }

            using (UsdStage luxStage = UsdStage.Open(usdLuxPath))
            {
                UsdLuxDistantLight distant =
                    UsdLuxDistantLight.Wrap(luxStage.GetPrim("/World/Lights/Sun"));
                UsdLuxSphereLight sphere =
                    UsdLuxSphereLight.Wrap(luxStage.GetPrim("/World/Lights/Bulb"));
                UsdLuxRectLight rect =
                    UsdLuxRectLight.Wrap(luxStage.GetPrim("/World/Lights/Panel"));
                UsdLuxDomeLight dome =
                    UsdLuxDomeLight.Wrap(luxStage.GetPrim("/World/Lights/Environment"));
                if (distant.Light.Intensity != 4.5f ||
                    distant.Light.Exposure != 2 ||
                    distant.Light.ColorTemperature != 5500 ||
                    distant.Angle != 0.75f ||
                    sphere.Radius != 0.25f ||
                    sphere.Light.GetShaping().ConeAngle != 35 ||
                    rect.Width != 3 ||
                    rect.Height != 2 ||
                    rect.Xformable.GetLocalTransform() != lightTransform ||
                    rect.TextureFile.Path != "textures/panel.exr" ||
                    dome.TextureFile != domeTexture)
                {
                    Console.Error.WriteLine("Saved managed UsdLux facade did not round-trip.");
                    return 54;
                }
            }
            Console.WriteLine("UsdLux facade passed.");

            string usdSkelPath = Path.Combine(directory, "usdskel-authored.usda");
            File.Delete(usdSkelPath);
            string[] skelJoints = ["Root", "Root/Arm"];
            UsdMatrix4d armTransform = UsdMatrix4d.CreateTranslation(0, 1, 0);
            UsdMatrix4d[] bindTransforms = [UsdMatrix4d.Identity, armTransform];
            UsdMatrix4d[] restTransforms = [UsdMatrix4d.Identity, armTransform];
            UsdVec3f[] defaultTranslations = [new(0, 0, 0), new(0, 1, 0)];
            UsdVec3f[] sampledTranslations = [new(0, 0, 0), new(0, 2, 0)];
            UsdQuatf[] defaultRotations = [UsdQuatf.Identity, UsdQuatf.Identity];
            UsdQuatf[] sampledRotations =
            [
                UsdQuatf.Identity,
                new UsdQuatf(0.70710677f, 0, 0, 0.70710677f)
            ];
            UsdVec3f[] skelScales = [new(1, 1, 1), new(1, 1, 1)];
            int[] jointIndices = [0, 1, 0, 1, 0, 1];
            float[] jointWeights = [1, 0, 0.5f, 0.5f, 0, 1];
            using (UsdStage skelStage = UsdStage.Create(usdSkelPath))
            {
                UsdSkelRoot skelRoot = skelStage.DefineSkelRoot("/World/Character");
                UsdSkelSkeleton skeleton =
                    skelStage.DefineSkeleton("/World/Character/Skeleton");
                UsdSkelAnimation animation =
                    skelStage.DefineAnimation("/World/Character/Animation");
                UsdGeomMesh skinnedMesh =
                    skelStage.DefineMesh("/World/Character/Mesh");
                skinnedMesh.SetTopology([3], [0, 1, 2]);
                skinnedMesh.SetPoints(
                    [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)]);

                skeleton.SetJoints(skelJoints);
                skeleton.SetBindTransforms(bindTransforms);
                skeleton.SetRestTransforms(restTransforms);
                animation.SetJoints(skelJoints);
                animation.SetTranslations(defaultTranslations);
                animation.SetTranslations(sampledTranslations, 10);
                animation.SetRotations(defaultRotations);
                animation.SetRotations(sampledRotations, 10);
                animation.SetScales(skelScales);
                animation.SetScales(skelScales, 10);

                UsdSkelBinding rootBinding = skelRoot.ApplyBinding();
                rootBinding.SetSkeleton(skeleton);
                UsdSkelBinding skeletonBinding = skeleton.ApplyBinding();
                skeletonBinding.SetAnimationSource(animation);
                UsdSkelBinding meshBinding = UsdSkelBinding.Apply(skinnedMesh.Prim);
                meshBinding.GeomBindTransform = UsdMatrix4d.Identity;
                meshBinding.SetJointInfluences(
                    jointIndices,
                    jointWeights,
                    elementSize: 2,
                    UsdSkelInterpolation.Vertex);

                bool wrongSchemaRejected =
                    !UsdSkelSkeleton.TryWrap(animation.Prim, out _);
                bool wrapRejected;
                try
                {
                    _ = UsdSkelAnimation.Wrap(skeleton.Prim);
                    wrapRejected = false;
                }
                catch (ArgumentException)
                {
                    wrapRejected = true;
                }

                bool missingRejected;
                try
                {
                    _ = UsdSkelSkeleton.Wrap(
                        skelStage.GetPrim("/World/Character/Missing"));
                    missingRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.NotFound)
                {
                    missingRejected = true;
                }

                bool orderingRejected;
                try
                {
                    skeleton.SetJoints(["Root/Arm", "Root"]);
                    orderingRejected = false;
                }
                catch (ArgumentException)
                {
                    orderingRejected = true;
                }

                bool lowLevelPathRejected;
                try
                {
                    stage.DefineSkel(
                        "World/Skeleton",
                        OpenUsdNativeSkelSchemaKind.Skeleton);
                    lowLevelPathRejected = false;
                }
                catch (ArgumentException)
                {
                    lowLevelPathRejected = true;
                }

                bool lowLevelRotationPathRejected;
                try
                {
                    _ = stage.GetSkelAnimationRotations(
                        "/World/Skeleton.rotations");
                    lowLevelRotationPathRejected = false;
                }
                catch (ArgumentException)
                {
                    lowLevelRotationPathRejected = true;
                }

                bool lowLevelRelationshipRejected;
                try
                {
                    stage.ClearSkelBindingTarget(
                        "/World",
                        (OpenUsdNativeSkelBindingRelationship)99);
                    lowLevelRelationshipRejected = false;
                }
                catch (ArgumentOutOfRangeException)
                {
                    lowLevelRelationshipRejected = true;
                }

                bool cardinalityRejected;
                try
                {
                    animation.SetTranslations([new UsdVec3f(0, 0, 0)], 20);
                    cardinalityRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.InvalidArgument)
                {
                    cardinalityRejected = true;
                }

                bool quaternionRejected;
                try
                {
                    animation.SetRotations(
                        [UsdQuatf.Identity, new UsdQuatf(2, 0, 0, 0)],
                        20);
                    quaternionRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.InvalidArgument)
                {
                    quaternionRejected = true;
                }

                bool influenceShapeRejected;
                try
                {
                    meshBinding.SetJointInfluences(
                        [0, 1],
                        [1],
                        elementSize: 2,
                        UsdSkelInterpolation.Vertex);
                    influenceShapeRejected = false;
                }
                catch (ArgumentException)
                {
                    influenceShapeRejected = true;
                }

                bool influenceRangeRejected;
                try
                {
                    meshBinding.SetJointInfluences(
                        [0, 2],
                        [0.5f, 0.5f],
                        elementSize: 2,
                        UsdSkelInterpolation.Constant);
                    influenceRangeRejected = false;
                }
                catch (ArgumentOutOfRangeException)
                {
                    influenceRangeRejected = true;
                }

                bool influenceWeightRejected;
                try
                {
                    meshBinding.SetJointInfluences(
                        [0, 1],
                        [0.5f, float.NaN],
                        elementSize: 2,
                        UsdSkelInterpolation.Constant);
                    influenceWeightRejected = false;
                }
                catch (ArgumentOutOfRangeException)
                {
                    influenceWeightRejected = true;
                }

                bool influenceElementRejected;
                try
                {
                    meshBinding.SetJointInfluences(
                        [0, 1],
                        [0.5f, 0.5f],
                        elementSize: 0,
                        UsdSkelInterpolation.Constant);
                    influenceElementRejected = false;
                }
                catch (ArgumentOutOfRangeException)
                {
                    influenceElementRejected = true;
                }

                bool influenceEmptyRejected;
                try
                {
                    meshBinding.SetJointInfluences(
                        [],
                        [],
                        elementSize: 1,
                        UsdSkelInterpolation.Constant);
                    influenceEmptyRejected = false;
                }
                catch (ArgumentException)
                {
                    influenceEmptyRejected = true;
                }

                bool influenceDivisibilityRejected;
                try
                {
                    meshBinding.SetJointInfluences(
                        [0, 1, 0],
                        [0.5f, 0.5f, 1],
                        elementSize: 2,
                        UsdSkelInterpolation.Vertex);
                    influenceDivisibilityRejected = false;
                }
                catch (ArgumentException)
                {
                    influenceDivisibilityRejected = true;
                }

                bool influenceInterpolationRejected;
                try
                {
                    meshBinding.SetJointInfluences(
                        [0, 1],
                        [0.5f, 0.5f],
                        elementSize: 2,
                        (UsdSkelInterpolation)99);
                    influenceInterpolationRejected = false;
                }
                catch (ArgumentOutOfRangeException)
                {
                    influenceInterpolationRejected = true;
                }

                UsdSkelJointInfluences influences = meshBinding.GetJointInfluences();
                if (!wrongSchemaRejected ||
                    !wrapRejected ||
                    !missingRejected ||
                    !orderingRejected ||
                    !lowLevelPathRejected ||
                    !lowLevelRotationPathRejected ||
                    !lowLevelRelationshipRejected ||
                    !cardinalityRejected ||
                    !quaternionRejected ||
                    !influenceShapeRejected ||
                    !influenceRangeRejected ||
                    !influenceWeightRejected ||
                    !influenceElementRejected ||
                    !influenceEmptyRejected ||
                    !influenceDivisibilityRejected ||
                    !influenceInterpolationRejected ||
                    !skeleton.GetJoints().SequenceEqual(skelJoints) ||
                    !skeleton.GetBindTransforms().SequenceEqual(bindTransforms) ||
                    !skeleton.GetRestTransforms().SequenceEqual(restTransforms) ||
                    !animation.GetTranslations(10).SequenceEqual(sampledTranslations) ||
                    !animation.GetRotations(10).SequenceEqual(sampledRotations) ||
                    !animation.GetScales(10).SequenceEqual(skelScales) ||
                    rootBinding.GetSkeleton().Path != skeleton.Path ||
                    skeletonBinding.GetAnimationSource().Path != animation.Path ||
                    meshBinding.GeomBindTransform != UsdMatrix4d.Identity ||
                    !influences.JointIndices.SequenceEqual(jointIndices) ||
                    !influences.JointWeights.SequenceEqual(jointWeights) ||
                    influences.ElementSize != 2 ||
                    influences.Interpolation != UsdSkelInterpolation.Vertex)
                {
                    Console.Error.WriteLine("Managed UsdSkel facade failed before save.");
                    return 55;
                }
                skelStage.Save();
            }

            using (UsdStage skelStage = UsdStage.Open(usdSkelPath))
            {
                UsdSkelRoot skelRoot =
                    UsdSkelRoot.Wrap(skelStage.GetPrim("/World/Character"));
                UsdSkelSkeleton skeleton =
                    UsdSkelSkeleton.Wrap(
                        skelStage.GetPrim("/World/Character/Skeleton"));
                UsdSkelAnimation animation =
                    UsdSkelAnimation.Wrap(
                        skelStage.GetPrim("/World/Character/Animation"));
                UsdSkelBinding rootBinding = UsdSkelBinding.Wrap(skelRoot.Prim);
                UsdSkelBinding skeletonBinding = UsdSkelBinding.Wrap(skeleton.Prim);
                UsdSkelBinding meshBinding = UsdSkelBinding.Wrap(
                    skelStage.GetPrim("/World/Character/Mesh"));
                UsdSkelJointInfluences influences = meshBinding.GetJointInfluences();
                if (!skeleton.GetJoints().SequenceEqual(skelJoints) ||
                    !skeleton.GetBindTransforms().SequenceEqual(bindTransforms) ||
                    !animation.GetTranslations(10).SequenceEqual(sampledTranslations) ||
                    !animation.GetRotations(10).SequenceEqual(sampledRotations) ||
                    rootBinding.GetSkeleton().Path != skeleton.Path ||
                    skeletonBinding.GetAnimationSource().Path != animation.Path ||
                    !influences.JointIndices.SequenceEqual(jointIndices) ||
                    !influences.JointWeights.SequenceEqual(jointWeights) ||
                    influences.ElementSize != 2 ||
                    influences.Interpolation != UsdSkelInterpolation.Vertex)
                {
                    Console.Error.WriteLine("Saved managed UsdSkel facade did not round-trip.");
                    return 56;
                }
            }
            Console.WriteLine("UsdSkel facade passed.");

            string editSublayerPath = Path.Combine(directory, "managed-edit-sublayer.usda");
            string editStagePath = Path.Combine(directory, "managed-edit-targets.usda");
            string foreignStagePath = Path.Combine(directory, "managed-foreign-edit-target.usda");
            File.Delete(editSublayerPath);
            File.Delete(editStagePath);
            File.Delete(foreignStagePath);
            using (UsdStage editSublayer = UsdStage.Create(editSublayerPath))
            {
                editSublayer.DefinePrim("/World/FromSublayer", "Xform");
                editSublayer.Save();
            }

            using (UsdStage editStage = UsdStage.Create(editStagePath))
            using (UsdLayer rootLayer = editStage.GetRootLayer())
            using (UsdLayer sessionLayer = editStage.GetSessionLayer())
            {
                rootLayer.AddSublayer(editSublayerPath);
                editStage.DefinePrim("/World/RootDirect", "Xform");
                editStage.SetEditTargetToSessionLayer();
                editStage.DefinePrim("/World/SessionDirect", "Xform");
                if (editStage.EditTargetLayerIdentifier != editStage.SessionLayerIdentifier)
                {
                    Console.Error.WriteLine("Session edit target was not selected.");
                    return 29;
                }

                editStage.SetEditTargetToRootLayer();
                editStage.DefinePrim("/World/RootConvenience", "Xform");
                editStage.SetEditTarget(sessionLayer);
                editStage.DefinePrim("/World/SessionOwned", "Xform");
                editStage.SetEditTarget(rootLayer);
                editStage.DefinePrim("/World/RootOwned", "Xform");

                bool foreignLayerRejected;
                using (UsdStage foreignStage = UsdStage.Create(foreignStagePath))
                using (UsdLayer foreignLayer = foreignStage.GetRootLayer())
                {
                    try
                    {
                        editStage.SetEditTarget(foreignLayer);
                        foreignLayerRejected = false;
                    }
                    catch (OpenUsdNativeException exception)
                        when (exception.Status == OpenUsdNativeStatus.InvalidArgument)
                    {
                        foreignLayerRejected = true;
                    }
                }

                string[] layerStack = editStage.GetLayerStackIdentifiers();
                string? sublayerIdentifier = layerStack.FirstOrDefault(
                    identifier =>
                        identifier != editStage.RootLayerIdentifier &&
                        identifier != editStage.SessionLayerIdentifier);
                if (!foreignLayerRejected ||
                    editStage.EditTargetLayerIdentifier != editStage.RootLayerIdentifier ||
                    !layerStack.Contains(editStage.RootLayerIdentifier, StringComparer.Ordinal) ||
                    !layerStack.Contains(editStage.SessionLayerIdentifier, StringComparer.Ordinal) ||
                    sublayerIdentifier is null ||
                    editStage.IsLayerMuted(sublayerIdentifier))
                {
                    Console.Error.WriteLine("Managed edit-target or layer-stack controls failed.");
                    return 30;
                }

                editStage.MuteLayer(sublayerIdentifier);
                if (!editStage.IsLayerMuted(sublayerIdentifier) ||
                    editStage.GetLayerStackIdentifiers().Contains(
                        sublayerIdentifier,
                        StringComparer.Ordinal))
                {
                    Console.Error.WriteLine("Managed layer muting failed.");
                    return 31;
                }
                editStage.UnmuteLayer(sublayerIdentifier);

                bool missingMuteRejected;
                try
                {
                    editStage.MuteLayer("missing-layer.usda");
                    missingMuteRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.NotFound)
                {
                    missingMuteRejected = true;
                }

                bool missingQueryRejected;
                try
                {
                    _ = editStage.IsLayerMuted("missing-layer.usda");
                    missingQueryRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.NotFound)
                {
                    missingQueryRejected = true;
                }

                if (editStage.IsLayerMuted(sublayerIdentifier) ||
                    !missingMuteRejected ||
                    !missingQueryRejected)
                {
                    Console.Error.WriteLine("Managed unmute or missing-identifier errors failed.");
                    return 32;
                }
                editStage.Save();
            }

            using (UsdStage editStage = UsdStage.Open(editStagePath))
            {
                if (!editStage.HasPrim("/World/RootDirect") ||
                    !editStage.HasPrim("/World/RootConvenience") ||
                    !editStage.HasPrim("/World/RootOwned") ||
                    editStage.HasPrim("/World/SessionDirect") ||
                    editStage.HasPrim("/World/SessionOwned") ||
                    !editStage.HasPrim("/World/FromSublayer"))
                {
                    Console.Error.WriteLine("Root and session edit-target authorship did not round-trip.");
                    return 33;
                }
            }

            string compositionSourcePath = Path.Combine(directory, "managed-composition-source.usda");
            string compositionControlsPath = Path.Combine(directory, "managed-composition-controls.usda");
            File.Delete(compositionSourcePath);
            File.Delete(compositionControlsPath);
            using (UsdStage sourceStage = UsdStage.Create(compositionSourcePath))
            {
                UsdPrim model = sourceStage.DefinePrim("/Model", "Xform");
                sourceStage.DefinePrim("/Model/Child", "Xform");
                model.SetDouble("custom:sourceValue", 33);
                sourceStage.Save();
            }

            using (UsdStage compositionStage = UsdStage.Create(compositionControlsPath))
            {
                UsdPrim inheritBase = compositionStage.CreateClassPrim("/InheritBase");
                inheritBase.SetDouble("custom:inherited", 11);
                UsdPrim specializeBase = compositionStage.CreateClassPrim("/SpecializeBase");
                specializeBase.SetDouble("custom:specialized", 22);

                UsdPrim inherited = compositionStage.DefinePrim("/World/Inherited", "Xform");
                inherited.AddInherit("/InheritBase");
                UsdPrim specialized = compositionStage.DefinePrim("/World/Specialized", "Xform");
                specialized.AddSpecialize("/SpecializeBase");
                UsdPrim payload = compositionStage.DefinePrim("/World/Payload", "Xform");
                payload.AddPayload(compositionSourcePath, "/Model");
                UsdPrim instancePrim = compositionStage.DefinePrim("/World/Instance", "Xform");
                instancePrim.AddReference(compositionSourcePath, "/Model");
                instancePrim.SetInstanceable(true);
                compositionStage.DefinePrim("/World/Keep", "Xform");
                compositionStage.DefinePrim("/World/Keep/Child", "Xform");
                compositionStage.DefinePrim("/World/Exclude", "Xform");
                compositionStage.DefinePrim("/Other", "Xform");
                compositionStage.Save();
            }

            using (UsdStage compositionStage = UsdStage.Open(compositionControlsPath))
            {
                UsdPrim inherited = compositionStage.GetPrim("/World/Inherited");
                UsdPrim specialized = compositionStage.GetPrim("/World/Specialized");
                UsdPrim payload = compositionStage.GetPrim("/World/Payload");
                UsdPrim instancePrim = compositionStage.GetPrim("/World/Instance");
                string prototypePath = instancePrim.GetPrototypePath();
                UsdPrim prototypePrim = compositionStage.GetPrim(prototypePath);

                payload.Unload();
                bool unloaded = !payload.IsLoaded();
                payload.Load();

                bool invalidInheritRejected;
                try
                {
                    inherited.AddInherit("relative");
                    invalidInheritRejected = false;
                }
                catch (ArgumentException)
                {
                    invalidInheritRejected = true;
                }

                bool missingSpecializeRejected;
                try
                {
                    specialized.AddSpecialize("/Missing");
                    missingSpecializeRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.NotFound)
                {
                    missingSpecializeRejected = true;
                }

                bool nonInstancePrototypeRejected;
                try
                {
                    _ = compositionStage.GetPrim("/World/Keep").GetPrototypePath();
                    nonInstancePrototypeRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.InvalidArgument)
                {
                    nonInstancePrototypeRejected = true;
                }

                bool prototypeLoadRejected;
                try
                {
                    prototypePrim.Load();
                    prototypeLoadRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.InvalidArgument)
                {
                    prototypeLoadRejected = true;
                }

                if (inherited.GetDouble("custom:inherited") != 11 ||
                    specialized.GetDouble("custom:specialized") != 22 ||
                    !unloaded ||
                    !payload.IsLoaded() ||
                    !instancePrim.IsInstance() ||
                    instancePrim.IsPrototype() ||
                    !prototypePrim.IsPrototype() ||
                    !invalidInheritRejected ||
                    !missingSpecializeRejected ||
                    !nonInstancePrototypeRejected ||
                    !prototypeLoadRejected)
                {
                    Console.Error.WriteLine("Managed composition/load inspection failed.");
                    return 34;
                }

                inherited.ClearInherits();
                specialized.ClearSpecializes();
                bool inheritCleared;
                try
                {
                    _ = inherited.GetDouble("custom:inherited");
                    inheritCleared = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.NotFound)
                {
                    inheritCleared = true;
                }

                bool specializeCleared;
                try
                {
                    _ = specialized.GetDouble("custom:specialized");
                    specializeCleared = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.NotFound)
                {
                    specializeCleared = true;
                }

                if (!inheritCleared || !specializeCleared)
                {
                    Console.Error.WriteLine("Clearing composition arcs failed.");
                    return 35;
                }
            }

            using (UsdStage maskedStage = UsdStage.OpenMasked(
                compositionControlsPath,
                ["/World/Keep"]))
            {
                string[] maskedPaths = maskedStage.Traverse().Select(static prim => prim.Path).ToArray();
                if (!maskedPaths.Contains("/World", StringComparer.Ordinal) ||
                    !maskedPaths.Contains("/World/Keep", StringComparer.Ordinal) ||
                    !maskedPaths.Contains("/World/Keep/Child", StringComparer.Ordinal) ||
                    maskedPaths.Contains("/World/Exclude", StringComparer.Ordinal) ||
                    maskedPaths.Contains("/Other", StringComparer.Ordinal))
                {
                    Console.Error.WriteLine("Managed masked traversal did not exclude unrelated prims.");
                    return 36;
                }
            }

            bool invalidMaskRejected;
            try
            {
                using UsdStage _ = UsdStage.OpenMasked(compositionControlsPath, ["relative"]);
                invalidMaskRejected = false;
            }
            catch (ArgumentException)
            {
                invalidMaskRejected = true;
            }
            if (!invalidMaskRejected)
            {
                Console.Error.WriteLine("Managed population-mask validation failed.");
                return 37;
            }

            string multiSourcePath = Path.Combine(directory, "managed-multi-source.usda");
            await File.WriteAllTextAsync(
                multiSourcePath,
                """
                #usda 1.0
                def "World" {
                    def Shader "A" {
                        uniform token info:id = "TestA"
                        float outputs:out = 1
                    }
                    def Shader "B" {
                        uniform token info:id = "TestB"
                        float outputs:out = 2
                    }
                    def Shader "Dest" {
                        uniform token info:id = "TestDest"
                        float inputs:value.connect = [
                            </World/A.outputs:out>,
                            </World/B.outputs:out>
                        ]
                    }
                }
                """);
            using (UsdStage multiSourceStage = UsdStage.Open(multiSourcePath))
            {
                UsdShadeInput input =
                    UsdShadeShader.Wrap(
                        multiSourceStage.GetPrim("/World/Dest")).GetInput("value");
                IReadOnlyList<UsdShadeConnection> sources = input.GetConnectedSources();
                bool singleRejected;
                try
                {
                    _ = input.GetConnectedSource();
                    singleRejected = false;
                }
                catch (OpenUsdNativeException exception)
                    when (exception.Status == OpenUsdNativeStatus.InvalidArgument)
                {
                    singleRejected = true;
                }
                if (sources.Count != 2 ||
                    sources[0].SourcePrimPath != "/World/A" ||
                    sources[1].SourcePrimPath != "/World/B" ||
                    !singleRejected)
                {
                    Console.Error.WriteLine("Multiple shading sources were not preserved.");
                    return 57;
                }
            }

            Console.WriteLine("All probes passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void RunWorldBoundsProbe(string directory)
    {
        string emptyPath = Path.Combine(directory, "managed-empty-bounds.usda");
        string modelPath = Path.Combine(directory, "managed-bounds-model.usda");
        string boundsPath = Path.Combine(directory, "managed-world-bounds.usda");
        File.Delete(emptyPath);
        File.Delete(modelPath);
        File.Delete(boundsPath);

        using (UsdStage emptyStage = UsdStage.Create(emptyPath))
        {
            UsdBounds3d empty = emptyStage.GetWorldBounds();
            if (!empty.IsEmpty ||
                empty.Min != default ||
                empty.Max != default ||
                empty.Center != default ||
                empty.Size != default)
            {
                throw new InvalidOperationException(
                    "An empty stage did not return canonical empty world bounds.");
            }
        }

        using (UsdStage modelStage = UsdStage.Create(modelPath))
        {
            modelStage.DefineXform("/Model");
            UsdGeomMesh modelMesh = modelStage.DefineMesh("/Model/Mesh");
            modelMesh.SetExtent(
                new UsdExtent3f(
                    new UsdVec3f(1, 2, 3),
                    new UsdVec3f(4, 6, 8)));
            modelStage.Save();
        }

        using (UsdStage stage = UsdStage.Create(boundsPath))
        {
            stage.DefineXform("/World");
            UsdGeomXform hierarchy = stage.DefineXform("/World/Hierarchy");
            hierarchy.Xformable.SetLocalTransform(UsdMatrix4d.CreateTranslation(5, 6, 7));
            UsdGeomMesh hierarchyMesh = stage.DefineMesh("/World/Hierarchy/Mesh");
            hierarchyMesh.SetExtent(
                new UsdExtent3f(
                    new UsdVec3f(-2, -1, 0),
                    new UsdVec3f(2, 1, 0)));

            stage.DefineXform("/World/Purposes");
            UsdGeomMesh defaultMesh = stage.DefineMesh("/World/Purposes/Default");
            defaultMesh.SetExtent(
                new UsdExtent3f(
                    new UsdVec3f(0, 0, 0),
                    new UsdVec3f(1, 1, 1)));
            UsdGeomMesh proxyMesh = stage.DefineMesh("/World/Purposes/Proxy");
            proxyMesh.SetExtent(
                new UsdExtent3f(
                    new UsdVec3f(10, 10, 10),
                    new UsdVec3f(11, 11, 11)));
            proxyMesh.Imageable.SetPurpose(UsdGeomPurpose.Proxy);
            UsdGeomMesh renderMesh = stage.DefineMesh("/World/Purposes/Render");
            renderMesh.SetExtent(
                new UsdExtent3f(
                    new UsdVec3f(20, 20, 20),
                    new UsdVec3f(21, 21, 21)));
            renderMesh.Imageable.SetPurpose(UsdGeomPurpose.Render);
            UsdGeomMesh guideMesh = stage.DefineMesh("/World/Purposes/Guide");
            guideMesh.SetExtent(
                new UsdExtent3f(
                    new UsdVec3f(30, 30, 30),
                    new UsdVec3f(31, 31, 31)));
            guideMesh.Imageable.SetPurpose(UsdGeomPurpose.Guide);

            UsdGeomMesh animated = stage.DefineMesh("/World/Animated");
            animated.SetExtent(
                new UsdExtent3f(
                    new UsdVec3f(-1, -1, -1),
                    new UsdVec3f(1, 1, 1)));
            animated.SetExtent(
                new UsdExtent3f(
                    new UsdVec3f(-3, -2, -1),
                    new UsdVec3f(3, 2, 1)),
                timeCode: 10);

            UsdGeomMesh inactive = stage.DefineMesh("/World/Inactive");
            inactive.SetExtent(
                new UsdExtent3f(
                    new UsdVec3f(0, 0, 0),
                    new UsdVec3f(1, 1, 1)));
            inactive.Prim.SetActive(false);

            UsdPrim instance = stage.DefinePrim("/World/Instance", "Xform");
            instance.AddReference(modelPath, "/Model");
            instance.SetInstanceable(true);
            UsdPrim payload = stage.DefinePrim("/World/Payload", "Xform");
            payload.AddPayload(modelPath, "/Model");

            UsdBounds3d hierarchyBounds = hierarchy.Prim.GetWorldBounds();
            UsdBounds3d defaultBounds = stage.GetPrim("/World/Purposes")
                .GetWorldBounds(UsdGeomPurposeMask.Default);
            UsdBounds3d proxyBounds = stage.GetPrim("/World/Purposes")
                .GetWorldBounds(UsdGeomPurposeMask.Proxy);
            UsdBounds3d renderBounds = stage.GetPrim("/World/Purposes")
                .GetWorldBounds(UsdGeomPurposeMask.Render);
            UsdBounds3d guideBounds = stage.GetPrim("/World/Purposes")
                .GetWorldBounds(UsdGeomPurposeMask.Guide);
            UsdBounds3d allBounds = stage.GetPrim("/World/Purposes")
                .GetWorldBounds(UsdGeomPurposeMask.All);
            UsdBounds3d noBounds = stage.GetPrim("/World/Purposes")
                .GetWorldBounds(UsdGeomPurposeMask.None);
            UsdBounds3d animatedDefault = animated.Prim.GetWorldBounds();
            UsdBounds3d animatedSample = animated.Prim.GetWorldBounds(10);

            if (hierarchyBounds != new UsdBounds3d(
                    new UsdVec3d(3, 5, 7),
                    new UsdVec3d(7, 7, 7)) ||
                hierarchyBounds.Center != new UsdVec3d(5, 6, 7) ||
                hierarchyBounds.Size != new UsdVec3d(4, 2, 0) ||
                defaultBounds.Min.X != 0 || defaultBounds.Max.X != 1 ||
                proxyBounds.Min.X != 10 || proxyBounds.Max.X != 11 ||
                renderBounds.Min.X != 20 || renderBounds.Max.X != 21 ||
                guideBounds.Min.X != 30 || guideBounds.Max.X != 31 ||
                allBounds.Min.X != 0 || allBounds.Max.X != 31 ||
                !noBounds.IsEmpty ||
                animatedDefault.Min.X != -1 || animatedDefault.Max.X != 1 ||
                animatedSample.Min.X != -3 || animatedSample.Max.X != 3 ||
                stage.GetWorldBounds().IsEmpty ||
                !stage.GetPrim("/World/Missing").GetWorldBounds().IsEmpty ||
                !inactive.Prim.GetWorldBounds().IsEmpty ||
                instance.GetWorldBounds().IsEmpty ||
                payload.GetWorldBounds().IsEmpty)
            {
                throw new InvalidOperationException(
                    "Managed stage or prim world bounds were incorrect before save.");
            }

            bool detachedPathRejected;
            try
            {
                _ = default(UsdPrim).GetWorldBounds();
                detachedPathRejected = false;
            }
            catch (ArgumentException)
            {
                detachedPathRejected = true;
            }

            bool lowLevelPathRejected;
            try
            {
                _ = stage.Native.GetWorldBounds(
                    "relative",
                    (uint)UsdGeomPurposeMask.All);
                lowLevelPathRejected = false;
            }
            catch (ArgumentException)
            {
                lowLevelPathRejected = true;
            }

            bool maskRejected;
            try
            {
                _ = stage.GetWorldBounds((UsdGeomPurposeMask)(1U << 31));
                maskRejected = false;
            }
            catch (ArgumentOutOfRangeException)
            {
                maskRejected = true;
            }

            bool nanRejected;
            try
            {
                _ = stage.GetWorldBounds(double.NaN);
                nanRejected = false;
            }
            catch (ArgumentOutOfRangeException)
            {
                nanRejected = true;
            }

            bool infinityRejected;
            try
            {
                _ = animated.Prim.GetWorldBounds(double.PositiveInfinity);
                infinityRejected = false;
            }
            catch (ArgumentOutOfRangeException)
            {
                infinityRejected = true;
            }

            if (!detachedPathRejected ||
                !lowLevelPathRejected ||
                !maskRejected ||
                !nanRejected ||
                !infinityRejected)
            {
                throw new InvalidOperationException(
                    "Managed world-bounds input validation did not fail before P/Invoke.");
            }
            stage.Save();
        }

        using (UsdStage stage = UsdStage.Open(boundsPath))
        {
            UsdPrim instance = stage.GetPrim("/World/Instance");
            UsdPrim prototype = stage.GetPrim(instance.GetPrototypePath());
            UsdPrim payload = stage.GetPrim("/World/Payload");
            if (instance.GetWorldBounds().IsEmpty ||
                prototype.GetWorldBounds().IsEmpty ||
                payload.GetWorldBounds().IsEmpty)
            {
                throw new InvalidOperationException(
                    "Saved instance, prototype, or payload bounds were empty.");
            }

            payload.Unload();
            if (payload.IsLoaded() || !payload.GetWorldBounds().IsEmpty)
            {
                throw new InvalidOperationException(
                    "An unloaded payload without extents hints did not return empty bounds.");
            }
        }
    }
}
