// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Performance.Tests;

public sealed class PInvokeBoundaryContractTests
{
    private static readonly string[] ForbiddenBoundaryTokens =
    [
        "LibraryImport(",
        "DllImport(",
        "NativeMethods.",
        "OpenUsdNativeMethods.",
        "OpenUsdNativeRuntime.",
        "OpenUsdSilkRuntime.",
    ];

    [Test]
    public async Task ManagedSceneAndStateHotPathsRemainNativeFree()
    {
        string[] relativePaths =
        [
            "src/OpenUsd.Rendering/StageRenderState.cs",
            "src/OpenUsd.Rendering/PickingContracts.cs",
            "src/OpenUsd.Rendering.Silk/SilkCommand.cs",
            "src/OpenUsd.Rendering.Silk/SilkCommandEnumerator.cs",
            "src/OpenUsd.Rendering.Silk/SilkCommandParser.cs",
            "src/OpenUsd.Rendering.Silk/SilkMeshGeometry.cs",
            "src/OpenUsd.Rendering.Silk/SilkPickIdentityTable.cs",
            "src/OpenUsd.Rendering.Silk/SilkSceneState.cs",
        ];
        var violations = new List<string>();

        foreach (string relativePath in relativePaths)
        {
            string source = RepositorySource.Read(relativePath);
            foreach (string token in ForbiddenBoundaryTokens)
            {
                if (source.Contains(token, StringComparison.Ordinal))
                {
                    violations.Add($"{relativePath}: {token}");
                }
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task CollectionWrappersKeepNativeCallsOutsideElementLoops()
    {
        string stage = RepositorySource.ExtractBlock(
            RepositorySource.Read("src/OpenUsd/UsdStage.cs"),
            "public static UsdStage OpenMasked(");
        string topology = RepositorySource.ExtractBlock(
            RepositorySource.Read("src/OpenUsd/Geom/UsdGeomMesh.cs"),
            "public void SetTopology(");

        await AssertSingleCallOutsideLoops(
            stage,
            "OpenUsdNativeRuntime.OpenStageMasked(");
        await AssertSingleCallOutsideLoops(
            topology,
            "Stage.Native.SetGeomMeshTopology(");
    }

    [Test]
    public async Task NativeWrappersUseOnePackedOrPageBoundary()
    {
        string nativeRuntime = RepositorySource.Read(
            "src/OpenUsd.Interop/OpenUsdNativeRuntime.cs");
        string openMasked = RepositorySource.ExtractBlock(
            nativeRuntime,
            "internal static OpenUsdNativeStage OpenStageMasked(");
        string setTargets = RepositorySource.ExtractBlock(
            nativeRuntime,
            "internal static void SetRelationshipTargets(");
        string silkSync = RepositorySource.ExtractBlock(
            RepositorySource.Read(
                "src/OpenUsd.Rendering.Silk/OpenUsdSilkRuntime.cs"),
            "internal static OpenUsdNativeStatus InvokeSync<TCall>(");

        await AssertSinglePackedBoundary(
            openMasked,
            "NativeMethods.StageOpenMasked(");
        await AssertSinglePackedBoundary(
            setTargets,
            "NativeMethods.StageSetRelationshipTargets(");
        await Assert.That(RepositorySource.Count(silkSync, "TCall.Invoke("))
            .IsEqualTo(1);
        await Assert.That(RepositorySource.ContainsLoop(silkSync)).IsFalse();
    }

    [Test]
    public async Task DelegateBoundariesKeepConstantNativeCallCounts()
    {
        string nativeRuntime = RepositorySource.Read(
            "src/OpenUsd.Interop/OpenUsdNativeRuntime.cs");
        string nativeSkel = RepositorySource.Read(
            "src/OpenUsd.Interop/OpenUsdNativeSkel.cs");

        await AssertHookCallCount(
            nativeRuntime,
            "private static void SetGeomArray<T>(",
            "setter(",
            expected: 1);
        await AssertHookCallCount(
            nativeRuntime,
            "private static T[] GetGeomArray<T>(",
            "getter(",
            expected: 2);
        await AssertHookCallCount(
            nativeRuntime,
            "private static T[] GetGeomUntimedArray<T>(",
            "getter(",
            expected: 2);
        await AssertHookCallCount(
            nativeRuntime,
            "private static void SetArray<T>(",
            "setter(",
            expected: 1);
        await AssertHookCallCount(
            nativeRuntime,
            "private static T[] GetArray<T>(",
            "getter(",
            expected: 2);
        await AssertHookCallCount(
            nativeRuntime,
            "private static string GetString(NativeStringGetter getter)",
            "getter(",
            expected: 2);
        await AssertHookCallCount(
            nativeSkel,
            "private static void SetSkelArray<T>(",
            "setter(",
            expected: 1);
        await AssertHookCallCount(
            nativeSkel,
            "private static T[] GetSkelArray<T>(",
            "getter(",
            expected: 2);
    }

    private static async Task AssertSinglePackedBoundary(
        string method,
        string nativeCall)
    {
        await Assert.That(RepositorySource.Count(
            method,
            "NativeStringListPacking.Pack(")).IsEqualTo(1);
        await Assert.That(RepositorySource.Count(method, nativeCall)).IsEqualTo(1);
        await Assert.That(RepositorySource.ContainsLoop(method)).IsFalse();
    }

    private static async Task AssertSingleCallOutsideLoops(
        string method,
        string nativeCall)
    {
        await Assert.That(RepositorySource.Count(method, nativeCall)).IsEqualTo(1);
        await Assert.That(
            RepositorySource.IsTokenInsideLoop(method, nativeCall)).IsFalse();
    }

    private static async Task AssertHookCallCount(
        string source,
        string methodMarker,
        string hook,
        int expected)
    {
        string method = RepositorySource.ExtractBlock(source, methodMarker);
        await Assert.That(RepositorySource.Count(method, hook)).IsEqualTo(expected);
        await Assert.That(RepositorySource.IsTokenInsideLoop(method, hook)).IsFalse();
    }
}

internal static class RepositorySource
{
    private static readonly string Root = FindRoot();

    internal static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(
            Root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    internal static int Count(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    internal static bool ContainsLoop(string source) =>
        source.Contains("for (", StringComparison.Ordinal) ||
        source.Contains("foreach (", StringComparison.Ordinal) ||
        source.Contains("while (", StringComparison.Ordinal);

    internal static bool IsTokenInsideLoop(string source, string token)
    {
        if (!source.Contains(token, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Source token '{token}' was not found.");
        }

        foreach (string marker in new[] { "for (", "foreach (", "while (" })
        {
            int markerIndex = 0;
            while ((markerIndex = source.IndexOf(
                marker,
                markerIndex,
                StringComparison.Ordinal)) >= 0)
            {
                int start = source.IndexOf('{', markerIndex + marker.Length);
                if (start < 0)
                {
                    throw new InvalidOperationException(
                        $"Loop marker '{marker}' has no body.");
                }
                int end = FindBlockEnd(source, start);
                if (source.IndexOf(
                    token,
                    start,
                    (end - start) + 1,
                    StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
                markerIndex = end + 1;
            }
        }
        return false;
    }

    internal static string ExtractBlock(string source, string marker)
    {
        int markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            throw new InvalidOperationException($"Source marker '{marker}' was not found.");
        }
        int start = source.IndexOf('{', markerIndex);
        if (start < 0)
        {
            throw new InvalidOperationException($"Source marker '{marker}' has no body.");
        }
        int end = FindBlockEnd(source, start);
        return source[start..(end + 1)];
    }

    private static int FindBlockEnd(string source, int start)
    {
        int depth = 0;
        for (int index = start; index < source.Length; index++)
        {
            switch (source[index])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return index;
                    }
                    break;
            }
        }
        throw new InvalidOperationException("Source block has an incomplete body.");
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate the repository root for source-contract tests.");
    }
}
