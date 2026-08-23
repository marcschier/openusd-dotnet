// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Asserts the production transport adapter's metadata cache. The capability matrix and the
/// diagnostic list are read once per painted frame, so rebuilding either of them per read allocated
/// at frame rate and handed every downstream identity check a new reference, which rebuilt the whole
/// inspector under the operator about a hundred times a second.
/// </summary>
public sealed class ViewerPhysicsMetadataCacheTests
{
    [Test]
    public async Task TheSameCapabilityFlagsAlwaysReturnTheSameRows()
    {
        var cache = new ViewerPhysicsMetadataCache();
        const UsdPhysicsCapability features =
            UsdPhysicsCapability.RigidBodies | UsdPhysicsCapability.Articulations;

        IReadOnlyList<ViewerPhysicsCapabilitySupport> first = cache.GetCapabilities(features);
        for (int index = 0; index < 256; index++)
        {
            await Assert.That(ReferenceEquals(cache.GetCapabilities(features), first)).IsTrue();
        }

        await Assert.That(first.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task TheCapabilityRowsDescribeExactlyTheFlagsThatAreSet()
    {
        var cache = new ViewerPhysicsMetadataCache();

        IReadOnlyList<ViewerPhysicsCapabilitySupport> rows =
            cache.GetCapabilities(UsdPhysicsCapability.RigidBodies);

        ViewerPhysicsCapabilitySupport rigid = Find(rows, nameof(UsdPhysicsCapability.RigidBodies));
        ViewerPhysicsCapabilitySupport cloth = Find(rows, nameof(UsdPhysicsCapability.Cloth));

        await Assert.That(rigid.IsSupported).IsTrue();
        await Assert.That(rigid.Domain).IsEqualTo(PhysicsRenderDomain.RigidBody);
        await Assert.That(rigid.Detail).Contains("simulates");
        await Assert.That(cloth.IsSupported).IsFalse();
        await Assert.That(cloth.Detail).Contains("does not provide");
        await Assert.That(rows.Any(row => row.Name == nameof(UsdPhysicsCapability.None))).IsFalse();
        await Assert.That(rows.Any(row => row.Name == nameof(UsdPhysicsCapability.All))).IsFalse();
    }

    [Test]
    public async Task AChangedFeatureBitPublishesNewRowsInsteadOfHidingBehindTheCache()
    {
        var cache = new ViewerPhysicsMetadataCache();

        IReadOnlyList<ViewerPhysicsCapabilitySupport> before =
            cache.GetCapabilities(UsdPhysicsCapability.RigidBodies);
        IReadOnlyList<ViewerPhysicsCapabilitySupport> after = cache.GetCapabilities(
            UsdPhysicsCapability.RigidBodies | UsdPhysicsCapability.Cloth);

        await Assert.That(ReferenceEquals(before, after)).IsFalse();
        await Assert.That(Find(before, nameof(UsdPhysicsCapability.Cloth)).IsSupported).IsFalse();
        await Assert.That(Find(after, nameof(UsdPhysicsCapability.Cloth)).IsSupported).IsTrue();

        // Going back to the previous flags builds rows again rather than returning stale ones.
        IReadOnlyList<ViewerPhysicsCapabilitySupport> back =
            cache.GetCapabilities(UsdPhysicsCapability.RigidBodies);
        await Assert.That(Find(back, nameof(UsdPhysicsCapability.Cloth)).IsSupported).IsFalse();
    }

    [Test]
    public async Task TheSameDiagnosticSetAlwaysReturnsTheSameRows()
    {
        var cache = new ViewerPhysicsMetadataCache();
        UsdPhysicsDiagnostics diagnostics = Diagnostics(("CODE_A", "The world was built."));

        IReadOnlyList<ViewerPhysicsDiagnosticRow> first = cache.GetDiagnostics(diagnostics);
        for (int index = 0; index < 256; index++)
        {
            await Assert.That(ReferenceEquals(cache.GetDiagnostics(diagnostics), first)).IsTrue();
        }

        await Assert.That(first.Count).IsEqualTo(1);
        await Assert.That(first[0].Code).IsEqualTo("CODE_A");
        await Assert.That(first[0].Message).IsEqualTo("The world was built.");
    }

    [Test]
    public async Task ARebuiltDiagnosticSetWithIdenticalEntriesDoesNotChurnTheInspector()
    {
        var cache = new ViewerPhysicsMetadataCache();

        IReadOnlyList<ViewerPhysicsDiagnosticRow> first =
            cache.GetDiagnostics(Diagnostics(("CODE_A", "The world was built.")));
        for (int index = 0; index < 64; index++)
        {
            // A different instance carrying the same entries is the same content to the operator.
            IReadOnlyList<ViewerPhysicsDiagnosticRow> repeated =
                cache.GetDiagnostics(Diagnostics(("CODE_A", "The world was built.")));
            await Assert.That(ReferenceEquals(repeated, first)).IsTrue();
        }
    }

    [Test]
    public async Task EveryKindOfDiagnosticChangeIsPublished()
    {
        var cache = new ViewerPhysicsMetadataCache();
        IReadOnlyList<ViewerPhysicsDiagnosticRow> baseline =
            cache.GetDiagnostics(Diagnostics(("CODE_A", "The world was built.")));

        IReadOnlyList<ViewerPhysicsDiagnosticRow> changedMessage =
            cache.GetDiagnostics(Diagnostics(("CODE_A", "The world was rebuilt.")));
        await Assert.That(ReferenceEquals(changedMessage, baseline)).IsFalse();
        await Assert.That(changedMessage[0].Message).IsEqualTo("The world was rebuilt.");

        IReadOnlyList<ViewerPhysicsDiagnosticRow> changedCode =
            cache.GetDiagnostics(Diagnostics(("CODE_B", "The world was rebuilt.")));
        await Assert.That(ReferenceEquals(changedCode, changedMessage)).IsFalse();
        await Assert.That(changedCode[0].Code).IsEqualTo("CODE_B");

        IReadOnlyList<ViewerPhysicsDiagnosticRow> added = cache.GetDiagnostics(Diagnostics(
            ("CODE_B", "The world was rebuilt."),
            ("CODE_C", "A collider was skipped.")));
        await Assert.That(ReferenceEquals(added, changedCode)).IsFalse();
        await Assert.That(added.Count).IsEqualTo(2);

        IReadOnlyList<ViewerPhysicsDiagnosticRow> removed =
            cache.GetDiagnostics(UsdPhysicsDiagnostics.Empty);
        await Assert.That(ReferenceEquals(removed, added)).IsFalse();
        await Assert.That(removed).IsEmpty();

        // Severity is part of the content even when every string is identical.
        var warning = new UsdPhysicsDiagnostics([
            new UsdPhysicsDiagnostic(
                UsdPhysicsDiagnosticSeverity.Warning,
                UsdPhysicsDiagnosticCategory.Build,
                "CODE_D",
                "Same words."),
        ]);
        var error = new UsdPhysicsDiagnostics([
            new UsdPhysicsDiagnostic(
                UsdPhysicsDiagnosticSeverity.Error,
                UsdPhysicsDiagnosticCategory.Build,
                "CODE_D",
                "Same words."),
        ]);
        IReadOnlyList<ViewerPhysicsDiagnosticRow> asWarning = cache.GetDiagnostics(warning);
        IReadOnlyList<ViewerPhysicsDiagnosticRow> asError = cache.GetDiagnostics(error);

        await Assert.That(ReferenceEquals(asError, asWarning)).IsFalse();
        await Assert.That(asWarning[0].Severity).IsEqualTo("Warning");
        await Assert.That(asError[0].Severity).IsEqualTo("Error");
    }

    [Test]
    [NotInParallel("ViewerPhysicsAllocation")]
    public async Task WarmMetadataReadsAllocateNothing()
    {
        var cache = new ViewerPhysicsMetadataCache();
        const UsdPhysicsCapability features =
            UsdPhysicsCapability.RigidBodies | UsdPhysicsCapability.Articulations;
        UsdPhysicsDiagnostics diagnostics = Diagnostics(("CODE_A", "The world was built."));

        int warmed = 0;
        _ = AllocationWarmup.UntilQuiet(_ =>
            warmed += cache.GetCapabilities(features).Count +
                cache.GetDiagnostics(diagnostics).Count);
        await Assert.That(warmed).IsGreaterThan(0);

        // The measured loop lives in its own method so its first execution pays for whatever tiered
        // or on-stack-replacement compilation it needs; the assertion stays at exactly zero.
        long allocated = MeasureWarmReads(cache, features, diagnostics, out int total);
        if (allocated != 0)
        {
            allocated = MeasureWarmReads(cache, features, diagnostics, out total);
        }

        await Assert.That(total).IsGreaterThan(0);
        await Assert.That(allocated).IsEqualTo(0L);
    }

    private static long MeasureWarmReads(
        ViewerPhysicsMetadataCache cache,
        UsdPhysicsCapability features,
        UsdPhysicsDiagnostics diagnostics,
        out int total)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        int count = 0;
        for (int index = 0; index < 1000; index++)
        {
            count += cache.GetCapabilities(features).Count;
            count += cache.GetDiagnostics(diagnostics).Count;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        total = count;
        return allocated;
    }

    [Test]
    [NotInParallel("ViewerPhysicsAllocation")]
    public async Task ADiagnosticSetRebuiltEveryReadStillCostsNothingWarm()
    {
        // The retained transport publishes a new set whenever an operation reports, so the identical
        // content path has to be as cheap as the identity path or a chatty operation would allocate
        // once per painted frame.
        var cache = new ViewerPhysicsMetadataCache();
        var sets = new UsdPhysicsDiagnostics[64];
        for (int index = 0; index < sets.Length; index++)
        {
            sets[index] = Diagnostics(("CODE_A", "The world was built."));
        }

        int warmed = 0;
        _ = AllocationWarmup.UntilQuiet(iteration =>
            warmed += cache.GetDiagnostics(sets[iteration % sets.Length]).Count);
        await Assert.That(warmed).IsGreaterThan(0);

        long allocated = MeasureRotatingReads(cache, sets, out int total);
        if (allocated != 0)
        {
            allocated = MeasureRotatingReads(cache, sets, out total);
        }

        await Assert.That(total).IsEqualTo(1000);
        await Assert.That(allocated).IsEqualTo(0L);
    }

    private static long MeasureRotatingReads(
        ViewerPhysicsMetadataCache cache,
        UsdPhysicsDiagnostics[] sets,
        out int total)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        int count = 0;
        for (int index = 0; index < 1000; index++)
        {
            count += cache.GetDiagnostics(sets[index % sets.Length]).Count;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        total = count;
        return allocated;
    }

    [Test]
    public async Task ConcurrentReadersNeverSeeAPartiallyBuiltMatrix()
    {
        var cache = new ViewerPhysicsMetadataCache();
        var flags = new[]
        {
            UsdPhysicsCapability.RigidBodies,
            UsdPhysicsCapability.RigidBodies | UsdPhysicsCapability.Cloth,
            UsdPhysicsCapability.Vehicles,
        };

        Parallel.For(0, 512, index =>
        {
            UsdPhysicsCapability features = flags[index % flags.Length];
            IReadOnlyList<ViewerPhysicsCapabilitySupport> rows = cache.GetCapabilities(features);
            foreach (ViewerPhysicsCapabilitySupport row in rows)
            {
                bool expected = (features & Enum.Parse<UsdPhysicsCapability>(row.Name)) != 0;
                if (row.IsSupported != expected)
                {
                    throw new InvalidOperationException(
                        $"Row '{row.Name}' did not describe the flags it was requested for.");
                }
            }
        });

        await Assert.That(cache.GetCapabilities(UsdPhysicsCapability.Vehicles).Count)
            .IsGreaterThan(0);
    }

    private static ViewerPhysicsCapabilitySupport Find(
        IReadOnlyList<ViewerPhysicsCapabilitySupport> rows,
        string name)
    {
        foreach (ViewerPhysicsCapabilitySupport row in rows)
        {
            if (row.Name == name)
            {
                return row;
            }
        }

        throw new InvalidOperationException($"The capability matrix has no '{name}' row.");
    }

    private static UsdPhysicsDiagnostics Diagnostics(params (string Code, string Message)[] entries)
    {
        var rows = new UsdPhysicsDiagnostic[entries.Length];
        for (int index = 0; index < entries.Length; index++)
        {
            rows[index] = new UsdPhysicsDiagnostic(
                UsdPhysicsDiagnosticSeverity.Warning,
                UsdPhysicsDiagnosticCategory.Build,
                entries[index].Code,
                entries[index].Message);
        }

        return new UsdPhysicsDiagnostics(rows);
    }
}
