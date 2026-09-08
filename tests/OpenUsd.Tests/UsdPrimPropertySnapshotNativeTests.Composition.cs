// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Tests;

public sealed partial class UsdPrimPropertySnapshotNativeTests
{
    [Test]
    [Arguments("internal", false)]
    [Arguments("internal", true)]
    [Arguments("external", false)]
    [Arguments("external", true)]
    [Arguments("layer-stack", false)]
    [Arguments("layer-stack", true)]
    public async Task StoredCompositionErrorsPreventACompleteInventory(string errorKind, bool survivingLocalValue)
    {
        string missing = $"missing-property-composition-{Guid.NewGuid():N}.usda";
        string layerHeader = errorKind == "layer-stack" ? $"(subLayers = [@{missing}@])" : "";
        string primMetadata = errorKind switch
        {
            "internal" => "(references = </Missing>)",
            "external" => $"(references = @{missing}@</Model>)",
            _ => ""
        };
        string local = survivingLocalValue ? "custom double answer = 7" : "";
        string path = await WriteStageAsync(
            $$"""
            #usda 1.0
            {{layerHeader}}
            def "Subject" {{primMetadata}}
            {
                {{local}}
            }
            """);
        try
        {
            using UsdStage stage = UsdStage.Open(path);
            ulong serial = stage.ChangeSerial;
            OpenUsdNativeException? failure = null;
            try
            {
                _ = stage.GetPrimPropertySnapshot("/Subject");
            }
            catch (OpenUsdNativeException exception)
            {
                failure = exception;
            }
            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.Status).IsEqualTo(OpenUsdNativeStatus.NativeError);
            await Assert.That(failure.Message).Contains("composition");
            await Assert.That(failure.Message).DoesNotContain(missing);
            await Assert.That(stage.ChangeSerial).IsEqualTo(serial);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task UnrelatedStoredPrimErrorsDoNotPoisonACompleteSelectedInventory()
    {
        string path = await WriteStageAsync(
            """
            #usda 1.0
            def "Subject"
            {
                custom double answer = 7
            }
            def "Unrelated" (references = </Missing>) {}
            """);
        try
        {
            using UsdStage stage = UsdStage.Open(path);
            UsdPrimPropertySnapshot snapshot = stage.GetPrimPropertySnapshot("/Subject");
            await Assert.That(snapshot.IsComplete).IsTrue();
            await Assert.That(snapshot.Properties.Count).IsEqualTo(1);
            await Assert.That(Attribute(snapshot, "answer").Value.Elements.Single()).IsEqualTo("7");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
