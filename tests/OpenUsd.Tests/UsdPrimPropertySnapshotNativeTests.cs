// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Tests;

public sealed partial class UsdPrimPropertySnapshotNativeTests
{
    [Test]
    public async Task ComposedPropertySnapshotOutlivesItsSchedulerAndCannotBeMutated()
    {
        string path = await WriteStageAsync(
            """
            #usda 1.0
            def Xform "Subject"
            {
                custom int answer = 42
                custom float connected = 4
                custom float connected.connect = </Graph.outputs:value>
                custom rel target = </Other>
            }
            def Scope "Other" {}
            """);
        try
        {
            UsdPrimPropertySnapshot snapshot;
            await using (UsdStageScheduler scheduler = UsdStageScheduler.Open(path))
            {
                snapshot = await scheduler.InvokeAsync(stage => stage.GetPrimPropertySnapshot("/Subject"));
            }

            await Assert.That(snapshot.PrimPath).IsEqualTo("/Subject");
            await Assert.That(snapshot.TimeCode).IsNull();
            UsdAttributePropertySnapshot answer = Attribute(snapshot, "answer");
            await Assert.That(answer.TypeName).IsEqualTo("int");
            await Assert.That(answer.Value.Elements.Single()).IsEqualTo("42");
            await Assert.That(answer.ResolveSource).IsEqualTo(UsdAttributeResolveSource.Default);
            await Assert.That(answer.HasAuthoredValueOpinion).IsTrue();
            await Assert.That(answer.ValueSource!.SpecPath).IsEqualTo("/Subject.answer");
            UsdAttributePropertySnapshot connected = Attribute(snapshot, "connected");
            await Assert.That(connected.Value.Elements.Single()).IsEqualTo("4");
            await Assert.That(connected.Connections.Paths.Single()).IsEqualTo("/Graph.outputs:value");
            var target = (UsdRelationshipPropertySnapshot)snapshot.Properties
                .Single(property => property.Name == "target");
            await Assert.That(target.Targets.Paths.Single()).IsEqualTo("/Other");
            await Assert.That(snapshot.Properties is UsdPropertySnapshot[]).IsFalse();
            await Assert.That(answer.Value.Elements is string[]).IsFalse();
            await Assert.That(() => ((IList<string>)answer.Value.Elements)[0] = "changed")
                .Throws<NotSupportedException>();
            await Assert.That(() => ((IList<UsdPropertySnapshot>)snapshot.Properties).Clear())
                .Throws<NotSupportedException>();
            await Assert.That(answer.Value.Elements.Single()).IsEqualTo("42");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<string> WriteStageAsync(string content)
    {
        string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
        if (string.IsNullOrWhiteSpace(plugins) || !Directory.Exists(plugins))
        {
            Skip.Test("Set OPENUSD_TEST_PLUGIN_PATH and a matched native property inspection runtime.");
        }
        _ = OpenUsdNativeRuntime.RegisterPlugins(plugins!);
        string directory = Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT")
            ?? Path.Combine(AppContext.BaseDirectory, "native-property-work");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"property-{Guid.NewGuid():N}.usda");
        await File.WriteAllTextAsync(path, content);
        return path;
    }
}
