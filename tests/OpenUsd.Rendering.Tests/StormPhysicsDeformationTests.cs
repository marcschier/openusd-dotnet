// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Pins the managed mirror of the Storm deformation override C ABI and the batch that packs it.
/// </summary>
/// <remarks>
/// <para>
/// The packed layout is the only thing the managed and native halves agree on at runtime, and a
/// silent disagreement produces geometry rather than a crash: a shifted field would draw a prim
/// with another prim's vertices. The sizes and offsets are therefore asserted here against the same
/// numbers <c>native/include/openusd_render_physics.h</c> asserts in C++.
/// </para>
/// <para>
/// The batch itself is asserted through its packing rather than through a renderer, so the region
/// records, the shared point page, and the packed path bytes can be proven correct without a GPU.
/// </para>
/// </remarks>
public sealed class StormPhysicsDeformationTests
{
    private const string ClothPath = "/World/Cloth";

    [Test]
    public async Task ThePackedLayoutMatchesTheNativeContract()
    {
        await Assert.That(Unsafe.SizeOf<StormPhysicsOverrideInterop.NativeDeformationOverrideItem>())
            .IsEqualTo(40);
        await Assert.That(Marshal.SizeOf<StormPhysicsOverrideInterop.NativeDeformationOverrideItem>())
            .IsEqualTo(40);
        await Assert.That(Unsafe.SizeOf<
                StormPhysicsOverrideInterop.NativeDeformationOverrideDiagnostics>())
            .IsEqualTo(64);
        await Assert.That(Unsafe.SizeOf<
                StormPhysicsOverrideInterop.NativeDeformationOverrideUpdate>())
            .IsEqualTo(56);

        await Assert.That((int)Marshal.OffsetOf<
                StormPhysicsOverrideInterop.NativeDeformationOverrideItem>("PathOffset"))
            .IsEqualTo(8);
        await Assert.That((int)Marshal.OffsetOf<
                StormPhysicsOverrideInterop.NativeDeformationOverrideItem>("PointOffset"))
            .IsEqualTo(24);
        await Assert.That((int)Marshal.OffsetOf<
                StormPhysicsOverrideInterop.NativeDeformationOverrideItem>("TopologyRevision"))
            .IsEqualTo(32);
        await Assert.That((int)Marshal.OffsetOf<
                StormPhysicsOverrideInterop.NativeDeformationOverrideDiagnostics>("Revision"))
            .IsEqualTo(16);
        await Assert.That((int)Marshal.OffsetOf<
                StormPhysicsOverrideInterop.NativeDeformationOverrideDiagnostics>("Capacity"))
            .IsEqualTo(48);
        await Assert.That((int)Marshal.OffsetOf<
                StormPhysicsOverrideInterop.NativeDeformationOverrideDiagnostics>("MismatchedCount"))
            .IsEqualTo(60);
    }

    [Test]
    public async Task TheDeclaredBoundsMatchTheNativeContract()
    {
        int capacity = StormPhysicsDeformationOverrides.MaximumCapacity;
        int points = StormPhysicsDeformationOverrides.MaximumPoints;
        int pathBytes = StormPhysicsDeformationOverrides.MaximumPathBytes;
        uint storm = RenderNativeAbiVersions.StormAbi;

        await Assert.That(capacity).IsEqualTo(1024);
        await Assert.That(points).IsEqualTo(4194304);
        await Assert.That(pathBytes).IsEqualTo(1024 * 1024);

        // The Storm ABI announces the capability, so the version has to move
        // with the entry points rather than after them.
        await Assert.That(storm).IsEqualTo(8u);
    }

    [Test]
    public async Task ABatchPacksOneRegionPerBoundBody()
    {
        var batch = new StormPhysicsDeformationOverrides(8, 64, 4096);
        var bindings = new PhysicsRenderBindingTable(8);
        var cloth = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.Deformable);
        var jelly = new PhysicsRenderObjectId(102, PhysicsRenderObjectKind.Deformable);
        _ = bindings.TryBind(cloth, ClothPath);
        _ = bindings.TryBind(jelly, "/World/Jelly");

        float[] vertices = [0, 0, 0, 1, 0, 0, 1, 0, 1, 5, 5, 5, 6, 6, 6];
        int packed = batch.Refresh(
            new PhysicsRenderDeformationView(
                new PhysicsRenderDeformableRegion[]
                {
                    new(cloth, PhysicsRenderDomain.Cloth, 0, 3, 7),
                    new(jelly, PhysicsRenderDomain.Deformable, 3, 2, 9)
                },
                vertices,
                revision: 12),
            bindings);

        await Assert.That(packed).IsEqualTo(2);
        await Assert.That(batch.PointCount).IsEqualTo(5);
        await Assert.That(batch.Revision).IsEqualTo(12UL);
        await Assert.That(batch.PathAt(0)).IsEqualTo(ClothPath);
        await Assert.That(batch.PathAt(1)).IsEqualTo("/World/Jelly");
        await Assert.That(batch.ObjectIdAt(0)).IsEqualTo(101UL);

