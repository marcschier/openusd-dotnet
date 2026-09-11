// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Package.Tests;

internal static class HierarchyConsumerSource
{
    internal const string Text =
        """"
    using System;
    using System.IO;
    using OpenUsd;
    using OpenUsd.Interop;

    namespace PackageExecutionConsumer;

    internal static class HierarchyConsumer
    {
        internal static bool Run()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "hierarchy-" + Guid.NewGuid().ToString("N") + ".usda");
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
            File.WriteAllText(path, source);
            try
            {
                UsdHierarchySnapshot snapshot;
                UsdHierarchyVariantSet variant;
                UsdStageScheduler scheduler = UsdStageScheduler.Open(path);
                try
                {
                    snapshot = scheduler.InvokeAsync(stage =>
                    {
                        ulong serial = stage.ChangeSerial;
                        UsdHierarchySnapshot result = stage.GetHierarchySnapshot();
                        if (result.ChangeSerial != serial || stage.ChangeSerial != serial)
                        {
                            throw new InvalidOperationException("Hierarchy query changed the source.");
                        }
                        return result;
                    }).GetAwaiter().GetResult();
                    variant = scheduler.InvokeAsync(stage => stage.GetHierarchySnapshot().Entries[0].VariantSets[0])
                        .GetAwaiter().GetResult();
                    bool refused = scheduler.InvokeAsync(stage =>
                    {
                        try
                        {
                            _ = stage.GetHierarchySnapshot(new UsdHierarchyLimits(maximumPrimCount: 1));
                            return false;
                        }
                        catch (OpenUsdNativeException error)
                        {
                            return error.Message.Contains("prim count", StringComparison.Ordinal);
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
                return snapshot.IsComplete && snapshot.Entries.Count == 5 &&
                    snapshot.Entries[0].Path == "/World" && snapshot.Entries[0].ChildCount == 1 &&
                    snapshot.Entries[1].ParentIndex == 0 && snapshot.Entries[1].Depth == 2 &&
                    !snapshot.Entries[2].IsDefined && snapshot.Entries[3].IsAbstract &&
                    !snapshot.Entries[4].IsActive &&
                    variant.Selection == "two" && variant.VariantNames.Count == 2 &&
                    variant.VariantNames[0] == "one" && variant.VariantNames[1] == "two" &&
                    !(snapshot.Entries is UsdHierarchyEntry[]) && !(variant.VariantNames is string[]) &&
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
