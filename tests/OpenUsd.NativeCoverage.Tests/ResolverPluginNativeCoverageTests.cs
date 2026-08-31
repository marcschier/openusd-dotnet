// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.NativeCoverage.Tests;

/// <summary>
/// Covers resolver contexts, bindings, and plugin-tree discovery.
/// </summary>
/// <remarks>
/// Every test here touches process-global OpenUSD state: the plugin registry, the thread-local
/// binding stack, and the process-wide registry of bound contexts that gates
/// <see cref="UsdResolverContext.Refresh"/>. Running two of them at once would let one test's
/// binding decide another test's refresh result, so the whole class is serialised.
/// </remarks>
[NotInParallel("ResolverProcessState")]
public sealed class ResolverPluginNativeCoverageTests
{
    [Test]
    public async Task RegisteredPluginsReportTheDiscoveredTreeWithoutFlattening()
    {
        NativeCoverageRuntime.EnsureNativeLoaded();

        IReadOnlyList<UsdPluginInfo> plugins = UsdPluginRegistry.GetRegisteredPlugins();

        await Assert.That(plugins.Count).IsGreaterThan(0);
        await Assert.That(plugins.Select(plugin => plugin.Name).SequenceEqual(
                plugins.Select(plugin => plugin.Name).OrderBy(name => name, StringComparer.Ordinal)))
            .IsTrue();
        await Assert.That(plugins.Any(plugin => plugin.Name == "usd")).IsTrue();

        UsdPluginInfo usd = plugins.First(plugin => plugin.Name == "usd");
        await Assert.That(usd.Kind).IsEqualTo(UsdPluginKind.Library);
        await Assert.That(string.IsNullOrEmpty(usd.ResourcePath)).IsFalse();

        // The locked runtime is a monolithic build, so a library plugin aggregated into usd_ms
        // declares an empty LibraryPath and the registry reports it verbatim. A reported path is
        // therefore either empty or rooted; it is never a rewritten or guessed value.
        foreach (UsdPluginInfo plugin in plugins)
        {
            await Assert.That(plugin.Path.Length == 0 || Path.IsPathRooted(plugin.Path)).IsTrue();
        }

        // Each plugin keeps its own resource directory: the registry is never handed a merged
        // plugInfo.json, so every discovered plugin still resolves resources from its own tree.
        foreach (UsdPluginInfo plugin in plugins.Where(plugin => plugin.ResourcePath.Length > 0))
        {
            await Assert.That(Path.IsPathRooted(plugin.ResourcePath)).IsTrue();
        }
        await Assert.That(
                plugins
                    .Where(plugin => plugin.ResourcePath.Length > 0)
                    .Select(plugin => plugin.ResourcePath)
                    .Distinct(StringComparer.Ordinal)
                    .Count())
            .IsEqualTo(plugins.Count(plugin => plugin.ResourcePath.Length > 0));
    }

    [Test]
    public async Task RegisteringAVendorTreeKeepsItsOwnResourceDirectory()
    {
        NativeCoverageRuntime.EnsureNativeLoaded();

        string root = NativeCoverageRuntime.CreateTempDirectory(
            nameof(RegisteringAVendorTreeKeepsItsOwnResourceDirectory));
        string name = $"openusdCoverageVendor{Guid.NewGuid():N}";
        string resources = Path.Combine(root, name, "resources");
        Directory.CreateDirectory(resources);
        string plugInfoPath = Path.Combine(resources, "plugInfo.json");
        await File.WriteAllTextAsync(
            plugInfoPath,
            $$"""
            {
                "Plugins": [
                    {
                        "Info": {},
                        "Name": "{{name}}",
                        "ResourcePath": "resources",
                        "Root": "..",
                        "Type": "resource"
                    }
                ]
            }
            """);

        // The tree is registered where it lies: nothing is merged into another plugInfo.json and
        // the vendor plugin keeps its own resource directory.
        await Assert.That(UsdPluginRegistry.Register(plugInfoPath)).IsEqualTo(1);
        await Assert.That(UsdPluginRegistry.Register(plugInfoPath)).IsEqualTo(0);

        UsdPluginInfo vendor = UsdPluginRegistry
            .GetRegisteredPlugins()
            .Single(plugin => plugin.Name == name);

        await Assert.That(vendor.Kind).IsEqualTo(UsdPluginKind.Resource);

        // A resource plugin has no library to load, so the registry reports it loaded as soon as
        // it is discovered.
        await Assert.That(vendor.IsLoaded).IsTrue();
        // The reported resource path is the vendor's own directory. Upstream reports it with
        // forward slashes and a trailing separator, so it is normalized before comparison.
        string reportedResources = Path.GetFullPath(vendor.ResourcePath.TrimEnd('/', '\\'));
        await Assert.That(
                string.Equals(
                    reportedResources,
                    Path.GetFullPath(resources),
                    StringComparison.OrdinalIgnoreCase))
            .IsTrue();
        await Assert.That(await File.ReadAllTextAsync(plugInfoPath)).Contains(name);
    }

