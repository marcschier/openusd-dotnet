// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkPickIdentityTableTests
{
    private const int LookupIterations = 1000;

    /// <summary>
    /// Page ABI 3 publishes one record per resolved instance of a prototype, so identity is
    /// (path, instance index). Keying by path alone made the second instance look like the
    /// same mesh changing identity and threw out of the scene state.
    /// </summary>
    [Test]
    public async Task InstancesOfOnePrototypeKeepDistinctIdentities()
    {
        var table = new SilkPickIdentityTable();
        SilkPickTokenRange first = table.Upsert(CreateMesh(
            "/Proto",
            primId: 5,
            topologyRevision: 1,
            triangleSubprims: [1, 2],
            instanceId: 9,
            instanceIndex: 0));
        SilkPickTokenRange second = table.Upsert(CreateMesh(
            "/Proto",
            primId: 5,
            topologyRevision: 1,
            triangleSubprims: [1, 2],
            instanceId: 9,
            instanceIndex: 1));

        await Assert.That(second.FirstToken).IsGreaterThan(first.LastToken);
        await Assert.That(table.ActiveRangeCount).IsEqualTo(2);

        await Assert.That(table.TryResolve(
            first.FirstToken,
            out SilkPickIdentity firstIdentity)).IsTrue();
        await Assert.That(firstIdentity.InstanceIndex).IsEqualTo(0);
        await Assert.That(table.TryResolve(
            second.FirstToken,
            out SilkPickIdentity secondIdentity)).IsTrue();
        await Assert.That(secondIdentity.InstanceIndex).IsEqualTo(1);
        await Assert.That(secondIdentity.InstanceId).IsEqualTo(9);
        await Assert.That(secondIdentity.Path).IsEqualTo("/Proto");

        await Assert.That(table.TryGetRange(
            "/Proto",
            1,
            out SilkPickTokenRange instanceRange)).IsTrue();
        await Assert.That(instanceRange).IsEqualTo(second);
        await Assert.That(table.TryGetRange(
            "/Proto",
            out SilkPickTokenRange defaultRange)).IsTrue();
        await Assert.That(defaultRange).IsEqualTo(first);
    }

    [Test]
    public async Task RemovingOneInstanceLeavesTheOthersResolvable()
    {
        var table = new SilkPickIdentityTable();
        SilkPickTokenRange first = table.Upsert(CreateMesh(
            "/Proto",
            primId: 5,
            topologyRevision: 1,
            triangleSubprims: [1],
            instanceIndex: 0));
        SilkPickTokenRange second = table.Upsert(CreateMesh(
            "/Proto",
            primId: 5,
            topologyRevision: 1,
            triangleSubprims: [1],
            instanceIndex: 1));

        await Assert.That(table.Remove("/Proto", 0)).IsTrue();
        await Assert.That(table.TryGetRange("/Proto", 0, out _)).IsFalse();
        await Assert.That(table.TryResolve(first.FirstToken, out _)).IsFalse();
        await Assert.That(table.TryGetRange("/Proto", 1, out _)).IsTrue();
        await Assert.That(table.TryResolve(second.FirstToken, out _)).IsTrue();
        await Assert.That(table.ActiveRangeCount).IsEqualTo(1);

        await Assert.That(table.Remove("/Proto", 1)).IsTrue();
        await Assert.That(table.ActiveRangeCount).IsEqualTo(0);
        await Assert.That(table.Remove("/Proto", 1)).IsFalse();
    }

    [Test]
    public async Task PropertyUpdateKeepsRangeAndTopologyUpdateRebuildsIt()
    {
        var table = new SilkPickIdentityTable();
        SilkPickTokenRange first = table.Upsert(CreateMesh(
            "/Mesh",
            primId: 7,
            topologyRevision: 1,
            triangleSubprims: [3, 9],
            color: [1, 0, 0, 1]));
        SilkPickTokenRange property = table.Upsert(CreateMesh(
            "/Mesh",
            primId: 7,
            topologyRevision: 1,
            triangleSubprims: [3, 9],
            color: [0, 1, 0, 1]));

        await Assert.That(table.Revision).IsEqualTo(1ul);
        await Assert.That(property).IsEqualTo(first);
        await Assert.That(table.AllocatedRangeCount).IsEqualTo(1ul);
        await Assert.That(table.TryResolve(
            first.FirstToken + 1,
            out SilkPickIdentity propertyIdentity)).IsTrue();
        await Assert.That(propertyIdentity.Path).IsEqualTo("/Mesh");
        await Assert.That(propertyIdentity.PrimId).IsEqualTo(7);
        await Assert.That(propertyIdentity.StableHash)
            .IsEqualTo(SilkWireFormat.ComputeStableHash("/Mesh"));
        await Assert.That(propertyIdentity.InstanceId).IsEqualTo(0);
        await Assert.That(propertyIdentity.InstanceIndex).IsEqualTo(0);
        await Assert.That(propertyIdentity.TopologyRevision).IsEqualTo(1ul);
        await Assert.That(propertyIdentity.SubprimIndex).IsEqualTo(9);

        SilkPickTokenRange topology = table.Upsert(CreateMesh(
            "/Mesh",
            primId: 7,
            topologyRevision: 2,
            triangleSubprims: [12]));

        await Assert.That(table.Revision).IsEqualTo(2ul);
        await Assert.That(topology.FirstToken).IsGreaterThan(first.LastToken);
        await Assert.That(table.AllocatedRangeCount).IsEqualTo(2ul);
        await Assert.That(table.ActiveRangeCount).IsEqualTo(1);
        await Assert.That(table.TryResolve(first.FirstToken, out _)).IsFalse();
        await Assert.That(table.TryResolve(
            topology.FirstToken,
            out SilkPickIdentity topologyIdentity)).IsTrue();
        await Assert.That(topologyIdentity.TopologyRevision).IsEqualTo(2ul);
        await Assert.That(topologyIdentity.SubprimIndex).IsEqualTo(12);
    }

    [Test]
    public async Task RejectsIdentityCollisionsAndUnrevisionedTopologyChanges()
    {
        var hashCollisionTable = new SilkPickIdentityTable(
            uint.MaxValue,
            _ => 100);
        _ = hashCollisionTable.Upsert(CreateMesh(
            "/First",
            primId: 1,
            topologyRevision: 1,
            triangleSubprims: [0],
            stableHash: 100));

        await Assert.That(() => hashCollisionTable.Upsert(CreateMesh(
                "/HashCollision",
                primId: 2,
                topologyRevision: 1,
                triangleSubprims: [0],
                stableHash: 100)))
            .Throws<InvalidDataException>();

        var primCollisionTable = new SilkPickIdentityTable();
        _ = primCollisionTable.Upsert(CreateMesh(
            "/First",
            primId: 1,
            topologyRevision: 1,
            triangleSubprims: [0]));
        await Assert.That(() => primCollisionTable.Upsert(CreateMesh(
                "/PrimCollision",
                primId: 1,
                topologyRevision: 1,
                triangleSubprims: [0])))
            .Throws<InvalidDataException>();
        await Assert.That(() => primCollisionTable.Upsert(CreateMesh(
                "/First",
                primId: 1,
                topologyRevision: 1,
                triangleSubprims: [1])))
            .Throws<InvalidDataException>();
        await Assert.That(() => new SilkPickIdentityTable().Upsert(CreateMesh(
                "/Mismatch",
                primId: 4,
                topologyRevision: 1,
                triangleSubprims: [0],
                stableHash: 1)))
            .Throws<InvalidDataException>();
        await Assert.That(() => new SilkPickIdentityTable().Upsert(CreateMesh(
                "/InvalidRevision",
                primId: 5,
                topologyRevision: 0,
                triangleSubprims: [0])))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task ImplicitRecreationReplacesIdentityAndAllocatesFreshRange()
    {
        var primChangeTable = new SilkPickIdentityTable();
        SilkPickTokenRange firstPrimRange = primChangeTable.Upsert(CreateMesh(
            "/PrimChange",
            primId: 7,
            topologyRevision: 5,
            triangleSubprims: [3]));
        SilkPickTokenRange changedPrimRange = primChangeTable.Upsert(CreateMesh(
            "/PrimChange",
            primId: 8,
            topologyRevision: 5,
            triangleSubprims: [4]));

        await Assert.That(primChangeTable.Revision).IsEqualTo(2ul);
        await Assert.That(changedPrimRange.FirstToken)
            .IsGreaterThan(firstPrimRange.LastToken);
        await Assert.That(primChangeTable.TryResolve(
            firstPrimRange.FirstToken,
            out _)).IsFalse();
        await Assert.That(primChangeTable.TryResolve(
            changedPrimRange.FirstToken,
            out SilkPickIdentity changedPrim)).IsTrue();
        await Assert.That(changedPrim.PrimId).IsEqualTo(8);
        await Assert.That(changedPrim.SubprimIndex).IsEqualTo(4);

        var revisionTable = new SilkPickIdentityTable();
        SilkPickTokenRange firstRevisionRange = revisionTable.Upsert(CreateMesh(
            "/RevisionReset",
            primId: 9,
            topologyRevision: 6,
            triangleSubprims: [5]));
        SilkPickTokenRange resetRevisionRange = revisionTable.Upsert(CreateMesh(
            "/RevisionReset",
            primId: 9,
            topologyRevision: 1,
            triangleSubprims: [6]));

        await Assert.That(revisionTable.Revision).IsEqualTo(2ul);
        await Assert.That(resetRevisionRange.FirstToken)
            .IsGreaterThan(firstRevisionRange.LastToken);
        await Assert.That(revisionTable.TryResolve(
            firstRevisionRange.FirstToken,
            out _)).IsFalse();
        await Assert.That(revisionTable.TryResolve(
            resetRevisionRange.FirstToken,
            out SilkPickIdentity resetRevision)).IsTrue();
        await Assert.That(resetRevision.TopologyRevision).IsEqualTo(1ul);
        await Assert.That(resetRevision.SubprimIndex).IsEqualTo(6);
    }

    [Test]
    public async Task RemoveReaddInvalidatesOldRangeAndTokenSpaceExhaustsCleanly()
    {
        var table = new SilkPickIdentityTable(maximumToken: 3);
        SilkPickTokenRange first = table.Upsert(CreateMesh(
            "/First",
            primId: 1,
            topologyRevision: 1,
            triangleSubprims: [4, 5]));

        await Assert.That(table.Remove("/First", 0)).IsTrue();
        await Assert.That(table.Revision).IsEqualTo(2ul);
        await Assert.That(table.ActiveRangeCount).IsEqualTo(0);
        await Assert.That(table.AllocatedRangeCount).IsEqualTo(1ul);
        await Assert.That(table.TryResolve(first.FirstToken, out _)).IsFalse();

        SilkPickTokenRange second = table.Upsert(CreateMesh(
            "/Second",
            primId: 2,
            topologyRevision: 1,
            triangleSubprims: [8]));
        await Assert.That(table.Revision).IsEqualTo(3ul);
        await Assert.That(table.ActiveRangeCount).IsEqualTo(1);
        await Assert.That(table.AllocatedRangeCount).IsEqualTo(2ul);
        await Assert.That(second.FirstToken).IsEqualTo(3u);
        await Assert.That(table.TryResolve(
            second.FirstToken,
            out SilkPickIdentity identity)).IsTrue();
        await Assert.That(identity.SubprimIndex).IsEqualTo(8);

        await Assert.That(() => table.Upsert(CreateMesh(
                "/Exhausted",
                primId: 3,
                topologyRevision: 1,
                triangleSubprims: [9])))
            .Throws<InvalidOperationException>();
        await Assert.That(table.Revision).IsEqualTo(3ul);
        await Assert.That(table.AllocatedRangeCount).IsEqualTo(2ul);
        await Assert.That(table.TryResolve(second.FirstToken, out _)).IsTrue();
    }

    [Test]
    public async Task SeededRandomizedUpsertsAndRemovalsPreserveIdentityInvariants()
    {
        const int operationCount = 500;
        var random = new Random(0x71C0_2026);
        var table = new SilkPickIdentityTable(maximumToken: 5_000);
        var active = new Dictionary<string, ExpectedPickState>(StringComparer.Ordinal);
        var retiredTokens = new List<uint>();
        ulong expectedRevision = 0;
        ulong expectedAllocatedRanges = 0;
        int nextPrimId = 100;
        int completedOperations = 0;

        for (int operation = 0; operation < operationCount; operation++)
        {
            string path = $"/Mesh_{random.Next(24)}";
            if (active.TryGetValue(path, out ExpectedPickState? existing))
            {
                int action = random.Next(100);
                if (action < 25)
                {
                    if (!table.Remove(path, 0))
                    {
                        throw new InvalidOperationException(
                            $"Active path '{path}' could not be removed.");
                    }
                    Retire(existing.Range, retiredTokens);
                    active.Remove(path);
                    expectedRevision++;
                }
                else if (action < 55)
                {
                    SilkPickTokenRange retained = table.Upsert(CreateMesh(
                        path,
                        existing.PrimId,
                        existing.TopologyRevision,
                        existing.Subprims,
                        color:
                        [
                            random.Next(2),
                            random.Next(2),
                            random.Next(2),
                            1,
                        ]));
                    if (retained != existing.Range)
                    {
                        throw new InvalidOperationException(
                            $"Property update for '{path}' changed its token range.");
                    }
                }
                else
                {
                    int[] subprims = CreateSubprims(random);
                    Retire(existing.Range, retiredTokens);
                    SilkPickTokenRange range = table.Upsert(CreateMesh(
                        path,
                        existing.PrimId,
                        existing.TopologyRevision + 1,
                        subprims));
                    active[path] = new ExpectedPickState(
                        existing.PrimId,
                        existing.TopologyRevision + 1,
                        subprims,
                        range);
                    expectedRevision++;
                    expectedAllocatedRanges++;
                }
            }
            else if (random.Next(100) < 20)
            {
                if (table.Remove(path, 0))
                {
                    throw new InvalidOperationException(
                        $"Missing path '{path}' was unexpectedly removed.");
                }
            }
            else
            {
                int[] subprims = CreateSubprims(random);
                int primId = nextPrimId++;
                SilkPickTokenRange range = table.Upsert(CreateMesh(
                    path,
                    primId,
                    topologyRevision: 1,
                    subprims));
                active.Add(
                    path,
                    new ExpectedPickState(primId, 1, subprims, range));
                expectedRevision++;
                expectedAllocatedRanges++;
            }

            VerifyPickInvariants(
                table,
                active,
                retiredTokens,
                expectedRevision,
                expectedAllocatedRanges);
            completedOperations++;
        }

        await Assert.That(completedOperations).IsEqualTo(operationCount);
    }

    [Test]
    public async Task SeededTokenExhaustionAndCollisionCasesFailAtomically()
    {
        var random = new Random(0x71C0_2026);
        int exhaustionCases = 0;
        for (uint maximumToken = 1; maximumToken <= 32; maximumToken++)
        {
            var table = new SilkPickIdentityTable(maximumToken);
            var ranges = new List<SilkPickTokenRange>();
            uint remaining = maximumToken;
            int pathIndex = 0;
            while (remaining != 0)
            {
                int triangleCount = random.Next(1, checked((int)Math.Min(4u, remaining)) + 1);
                int[] subprims = Enumerable.Range(0, triangleCount)
                    .Select(index => (pathIndex * 10) + index)
                    .ToArray();
                SilkPickTokenRange range = table.Upsert(CreateMesh(
                    $"/Token_{maximumToken}_{pathIndex}",
                    checked((int)((maximumToken * 100) + (uint)pathIndex)),
                    topologyRevision: 1,
                    subprims));
                ranges.Add(range);
                remaining -= checked((uint)triangleCount);
                pathIndex++;
            }

            ulong revision = table.Revision;
            ulong allocatedRanges = table.AllocatedRangeCount;
            _ = CaptureFailure<InvalidOperationException>(
                () => table.Upsert(CreateMesh(
                    $"/Token_{maximumToken}_Exhausted",
                    checked((int)((maximumToken * 100) + (uint)pathIndex)),
                    topologyRevision: 1,
                    triangleSubprims: [999])));
            if (table.Revision != revision ||
                table.AllocatedRangeCount != allocatedRanges)
            {
                throw new InvalidOperationException(
                    $"Token exhaustion at {maximumToken} mutated table state.");
            }
            foreach (SilkPickTokenRange range in ranges)
            {
                if (!table.TryResolve(range.FirstToken, out _))
                {
                    throw new InvalidOperationException(
                        $"Token exhaustion at {maximumToken} invalidated an active range.");
                }
            }
            exhaustionCases++;
        }

        const ulong collisionHash = 0xA5A5;
        var hashTable = new SilkPickIdentityTable(uint.MaxValue, _ => collisionHash);
        SilkPickTokenRange hashRange = hashTable.Upsert(CreateMesh(
            "/Hash_Base",
            primId: 1,
            topologyRevision: 1,
            triangleSubprims: [7],
            stableHash: collisionHash));
        var primTable = new SilkPickIdentityTable();
        SilkPickTokenRange primRange = primTable.Upsert(CreateMesh(
            "/Prim_Base",
            primId: 77,
            topologyRevision: 1,
            triangleSubprims: [8]));

        int collisionCases = 0;
        for (int collision = 0; collision < 64; collision++)
        {
            _ = CaptureFailure<InvalidDataException>(
                () => hashTable.Upsert(CreateMesh(
                    $"/Hash_{collision}",
                    primId: collision + 2,
                    topologyRevision: 1,
                    triangleSubprims: [collision],
                    stableHash: collisionHash)));
            _ = CaptureFailure<InvalidDataException>(
                () => primTable.Upsert(CreateMesh(
                    $"/Prim_{collision}",
                    primId: 77,
                    topologyRevision: 1,
                    triangleSubprims: [collision])));
            if (hashTable.Revision != 1 ||
                hashTable.AllocatedRangeCount != 1 ||
                !hashTable.TryResolve(hashRange.FirstToken, out _) ||
                primTable.Revision != 1 ||
                primTable.AllocatedRangeCount != 1 ||
                !primTable.TryResolve(primRange.FirstToken, out _))
            {
                throw new InvalidOperationException(
                    $"Collision case {collision} mutated authoritative table state.");
            }
            collisionCases += 2;
        }

        await Assert.That(exhaustionCases).IsEqualTo(32);
        await Assert.That(collisionCases).IsEqualTo(128);
    }

    [Test]
    public async Task SteadyLookupDoesNotAllocate()
    {
        var table = new SilkPickIdentityTable();
        SilkPickTokenRange range = table.Upsert(CreateMesh(
            "/Mesh",
            primId: 7,
            topologyRevision: 1,
            triangleSubprims: [3]));
        _ = table.TryResolve(range.FirstToken, out _);

        // Warm the loop before measuring. This is the only allocation test in
        // the suite whose measured region contains a long-running loop, and at
        // a thousand iterations the JIT performs on-stack replacement partway
        // through -- which allocates, on the measured thread, and is counted.
        // That made the assertion fail intermittently on hosted runners while
        // passing on a rerun of the identical commit. Running the loop first
        // moves the recompilation outside the measurement instead of loosening
        // the assertion, which stays at exactly zero.
        for (int warmup = 0; warmup < LookupIterations; warmup++)
        {
            _ = table.TryResolve(range.FirstToken, out _);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < LookupIterations; iteration++)
        {
            if (!table.TryResolve(range.FirstToken, out SilkPickIdentity identity) ||
                identity.SubprimIndex != 3)
            {
                throw new InvalidOperationException("The retained pick token did not resolve.");
            }
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0);
    }

    [Test]
    public async Task TopologyChurnDoesNotRetainMeshOrGeometryArrays()
    {
        var table = new SilkPickIdentityTable();
        (WeakReference[] references, uint firstToken, uint activeToken) =
            CreateTopologyChurn(table);

        for (int attempt = 0; attempt < 5 && references.Any(reference => reference.IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        await Assert.That(references.Any(reference => reference.IsAlive)).IsFalse();
        await Assert.That(table.AllocatedRangeCount).IsEqualTo(24ul);
        await Assert.That(table.ActiveRangeCount).IsEqualTo(1);
        await Assert.That(table.Revision).IsEqualTo(24ul);
        await Assert.That(table.TryResolve(firstToken, out _)).IsFalse();
        await Assert.That(table.TryResolve(activeToken, out _)).IsTrue();
    }

    [Test]
    public async Task LargeTransformAnimationUsesCachedTopologyFingerprint()
    {
        const int triangleCount = 20_000;
        SilkMeshData initial = CreateLargeMesh(
            "/Animated",
            topologyRevision: 1,
            triangleCount,
            translateX: 0);
        SilkMeshData transformed = CreateLargeMesh(
            "/Animated",
            topologyRevision: 1,
            triangleCount,
            translateX: 2);
        var table = new SilkPickIdentityTable();
        SilkPickTokenRange range = table.Upsert(initial);
        ulong comparisons = table.TopologyFingerprintComparisonCount;

        for (int frame = 0; frame < 1_000; frame++)
        {
            if (table.Upsert(transformed) != range)
            {
                throw new InvalidOperationException(
                    "A transform-only update changed the retained token range.");
            }
        }

        await Assert.That(initial.TopologyFingerprint)
            .IsEqualTo(transformed.TopologyFingerprint);
        await Assert.That(table.TopologyFingerprintComparisonCount - comparisons)
            .IsEqualTo(1_000ul);
        await Assert.That(table.Revision).IsEqualTo(1ul);
        await Assert.That(table.AllocatedRangeCount).IsEqualTo(1ul);
        await Assert.That(table.ActiveRangeCount).IsEqualTo(1);
    }

    [Test]
    public async Task TopologyFingerprintCoversCountsIndicesAndSubprims()
    {
        SilkMeshData baseline = CreateTopologyVariant(
            [0, 0, 0, 1, 0, 0, 0, 1, 0],
            [0, 1, 2],
            [4]);
        SilkMeshData pointCount = CreateTopologyVariant(
            [0, 0, 0, 1, 0, 0, 0, 1, 0, 2, 2, 2],
            [0, 1, 2],
            [4]);
        SilkMeshData indexSequence = CreateTopologyVariant(
            [0, 0, 0, 1, 0, 0, 0, 1, 0],
            [0, 2, 1],
            [4]);
        SilkMeshData subprim = CreateTopologyVariant(
            [0, 0, 0, 1, 0, 0, 0, 1, 0],
            [0, 1, 2],
            [5]);

        await Assert.That(pointCount.TopologyFingerprint)
            .IsNotEqualTo(baseline.TopologyFingerprint);
        await Assert.That(indexSequence.TopologyFingerprint)
            .IsNotEqualTo(baseline.TopologyFingerprint);
        await Assert.That(subprim.TopologyFingerprint)
            .IsNotEqualTo(baseline.TopologyFingerprint);
    }

    [Test]
    public async Task SustainedTopologyChurnPrunesInactiveRanges()
    {
        const int revisionCount = 1_024;
        var table = new SilkPickIdentityTable();
        uint firstToken = 0;
        uint activeToken = 0;
        for (int revision = 1; revision <= revisionCount; revision++)
        {
            SilkPickTokenRange range = table.Upsert(CreateMesh(
                "/Pruned",
                primId: 88,
                topologyRevision: checked((ulong)revision),
                triangleSubprims: [revision]));
            firstToken = firstToken == 0 ? range.FirstToken : firstToken;
            activeToken = range.FirstToken;
        }

        await Assert.That(table.ActiveRangeCount).IsEqualTo(1);
        await Assert.That(table.AllocatedRangeCount)
            .IsEqualTo((ulong)revisionCount);
        await Assert.That(table.Revision).IsEqualTo((ulong)revisionCount);
        await Assert.That(table.TryResolve(firstToken, out _)).IsFalse();
        await Assert.That(table.TryResolve(
            activeToken,
            out SilkPickIdentity active)).IsTrue();
        await Assert.That(active.SubprimIndex).IsEqualTo(revisionCount);
    }

    [Test]
    public async Task ResolvedIdentityProducesTruthfulIdOnlyHit()
    {
        var table = new SilkPickIdentityTable();
        SilkPickTokenRange range = table.Upsert(CreateMesh(
            "/Mesh",
            primId: 7,
            topologyRevision: 1,
            triangleSubprims: [3]));
        if (!table.TryResolve(range.FirstToken, out SilkPickIdentity identity))
        {
            throw new InvalidOperationException("The retained pick token did not resolve.");
        }

        var request = new RenderPickRequest(
            0,
            0,
            new ViewportDimensions(1, 1),
            requestedStateRevision: 5,
            requestedSceneRevision: 9,
            target: RenderPickTarget.Face);
        var item = new SelectionItem(
            identity.Path,
            instancerPath: null,
            instanceIndex: null,
            elementIndex: identity.SubprimIndex,
            elementKind: SelectionElementKind.Face);
        RenderPickResult result = RenderPickResult.Hit(
            request,
            stateRevision: 5,
            sceneRevision: 9,
            item,
            backendKind: RenderBackendKind.Vulkan,
            backendToken: range.FirstToken);

        await Assert.That(result.PrimPath).IsEqualTo(identity.Path);
        await Assert.That(result.ElementIndex).IsEqualTo(identity.SubprimIndex);
        await Assert.That(result.BackendToken).IsEqualTo(range.FirstToken);
        await Assert.That(result.WorldPosition).IsNull();
        await Assert.That(result.WorldNormal).IsNull();
        await Assert.That(result.NormalizedDepth).IsNull();
    }

    private static SilkMeshData CreateMesh(
        string path,
        int primId,
        ulong topologyRevision,
        int[] triangleSubprims,
        float[]? color = null,
        ulong? stableHash = null,
        float[]? points = null,
        int instanceId = 0,
        int instanceIndex = 0)
    {
        var indices = new uint[triangleSubprims.Length * 3];
        for (int triangle = 0; triangle < triangleSubprims.Length; triangle++)
        {
            int offset = triangle * 3;
            indices[offset] = 0;
            indices[offset + 1] = 1;
            indices[offset + 2] = 2;
        }
        return new SilkMeshData(
            primId,
            path,
            stableHash ?? SilkWireFormat.ComputeStableHash(path),
            instanceId,
            instanceIndex,
            SilkTopologyKind.TriangleList,
            topologyRevision,
            points ?? [0, 0, 0, 1, 0, 0, 0, 1, 0],
            indices,
            triangleSubprims,
            color ?? [1, 1, 1, 1],
            [
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1,
            ]);
    }

    private static int[] CreateSubprims(Random random)
    {
        var values = new int[random.Next(1, 5)];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = random.Next(0, 10_000);
        }
        return values;
    }

    private static void Retire(
        SilkPickTokenRange range,
        List<uint> retiredTokens)
    {
        for (uint offset = 0; offset < range.TokenCount; offset++)
        {
            retiredTokens.Add(checked(range.FirstToken + offset));
        }
    }

    private static void VerifyPickInvariants(
        SilkPickIdentityTable table,
        IReadOnlyDictionary<string, ExpectedPickState> active,
        List<uint> retiredTokens,
        ulong expectedRevision,
        ulong expectedAllocatedRanges)
    {
        if (table.Revision != expectedRevision ||
            table.AllocatedRangeCount != expectedAllocatedRanges ||
            table.ActiveRangeCount != active.Count ||
            table.TryResolve(0, out _))
        {
            throw new InvalidOperationException(
                "The randomized pick table counters or background token diverged.");
        }

        foreach ((string path, ExpectedPickState expected) in active)
        {
            if (!table.TryGetRange(path, out SilkPickTokenRange range) ||
                range != expected.Range ||
                range.TokenCount != expected.Subprims.Length)
            {
                throw new InvalidOperationException(
                    $"Active path '{path}' has an inconsistent token range.");
            }

            for (int index = 0; index < expected.Subprims.Length; index++)
            {
                uint token = checked(range.FirstToken + (uint)index);
                if (!table.TryResolve(token, out SilkPickIdentity identity) ||
                    identity.Path != path ||
                    identity.PrimId != expected.PrimId ||
                    identity.StableHash != SilkWireFormat.ComputeStableHash(path) ||
                    identity.TopologyRevision != expected.TopologyRevision ||
                    identity.SubprimIndex != expected.Subprims[index])
                {
                    throw new InvalidOperationException(
                        $"Token {token} for '{path}' resolved to the wrong identity.");
                }
            }
        }

        for (int index = 0; index < retiredTokens.Count; index++)
        {
            if (table.TryResolve(retiredTokens[index], out _))
            {
                throw new InvalidOperationException(
                    $"Retired token {retiredTokens[index]} remained active.");
            }
        }
    }

    private static TException CaptureFailure<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException(
            $"Expected a {typeof(TException).Name}.");
    }

    private sealed record ExpectedPickState(
        int PrimId,
        ulong TopologyRevision,
        int[] Subprims,
        SilkPickTokenRange Range);

    private static SilkMeshData CreateLargeMesh(
        string path,
        ulong topologyRevision,
        int triangleCount,
        double translateX)
    {
        var indices = new uint[checked(triangleCount * 3)];
        var subprims = new int[triangleCount];
        for (int triangle = 0; triangle < triangleCount; triangle++)
        {
            int offset = triangle * 3;
            indices[offset] = 0;
            indices[offset + 1] = 1;
            indices[offset + 2] = 2;
            subprims[triangle] = triangle;
        }
        return new SilkMeshData(
            99,
            path,
            SilkWireFormat.ComputeStableHash(path),
            instanceId: 0,
            instanceIndex: 0,
            SilkTopologyKind.TriangleList,
            topologyRevision,
            [0, 0, 0, 1, 0, 0, 0, 1, 0],
            indices,
            subprims,
            [1, 1, 1, 1],
            [
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                translateX, 0, 0, 1,
            ]);
    }

    private static SilkMeshData CreateTopologyVariant(
        float[] points,
        uint[] indices,
        int[] subprims) =>
        new(
            101,
            "/Fingerprint",
            SilkWireFormat.ComputeStableHash("/Fingerprint"),
            0,
            0,
            SilkTopologyKind.TriangleList,
            1,
            points,
            indices,
            subprims,
            [1, 1, 1, 1],
            [
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1,
            ]);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference[] References, uint FirstToken, uint ActiveToken)
        CreateTopologyChurn(SilkPickIdentityTable table)
    {
        var references = new List<WeakReference>();
        uint firstToken = 0;
        uint activeToken = 0;
        for (ulong revision = 1; revision <= 24; revision++)
        {
            var points = new float[90_000];
            SilkMeshData mesh = CreateMesh(
                "/Churn",
                primId: 77,
                topologyRevision: revision,
                triangleSubprims: [checked((int)revision)],
                points: points);
            references.Add(new WeakReference(mesh));
            AddBackingArrayReference(references, mesh.Points);
            AddBackingArrayReference(references, mesh.Indices);

            // The still-active record's own subprim table is deliberately not
            // tracked here: the identity it publishes IS that table, and the
            // table shares it with the record rather than copying it once per
            // resolved instance. Only the retired revisions must be released,
            // which is what this churn measures.
            if (revision != 24)
            {
                AddBackingArrayReference(references, mesh.TriangleSubprims);
            }
            SilkPickTokenRange range = table.Upsert(mesh);
            firstToken = firstToken == 0 ? range.FirstToken : firstToken;
            activeToken = range.FirstToken;
        }
        return ([.. references], firstToken, activeToken);
    }

    private static void AddBackingArrayReference<T>(
        List<WeakReference> references,
        ReadOnlyMemory<T> memory)
    {
        if (!MemoryMarshal.TryGetArray(
                memory,
                out ArraySegment<T> segment) ||
            segment.Array is null)
        {
            throw new InvalidOperationException(
                "Could not inspect a retained mesh array.");
        }
        references.Add(new WeakReference(segment.Array));
    }
}
