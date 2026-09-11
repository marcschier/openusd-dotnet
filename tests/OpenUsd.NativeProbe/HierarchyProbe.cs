// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.NativeProbe;

internal static class HierarchyProbe
{
    internal static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"hierarchy-aot-{Guid.NewGuid():N}.usda");
        const string source = """
            #usda 1.0
            def Xform "World" (
                prepend variantSets = "look"
                variants = { string look = "two" }
            ) {
                variantSet "look" = {
                    "two" {}
                    "one" {}
                }
                def Scope "Child" {}
            }
            over "Over" {}
            class "Class" {}
            def Scope "Inactive" (active = false) {}
            """;
        await File.WriteAllTextAsync(path, source).ConfigureAwait(false);
        try
        {
            UsdHierarchySnapshot snapshot;
            UsdHierarchyEntry entry;
            UsdHierarchyVariantSet variant;
            await using (UsdStageScheduler scheduler = UsdStageScheduler.Open(path))
            {
                snapshot = await scheduler.InvokeAsync(stage =>
                {
                    ulong serial = stage.ChangeSerial;
                    UsdHierarchySnapshot result = stage.GetHierarchySnapshot();
                    if (result.ChangeSerial != serial || stage.ChangeSerial != serial)
                    {
                        throw new InvalidOperationException("The hierarchy query changed its source stage.");
                    }
                    return result;
                }).ConfigureAwait(false);
                entry = await scheduler.InvokeAsync(stage => stage.GetHierarchySnapshot().Entries[0])
                    .ConfigureAwait(false);
                variant = await scheduler.InvokeAsync(stage => stage.GetHierarchySnapshot().Entries[0].VariantSets[0])
                    .ConfigureAwait(false);
                bool refused = await scheduler.InvokeAsync(stage =>
                {
                    try
                    {
                        _ = stage.GetHierarchySnapshot(new UsdHierarchyLimits(maximumPrimCount: 1));
                        return false;
                    }
                    catch (OpenUsdNativeException exception)
                    {
                        return exception.Message.Contains("prim count", StringComparison.Ordinal);
                    }
                }).ConfigureAwait(false);
                if (!refused)
                {
                    throw new InvalidOperationException("The hierarchy prim quota was not enforced.");
                }
            }
            if (!snapshot.IsComplete || snapshot.Entries.Count != 5 ||
                snapshot.Entries[0].Path != "/World" || snapshot.Entries[0].ChildCount != 1 ||
                snapshot.Entries[1].ParentIndex != 0 || snapshot.Entries[1].Depth != 2 ||
                snapshot.Entries[2].IsDefined || !snapshot.Entries[3].IsAbstract ||
                snapshot.Entries[4].IsActive || entry.VariantSets.Count != 1 ||
                variant.Selection != "two" || variant.VariantNames.Count != 2 ||
                variant.VariantNames[0] != "one" || variant.VariantNames[1] != "two" ||
                snapshot.Entries is UsdHierarchyEntry[] || variant.VariantNames is string[] ||
                await File.ReadAllTextAsync(path).ConfigureAwait(false) != source)
            {
                throw new InvalidOperationException(
                    "A detached native hierarchy snapshot lost data or changed its source.");
            }
            Console.WriteLine("HIERARCHY_MANAGED_OK: bulk all-prim hierarchy, variants, quotas, scheduler lifetime");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