        // The shared point page is packed contiguously in region order, so a
        // region's window is exactly the vertices it published.
        await Assert.That(batch.PointsAt(0).ToArray())
            .IsEquivalentTo(new float[] { 0, 0, 0, 1, 0, 0, 1, 0, 1 });
        await Assert.That(batch.PointsAt(1).ToArray())
            .IsEquivalentTo(new float[] { 5, 5, 5, 6, 6, 6 });
    }

    [Test]
    public async Task ARegionThatResolvesToNoPrimIsCountedRatherThanPacked()
    {
        var batch = new StormPhysicsDeformationOverrides(8, 64, 4096);
        var bindings = new PhysicsRenderBindingTable(8);
        var cloth = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.Deformable);

        int packed = batch.Refresh(View(cloth, PhysicsRenderDomain.Cloth), bindings);

        await Assert.That(packed).IsEqualTo(0);
        await Assert.That(batch.UnboundRegions).IsEqualTo(1);
        await Assert.That(batch.PointCount).IsEqualTo(0);
    }

    [Test]
    public async Task AParticleRegionIsNotPackedAgainstAMeshItDoesNotDescribe()
    {
        var batch = new StormPhysicsDeformationOverrides(8, 64, 4096);
        var bindings = new PhysicsRenderBindingTable(8);
        var particles = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.Deformable);
        _ = bindings.TryBind(particles, ClothPath);

        int packed = batch.Refresh(View(particles, PhysicsRenderDomain.Particles), bindings);

        await Assert.That(packed).IsEqualTo(0);
        await Assert.That(batch.UnboundRegions).IsEqualTo(0);
        await Assert.That(StormPhysicsDeformationOverrides.IsDomainSupported(
            PhysicsRenderDomain.Particles)).IsFalse();
        await Assert.That(StormPhysicsDeformationOverrides.IsDomainSupported(
            PhysicsRenderDomain.Cloth)).IsTrue();
        await Assert.That(StormPhysicsDeformationOverrides.IsDomainSupported(
            PhysicsRenderDomain.Deformable)).IsTrue();
    }

    [Test]
    public async Task ARegionThatDoesNotFitThePointPageIsDroppedWholeAndCounted()
    {
        var batch = new StormPhysicsDeformationOverrides(8, 2, 4096);
        var bindings = new PhysicsRenderBindingTable(8);
        var cloth = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.Deformable);
        _ = bindings.TryBind(cloth, ClothPath);

        int packed = batch.Refresh(View(cloth, PhysicsRenderDomain.Cloth), bindings);

        // Three vertices cannot be truncated into a two point page: a half
        // uploaded body renders geometry no producer described.
        await Assert.That(packed).IsEqualTo(0);
        await Assert.That(batch.DroppedRegions).IsEqualTo(1);
        await Assert.That(batch.PointCount).IsEqualTo(0);
    }

    [Test]
    public async Task ClearingTheBatchRestoresTheAuthoredPoints()
    {
        var batch = new StormPhysicsDeformationOverrides(8, 64, 4096);
        var bindings = new PhysicsRenderBindingTable(8);
        var cloth = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.Deformable);
        _ = bindings.TryBind(cloth, ClothPath);
        _ = batch.Refresh(View(cloth, PhysicsRenderDomain.Cloth), bindings);

        batch.Clear(revision: 33);

        // An emptied batch is what restores authored geometry, because the
        // renderer replaces every retained deformation with the batch it is
        // given rather than merging into it.
        await Assert.That(batch.Count).IsEqualTo(0);
        await Assert.That(batch.PointCount).IsEqualTo(0);
        await Assert.That(batch.PathByteCount).IsEqualTo(0);
        await Assert.That(batch.Revision).IsEqualTo(33UL);

        batch.Reset();
        await Assert.That(batch.DroppedRegions).IsEqualTo(0);
        await Assert.That(batch.UnboundRegions).IsEqualTo(0);
        await Assert.That(batch.RefreshCount).IsEqualTo(0);
    }

    [Test]
    public async Task ABatchRefusesCapacitiesTheAbiCannotCarry()
    {
        await Assert.That(() => new StormPhysicsDeformationOverrides(
                StormPhysicsDeformationOverrides.MaximumCapacity + 1, 64, 4096))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new StormPhysicsDeformationOverrides(
                8, StormPhysicsDeformationOverrides.MaximumPoints + 1, 4096))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new StormPhysicsDeformationOverrides(
                8, 64, StormPhysicsDeformationOverrides.MaximumPathBytes + 1))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task TheDiagnosticsDescribeEveryRefusalSeparately()
    {
        var diagnostics = new StormPhysicsDeformationDiagnostics(
            AppliedCount: 3,
            UnresolvedCount: 1,
            DroppedCount: 0,
            UnsupportedCount: 0,
            MismatchedCount: 2,
            Capacity: 1024,
            Revision: 9,
            AppliedBatchCount: 4,
            RejectedBatchCount: 0,
            DirtiedPrimCount: 12);

        await Assert.That(diagnostics.IsComplete).IsFalse();
        await Assert.That(diagnostics.Describe()).Contains("mismatched=2");
        await Assert.That(diagnostics.Describe()).Contains("applied=3/1024");
        await Assert.That(StormPhysicsDeformationDiagnostics.Empty.IsComplete).IsTrue();
    }

    private static PhysicsRenderDeformationView View(
        PhysicsRenderObjectId id,
        PhysicsRenderDomain domain) =>
        new(
            new PhysicsRenderDeformableRegion[] { new(id, domain, 0, 3, 7) },
            new float[] { 0, 1, 0, 1, 1, 0, 1, 1, 1 },
            revision: 5);
}
