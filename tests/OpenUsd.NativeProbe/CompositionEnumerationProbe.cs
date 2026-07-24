// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.NativeProbe;

internal static class CompositionEnumerationProbe
{
    internal static void Run(string directory)
    {
        string sourceAPath = Path.Combine(directory, "managed-enumeration-a.usda");
        string sourceBPath = Path.Combine(directory, "managed-enumeration-b.usda");
        string stagePath = Path.Combine(directory, "managed-composition-enumeration.usda");
        string lifetimePath = Path.Combine(directory, "managed-enumeration-lifetime.usda");
        string[] files = [sourceAPath, sourceBPath, stagePath, lifetimePath];
        foreach (string path in files)
        {
            File.Delete(path);
        }

        try
        {
            CreatePayloadSource(sourceAPath, "/ModelA");
            CreatePayloadSource(sourceBPath, "/ModelB");
            string sourceAAsset = Path.GetFileName(sourceAPath);
            string sourceBAsset = Path.GetFileName(sourceBPath);

            using (UsdStage stage = UsdStage.Create(stagePath))
            {
                UsdPrim payloads = stage.DefinePrim("/World/Payloads", "Xform");
                payloads.AddPayload(sourceAAsset, "/ModelA");
                payloads.AddPayload(sourceBAsset);
                stage.DefinePrim("/World/Empty", "Xform");

                UsdPrim inactivePayload =
                    stage.DefinePrim("/World/InactivePayload", "Xform");
                inactivePayload.AddPayload(sourceAAsset, "/ModelA");
                inactivePayload.SetActive(false);

                UsdPrim variants = stage.DefinePrim("/World/Variants", "Xform");
                variants.AddVariantSet("look");
                variants.AddVariantSet("lod");
                stage.DefinePrim("/World/NoVariants", "Xform");
                UsdPrim inactiveVariants =
                    stage.DefinePrim("/World/InactiveVariants", "Xform");
                inactiveVariants.AddVariantSet("inactiveSet");
                inactiveVariants.SetActive(false);
                stage.Save();
            }

            using (UsdStage stage = UsdStage.Open(stagePath))
            {
                UsdPrim payloads = stage.GetPrim("/World/Payloads");
                IReadOnlyList<UsdPayloadArc> arcs = payloads.GetPayloadArcs();
                Require(arcs.Count == 2, "Managed payload enumeration did not return both arcs.");
                Require(
                    arcs[0].AssetPath == sourceAAsset &&
                    arcs[0].TargetPrimPath == "/ModelA" &&
                    arcs[1].AssetPath == sourceBAsset &&
                    arcs[1].TargetPrimPath.Length == 0,
                    "Managed payload enumeration did not preserve authored relative and target paths.");
                Require(
                    arcs.All(arc => arc.SourceLayerIdentifier == stage.RootLayerIdentifier),
                    "Managed payload enumeration reported the wrong introducing layer.");
                Require(
                    arcs is not UsdPayloadArc[],
                    "Managed payload enumeration exposed a mutable result array.");

                payloads.Unload();
                IReadOnlyList<UsdPayloadArc> unloadedArcs = payloads.GetPayloadArcs();
                Require(
                    unloadedArcs.SequenceEqual(arcs),
                    "Unloading a payload changed its composed arc inspection.");
                Require(
                    stage.GetPrim("/World/Empty").GetPayloadArcs().Count == 0,
                    "A prim without payloads did not return an empty result.");
                Require(
                    stage.GetPrim("/World/InactivePayload").GetPayloadArcs().Count == 1,
                    "An inactive prim lost its authored payload arc inspection.");

                bool missingPayloadRejected = RejectsMissing(
                    () => stage.GetPrim("/World/Missing").GetPayloadArcs());
                bool missingPayloadRejectedAgain = RejectsMissing(
                    () => stage.GetPrim("/World/Missing").GetPayloadArcs());
                Require(
                    missingPayloadRejected && missingPayloadRejectedAgain,
                    "Missing payload prim failures were not deterministic.");

                UsdPrim variants = stage.GetPrim("/World/Variants");
                string[] variantSetNames = variants.GetVariantSetNames();
                Require(
                    variantSetNames.SequenceEqual(["look", "lod"]),
                    "Variant-set names did not preserve deterministic authored order.");
                Require(
                    variants.GetVariantSetNames().SequenceEqual(variantSetNames),
                    "Variant-set enumeration order changed between reads.");
                Require(
                    stage.GetPrim("/World/NoVariants").GetVariantSetNames().Length == 0,
                    "A prim without variant sets did not return an empty result.");
                Require(
                    stage.GetPrim("/World/InactiveVariants")
                        .GetVariantSetNames()
                        .SequenceEqual(["inactiveSet"]),
                    "An inactive prim lost its authored variant-set names.");
                Require(
                    RejectsMissing(
                        () => stage.GetPrim("/World/Missing").GetVariantSetNames()) &&
                    RejectsMissing(
                        () => stage.GetPrim("/World/Missing").GetVariantSetNames()),
                    "Missing variant prim failures were not deterministic.");

                variants.AddVariantSet("lateSet");
                payloads.ClearPayloads();
                Require(
                    variantSetNames.SequenceEqual(["look", "lod"]) &&
                    arcs.Count == 2 &&
                    arcs[0].AssetPath == sourceAAsset,
                    "Previously returned composition values were not detached.");

                var invalidPrim = new UsdPrim(stage, "relative");
                Require(
                    Throws<ArgumentException>(() => invalidPrim.GetVariantSetNames()) &&
                    Throws<ArgumentException>(() => invalidPrim.GetPayloadArcs()),
                    "Managed composition enumeration did not validate prim paths.");
            }

            Require(
                Throws<ArgumentException>(() => default(UsdPrim).GetVariantSetNames()) &&
                Throws<ArgumentException>(() => default(UsdPrim).GetPayloadArcs()),
                "Default prim composition enumeration did not reject invalid paths.");

            UsdPrim expiredVariantPrim;
            UsdPrim expiredPayloadPrim;
            using (UsdStage stage = UsdStage.Create(lifetimePath))
            {
                expiredVariantPrim = stage.DefinePrim("/World/Variants", "Xform");
                expiredVariantPrim.AddVariantSet("set");
                expiredPayloadPrim = stage.DefinePrim("/World/Payload", "Xform");
                expiredPayloadPrim.AddPayload(Path.GetFileName(sourceAPath), "/ModelA");
            }
            Require(
                Throws<ObjectDisposedException>(() => expiredVariantPrim.GetVariantSetNames()) &&
                Throws<ObjectDisposedException>(() => expiredPayloadPrim.GetPayloadArcs()),
                "Composition enumeration did not enforce stage lifetime.");
        }
        finally
        {
            foreach (string path in files)
            {
                File.Delete(path);
            }
        }
    }

    private static void CreatePayloadSource(string path, string primPath)
    {
        using UsdStage stage = UsdStage.Create(path);
        stage.DefinePrim(primPath, "Xform");
        stage.SetDefaultPrim(primPath);
        stage.Save();
    }

    private static bool RejectsMissing(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (OpenUsdNativeException exception)
            when (exception.Status == OpenUsdNativeStatus.NotFound)
        {
            return true;
        }
    }

    private static bool Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