    [Test]
    public async Task ResolverDiscoveryReportsThePrimaryResolverAndItsTypes()
    {
        NativeCoverageRuntime.EnsureNativeLoaded();

        await Assert.That(UsdResolver.PrimaryTypeName).IsEqualTo("ArDefaultResolver");

        // Available types are the primary-resolver candidates only. Upstream excludes every
        // resolver that declares URI/IRI schemes, and always ends the list with the default
        // resolver, so the two lists are disjoint discovery signals rather than one list.
        IReadOnlyList<string> types = UsdResolver.GetAvailableResolverTypeNames();
        IReadOnlyList<string> schemes = UsdResolver.GetRegisteredUriSchemes();

        await Assert.That(types).Contains("ArDefaultResolver");
        await Assert.That(types.Contains(UsdResolver.PrimaryTypeName)).IsTrue();
        await Assert.That(schemes.All(
                scheme => scheme.Length > 0 &&
                    string.Equals(scheme, scheme.ToLowerInvariant(), StringComparison.Ordinal)))
            .IsTrue();
    }

    [Test]
    public async Task BulkResolutionReportsResolvedAndUnresolvedAssets()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(BulkResolutionReportsResolvedAndUnresolvedAssets));
        string assetName = $"resolver-coverage-{Guid.NewGuid():N}.usda";
        string assetPath = Path.Combine(directory, assetName);
        using (UsdStage stage = UsdStage.Create(assetPath))
        {
            stage.DefinePrim("/Root", "Xform");
            stage.Save();
        }

        using UsdResolverContext context = UsdResolverContext.Create(
            [new UsdResolverContextString(string.Empty, directory)]);

        await Assert.That(context.IsEmpty).IsFalse();
        await Assert.That(context.DebugString.Length).IsGreaterThan(0);

        IReadOnlyList<UsdResolvedAsset> resolved = UsdResolver.Resolve(
            [assetName, $"missing-{Guid.NewGuid():N}.usda", assetPath],
            context);

        await Assert.That(resolved.Count).IsEqualTo(3);
        await Assert.That(resolved[0].IsResolved).IsTrue();
        await Assert.That(resolved[0].AssetPath).IsEqualTo(assetName);
        await Assert.That(Path.GetFullPath(resolved[0].ResolvedPath))
            .IsEqualTo(Path.GetFullPath(assetPath));
        await Assert.That(resolved[0].Extension).IsEqualTo("usda");
        await Assert.That(resolved[0].IsContextDependent).IsTrue();
        await Assert.That(resolved[0].ModificationTime.HasValue).IsTrue();
        await Assert.That(resolved[1].IsResolved).IsFalse();
        await Assert.That(resolved[1].ResolvedPath).IsEqualTo(string.Empty);
        await Assert.That(resolved[1].ModificationTime.HasValue).IsFalse();
        await Assert.That(resolved[2].IsResolved).IsTrue();
        await Assert.That(resolved[2].IsContextDependent).IsFalse();

        // Without the context the search path is gone, so the relative identifier no longer
        // resolves while the absolute one still does.
        IReadOnlyList<UsdResolvedAsset> withoutContext = UsdResolver.Resolve([assetName, assetPath]);
        await Assert.That(withoutContext[0].IsResolved).IsFalse();
        await Assert.That(withoutContext[1].IsResolved).IsTrue();
    }

    [Test]
    public async Task BindingAContextScopesResolutionToTheCallingThread()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(BindingAContextScopesResolutionToTheCallingThread));
        string assetName = $"resolver-binding-{Guid.NewGuid():N}.usda";
        using (UsdStage stage = UsdStage.Create(Path.Combine(directory, assetName)))
        {
            stage.Save();
        }

        using UsdResolverContext context = UsdResolverContext.Create(
            [new UsdResolverContextString(string.Empty, directory)]);

        // A binding is thread local, so nothing may await while it is held: a continuation can
        // resume on another thread, which would both resolve without the binding and fail to
        // release it. Every result is captured inside the scope and asserted after it.
        bool boundResolved;
        using (context.Bind())
        {
            boundResolved = UsdResolver.Resolve([assetName])[0].IsResolved;
        }

        bool unboundResolved = UsdResolver.Resolve([assetName])[0].IsResolved;

        await Assert.That(boundResolved).IsTrue();
        await Assert.That(unboundResolved).IsFalse();
    }

    [Test]
    public async Task ARejectedBindingReleaseStaysRetryableOnTheOwnerThread()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(ARejectedBindingReleaseStaysRetryableOnTheOwnerThread));
        string assetName = $"resolver-retry-{Guid.NewGuid():N}.usda";
        using (UsdStage stage = UsdStage.Create(Path.Combine(directory, assetName)))
        {
            stage.Save();
        }

        using UsdResolverContext context = UsdResolverContext.Create(
            [new UsdResolverContextString(string.Empty, directory)]);

        Exception? failure = null;
        bool resolvedAfterRejectedRelease;
        bool resolvedAfterOwnerRelease;
        UsdResolverContextBinding binding = context.Bind();
        try
        {
            var thread = new Thread(() =>
            {
                try
                {
                    binding.Dispose();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            thread.Start();
            thread.Join();

            // The rejected release must not have consumed the binding: the context is still bound
            // on this thread and the binding is still owned, so it can be released here.
            resolvedAfterRejectedRelease = UsdResolver.Resolve([assetName])[0].IsResolved;
        }
        finally
        {
            binding.Dispose();
        }

        resolvedAfterOwnerRelease = UsdResolver.Resolve([assetName])[0].IsResolved;

        // Disposing again after a successful release is still a no-op.
        binding.Dispose();

        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        await Assert.That(resolvedAfterRejectedRelease).IsTrue();
        await Assert.That(resolvedAfterOwnerRelease).IsFalse();
    }

    [Test]
    public async Task AnOutOfOrderBindingReleaseStaysRetryableInOrder()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(AnOutOfOrderBindingReleaseStaysRetryableInOrder));
        string assetName = $"resolver-order-{Guid.NewGuid():N}.usda";
        using (UsdStage stage = UsdStage.Create(Path.Combine(directory, assetName)))
        {
            stage.Save();
        }

        using UsdResolverContext context = UsdResolverContext.Create(
            [new UsdResolverContextString(string.Empty, directory)]);
        using UsdResolverContext empty = UsdResolverContext.CreateDefault();

        Exception? failure = null;
        bool shadowedAfterRejectedRelease;
        bool restoredAfterInnerRelease;
        bool resolvedAfterOuterRelease;
        UsdResolverContextBinding outer = context.Bind();
        try
        {
            UsdResolverContextBinding inner = empty.Bind();
            try
            {
                // The outer binding is not the newest one on this thread, so releasing it now is
                // rejected. The rejection must leave both bindings owned and still bound instead
                // of forgetting a handle the native side still holds.
                try
                {
                    outer.Dispose();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                shadowedAfterRejectedRelease = UsdResolver.Resolve([assetName])[0].IsResolved;
            }
            finally
            {
                inner.Dispose();
            }

            restoredAfterInnerRelease = UsdResolver.Resolve([assetName])[0].IsResolved;
        }
        finally
        {
            outer.Dispose();
        }

        resolvedAfterOuterRelease = UsdResolver.Resolve([assetName])[0].IsResolved;

        // The out-of-order release is a caller mistake, not a native failure, so it surfaces as
        // InvalidOperationException exactly like a wrong-thread release does.
        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        await Assert.That(shadowedAfterRejectedRelease).IsFalse();
        await Assert.That(restoredAfterInnerRelease).IsTrue();
        await Assert.That(resolvedAfterOuterRelease).IsFalse();
    }

    [Test]
    public async Task RefreshingABoundContextIsRejectedAndRetryable()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(RefreshingABoundContextIsRejectedAndRetryable));
        using UsdResolverContext context = UsdResolverContext.Create(
            [new UsdResolverContextString(string.Empty, directory)]);
        using UsdResolverContext unrelated = UsdResolverContext.Create(
            [new UsdResolverContextString(string.Empty, Path.Combine(directory, "other"))]);

        Exception? sameThreadFailure = null;
        Exception? otherThreadFailure = null;
        Exception? unrelatedFailure = null;
        using (context.Bind())
        {
            try
            {
                context.Refresh();
            }
            catch (Exception exception)
            {
                sameThreadFailure = exception;
            }

            // The registry of bound contexts is process-wide, so a context bound here also blocks
            // a refresh issued from another thread.
            var thread = new Thread(() =>
            {
                try
                {
                    context.Refresh();
                }
                catch (Exception exception)
                {
                    otherThreadFailure = exception;
                }
            });
            thread.Start();
            thread.Join();

            // The rejection is scoped to this context by value identity: an unrelated context is
            // not blocked just because some binding is live.
            try
            {
                unrelated.Refresh();
            }
            catch (Exception exception)
            {
                unrelatedFailure = exception;
            }
        }

        // Rejection is retryable, not permanent: the same call succeeds once unbound.
        context.Refresh();

        await Assert.That(sameThreadFailure).IsTypeOf<OpenUsdNativeException>();
        await Assert.That(otherThreadFailure).IsTypeOf<OpenUsdNativeException>();
        await Assert.That(unrelatedFailure).IsNull();
    }

    [Test]
    public async Task ResolvedRecordsAlwaysCarryAnIdentifier()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(ResolvedRecordsAlwaysCarryAnIdentifier));
        string assetName = $"resolver-identifier-{Guid.NewGuid():N}.usda";
        string assetPath = Path.Combine(directory, assetName);
        using (UsdStage stage = UsdStage.Create(assetPath))
        {
            stage.DefinePrim("/Root", "Xform");
            stage.Save();
        }

        using UsdResolverContext context = UsdResolverContext.Create(
            [new UsdResolverContextString(string.Empty, directory)]);

        IReadOnlyList<UsdResolvedAsset> resolved = UsdResolver.Resolve(
            [assetName, assetPath, $"missing-{Guid.NewGuid():N}.usda"],
            context);

        // A record is only ever reported resolved when the resolver also produced an identifier
        // for it. An asset the resolver declines to identify is unusable to composition, so the
        // shim reports it unresolved instead of falling back to resolving the raw asset path,
        // which would disagree with a stage opened against the same context.
        //
        // ArDefaultResolver identifies every non-empty path, so the discriminating case needs a
        // URI resolver that refuses one: the CTest native probe covers it against
        // native/tests/resolver_plugin, which returns an empty identifier for an asset whose file
        // exists and would otherwise resolve.
        await Assert.That(
                resolved.All(asset => !asset.IsResolved || asset.Identifier.Length > 0))
            .IsTrue();
        await Assert.That(resolved[0].IsResolved).IsTrue();
        await Assert.That(resolved[1].IsResolved).IsTrue();
        await Assert.That(resolved[2].IsResolved).IsFalse();
        await Assert.That(resolved[2].ResolvedPath).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task AnEmptyContextShadowsTheAmbientBinding()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(AnEmptyContextShadowsTheAmbientBinding));
        string assetName = $"resolver-shadow-{Guid.NewGuid():N}.usda";
        using (UsdStage stage = UsdStage.Create(Path.Combine(directory, assetName)))
        {
            stage.Save();
        }

        using UsdResolverContext context = UsdResolverContext.Create(
            [new UsdResolverContextString(string.Empty, directory)]);
        using UsdResolverContext empty = UsdResolverContext.CreateDefault();

        bool emptyIsEmpty = empty.IsEmpty;
        bool ambientResolved;
        bool shadowedResolved;
        bool ambientSurvivedShadowing;
        bool nestedResolved;
        bool restoredAfterNesting;
        using (context.Bind())
        {
            ambientResolved = UsdResolver.Resolve([assetName])[0].IsResolved;

            // An empty context is bound like any other, so it shadows the ambient binding for the
            // batch instead of silently resolving against it. Passing null keeps the ambient one.
            shadowedResolved = UsdResolver.Resolve([assetName], empty)[0].IsResolved;
            ambientSurvivedShadowing = UsdResolver.Resolve([assetName])[0].IsResolved;

            using (empty.Bind())
            {
                nestedResolved = UsdResolver.Resolve([assetName])[0].IsResolved;
            }

            restoredAfterNesting = UsdResolver.Resolve([assetName])[0].IsResolved;
        }

        await Assert.That(emptyIsEmpty).IsTrue();
        await Assert.That(ambientResolved).IsTrue();
        await Assert.That(shadowedResolved).IsFalse();
        await Assert.That(ambientSurvivedShadowing).IsTrue();
        await Assert.That(nestedResolved).IsFalse();
        await Assert.That(restoredAfterNesting).IsTrue();
    }

    [Test]
    public async Task DefaultAndAssetContextsMatchUpstreamBehavior()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(DefaultAndAssetContextsMatchUpstreamBehavior));
        string assetPath = Path.Combine(directory, "context-for-asset.usda");
        using (UsdStage stage = UsdStage.Create(assetPath))
        {
            stage.Save();
        }

        using UsdResolverContext defaultContext = UsdResolverContext.CreateDefault();
        using UsdResolverContext assetContext = UsdResolverContext.CreateForAsset(assetPath);

        await Assert.That(assetContext.IsEmpty).IsFalse();
        await Assert.That(assetContext.DebugString.Length).IsGreaterThan(0);
        defaultContext.Refresh();
        await Assert.That(defaultContext.DebugString).IsNotNull();
    }

    [Test]
    public async Task OpeningAStageWithAContextComposesThroughTheSameResolution()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(OpeningAStageWithAContextComposesThroughTheSameResolution));
        string referencedName = $"resolver-referenced-{Guid.NewGuid():N}.usda";
        using (UsdStage referenced = UsdStage.Create(Path.Combine(directory, referencedName)))
        {
            referenced.DefinePrim("/Target", "Xform");
            referenced.Save();
        }

        string hostDirectory = Path.Combine(directory, "host");
        Directory.CreateDirectory(hostDirectory);
        string hostPath = Path.Combine(hostDirectory, "host.usda");
        using (UsdStage host = UsdStage.Create(hostPath))
        {
            UsdPrim prim = host.DefinePrim("/Model", "Xform");
            prim.AddReference(referencedName, "/Target");
            host.Save();
        }

        using UsdResolverContext context = UsdResolverContext.Create(
            [new UsdResolverContextString(string.Empty, directory)]);
        using UsdStage composed = UsdStage.Open(hostPath, context);

        await Assert.That(composed.Traverse().Any(prim => prim.Path == "/Model")).IsTrue();
        PcpPrimIndex index = composed.GetPrim("/Model").GetPrimIndex();
        await Assert.That(index.Nodes.Any(node => node.ArcType == PcpArcType.Reference)).IsTrue();
        await Assert.That(index.Errors.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ResolverRequestsRejectInvalidArguments()
    {
        NativeCoverageRuntime.EnsureNativeLoaded();

        await Assert.That(() => UsdResolver.Resolve(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => UsdResolver.Resolve([string.Empty])).Throws<ArgumentException>();
        await Assert.That(() => UsdResolverContext.Create(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => UsdPluginRegistry.Register("  ")).Throws<ArgumentException>();
        await Assert.That(() => UsdResolverContext.CreateForAsset(string.Empty))
            .Throws<ArgumentException>();

        UsdResolverContext context = UsdResolverContext.CreateDefault();
        context.Dispose();
        await Assert.That(() => context.DebugString).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task EmptyResolutionRequestsReturnEmptyResults()
    {
        NativeCoverageRuntime.EnsureNativeLoaded();

        IReadOnlyList<UsdResolvedAsset> resolved = UsdResolver.Resolve([]);

        await Assert.That(resolved.Count).IsEqualTo(0);
    }
}
