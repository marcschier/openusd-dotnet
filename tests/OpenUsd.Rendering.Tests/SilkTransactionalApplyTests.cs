// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Pins that a page is a transaction: it either applies completely or leaves the
/// retained scene exactly as it found it.
/// </summary>
/// <remarks>
/// <para>
/// The whole-page preflight covers everything a command view can decide on its
/// own. It cannot cover the checks that depend on retained state -- a stable hash
/// that does not match its path, a hash that already names another prim, an
/// identity replaced without recreation evidence -- because those are only
/// decidable against the state the commands before them produced. Those checks
/// therefore run during the mutating pass, and the mutating pass records the
/// inverse of every write so a rejection can put them all back.
/// </para>
/// <para>
/// Every case here puts a perfectly valid mesh, material or environment first and
/// an offending command after it, and requires every observable of the scene --
/// the retained records, the pick identity table, and each of the five revisions
/// a consumer keys its caches on -- to be exactly what it was before the page.
/// That is what makes the rejection cost no GPU delta: a consumer that sees no
/// revision move has nothing to rebuild.
/// </para>
/// </remarks>
public sealed class SilkTransactionalApplyTests
{
    private const string FirstPath = "/World/Geom/First";
    private const string SecondPath = "/World/Geom/Second";
    private const string MaterialPath = "/World/Materials/Surface";
    private const string DomePath = "/World/Lights/Dome";

    [Test]
    public async Task ATrailingBadStableHashLeavesTheLeadingMeshUnapplied()
    {
        var scene = new SilkSceneState();
        Snapshot before = Snapshot.Of(scene);

        byte[] bad = Mesh(SecondPath, primId: 2);
        BinaryPrimitives.WriteUInt64LittleEndian(bad.AsSpan(8), 0xDEADBEEFDEADBEEFUL);

        await Assert.That(() => scene.Apply([.. Mesh(FirstPath, primId: 1), .. bad], 2, 1))
            .Throws<InvalidDataException>();

        await before.AssertUnchanged(scene, "a trailing mesh whose stable hash is wrong");
    }

    [Test]
    public async Task ATrailingHashCollisionLeavesTheLeadingMeshUnapplied()
    {
        var scene = new SilkSceneState();
        Snapshot before = Snapshot.Of(scene);

        // The second mesh names a different path but carries the first path's
        // hash, which is the collision the retained index cannot represent. It is
        // only detectable against the record the *first* command of this same
        // page retained, so it is exactly the case a preflight cannot hoist.
        byte[] colliding = Mesh(SecondPath, primId: 2);
        BinaryPrimitives.WriteUInt64LittleEndian(
            colliding.AsSpan(8),
            SilkEnvironmentLightingTests.ComputeStableHash(FirstPath));

        await Assert.That(() => scene.Apply(
                [.. Mesh(FirstPath, primId: 1), .. colliding],
                2,
                1))
            .Throws<InvalidDataException>();

        await before.AssertUnchanged(scene, "a trailing hash collision");
        await Assert.That(scene.PickIdentities.TryGetRange(FirstPath, out _))
            .IsFalse()
            .Because("The pick identity of the accepted mesh must be rolled back too.");
    }

    [Test]
    public async Task ARejectedReplacementRestoresTheRetainedRecordExactly()
    {
        // The hard case: the page's first command legitimately replaces a mesh
        // that is already retained, and its second command is rejected. Undoing
        // the replacement has to restore the previous record -- not merely drop
        // the new one -- because the old record is what every retained GPU
        // resource was built from, and because the replacement retires the old
        // pick token range and allocates a new one.
        //
        // The trailing command is rejected for a *retained-state* reason -- a
        // stable hash that does not match its path -- rather than a structural
        // one. A structurally malformed command is caught by the whole-page
        // preflight before the replacement is ever applied, so it would prove
        // only that the preflight runs first: this page has to reach the
        // mutating pass, replace the mesh, and then be undone.
        var scene = new SilkSceneState();
        _ = scene.Apply(Mesh(FirstPath, primId: 1), 1, 1);
        SilkMeshData retained = scene.Meshes.Values.Single();
        _ = scene.PickIdentities.TryGetRange(FirstPath, out SilkPickTokenRange range);
        Snapshot before = Snapshot.Of(scene);

        byte[] replacement = Mesh(FirstPath, primId: 1, topologyRevision: 4, x: 4);
        byte[] bad = Mesh(SecondPath, primId: 2);
        BinaryPrimitives.WriteUInt64LittleEndian(bad.AsSpan(8), 0xFEEDFACEFEEDFACEUL);

        await Assert.That(() => scene.Apply([.. replacement, .. bad], 2, 2))
            .Throws<InvalidDataException>();

        await before.AssertUnchanged(scene, "a rejected page that replaced a mesh");
        await Assert.That(scene.Meshes.Values.Single()).IsSameReferenceAs(retained);
        await Assert.That(scene.MeshesByPath[(FirstPath, 0)]).IsSameReferenceAs(retained);
        await Assert.That(scene.GetInstances(FirstPath).Single()).IsSameReferenceAs(retained);
        await Assert.That(retained.TopologyRevision)
            .IsEqualTo(1UL)
            .Because("The restored record must be the authored one, not the replacement.");

        // The pick range is the observable that a naive "drop the new record"
        // undo gets wrong: the replacement deactivated the old range and
        // allocated a new one, so a rollback that only restored the dictionaries
        // would leave the prim addressable by a token that resolves to nothing.
        _ = scene.PickIdentities.TryGetRange(FirstPath, out SilkPickTokenRange after);
        await Assert.That(after).IsEqualTo(range);
        await Assert.That(scene.PickIdentities.TryResolve(range.LastToken, out SilkPickIdentity identity))
            .IsTrue()
            .Because("The restored range must still resolve to the retained prim.");
        await Assert.That(identity.PrimId).IsEqualTo(1);
        await Assert.That(identity.TopologyRevision).IsEqualTo(1UL);
    }

    [Test]
    public async Task AFailedJournalRecordLeavesTheRetainedIdentityUntouched()
    {
        // A journal entry is recorded before the write it undoes, so a record
        // that fails must find nothing to undo. That ordering is invisible from
        // the outside -- a table that published a token range and then failed to
        // record its undo looks exactly like one that never allocated, right up
        // until the rollback leaves the range active and the token resolving to
        // an identity the scene does not retain.
        var scene = new SilkSceneState();
        _ = scene.Apply(Mesh(FirstPath, primId: 1), 1, 1);
        Snapshot before = Snapshot.Of(scene);
        _ = scene.PickIdentities.TryGetRange(FirstPath, out SilkPickTokenRange range);

        int covered = 0;
        for (int ordinal = 0; ordinal < 16; ordinal++)
        {
            scene.PickIdentities.FailUndoRecordForTesting(ordinal);
            bool threw = false;
            try
            {
                _ = scene.Apply(
                    [.. Mesh(SecondPath, primId: 2), .. Mesh(FirstPath, primId: 1, x: 3)],
                    2,
                    (ulong)ordinal + 2);
            }
            catch (InvalidOperationException)
            {
                // The injected journal failure, which must roll back whole.
                threw = true;
            }

            if (!threw)
            {
                // Past the last journal record this page writes, so there is
                // nothing left to inject and the page legitimately applied.
                scene.PickIdentities.FailUndoRecordForTesting(-1);
                break;
            }

            await before.AssertUnchanged(scene, $"an injected journal failure at {ordinal}");
            _ = scene.PickIdentities.TryGetRange(FirstPath, out SilkPickTokenRange actual);
            await Assert.That(actual)
                .IsEqualTo(range)
                .Because("A failed journal record may not leave a reallocated range.");
            await Assert.That(scene.PickIdentities.TryResolve(range.LastToken, out _))
                .IsTrue()
                .Because("The retained token must still resolve after every rollback.");
            covered++;
        }

        await Assert.That(covered)
            .IsGreaterThanOrEqualTo(4)
            .Because(
                "Every journal record a two-mesh page writes must be a point the " +
                "page can be rolled back from, including the one that publishes " +
                "the pick token range.");
    }

    [Test]
    public async Task ARejectedPageThatPublishedALinkTableAndAShadowTableUndoesBoth()
    {
        // Both are whole-table replacements whose Update allocates, so both are
        // journaled and both are restored by exchanging containers rather than
        // by copying back into them: a rollback that can itself fail half way is
        // not a rollback.
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [.. Frame(width: 4, lightCount: 2), .. Mesh(FirstPath, primId: 1)],
            2,
            1);
        Snapshot before = Snapshot.Of(scene);

        byte[] bad = Mesh(SecondPath, primId: 2);
        BinaryPrimitives.WriteUInt64LittleEndian(bad.AsSpan(8), 11UL);

        await Assert.That(() => scene.Apply(
                [
                    .. Frame(width: 4, lightCount: 2),
                    .. LightLinkTable(lightCount: 2),
                    .. ShadowTable(lightCount: 2),
                    .. bad,
                ],
                4,
                2))
            .Throws<InvalidDataException>();

        await before.AssertUnchanged(scene, "a rejected page that published both tables");
        await Assert.That(scene.LightLinks.HasLinks).IsFalse();
        await Assert.That(scene.Shadows.HasShadows).IsFalse();
    }

    [Test]
    public async Task ARejectedRemovalPageRestoresEveryRecordItRetired()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [.. Mesh(FirstPath, primId: 1), .. Mesh(SecondPath, primId: 2)],
            2,
            1);
        Snapshot before = Snapshot.Of(scene);

        byte[] bad = Mesh(SecondPath, primId: 3);
        BinaryPrimitives.WriteUInt64LittleEndian(bad.AsSpan(8), 1UL);

        await Assert.That(() => scene.Apply(
                [.. Removal(FirstPath), .. bad],
                2,
                2))
            .Throws<InvalidDataException>();

        await before.AssertUnchanged(scene, "a rejected page that removed a mesh");
        await Assert.That(scene.MeshesByPath.ContainsKey((FirstPath, 0)))
            .IsTrue()
            .Because("The removal must be undone, not merely stopped.");
        await Assert.That(scene.PickIdentities.TryGetRange(FirstPath, out _))
            .IsTrue()
            .Because("The retired pick identity must be restored with the record.");
    }

    [Test]
    public async Task ARejectedPageRestoresTheFrameTheLightLinksAndTheMaterials()
    {
        // A frame, a light link table and a material all replace whole retained
        // structures rather than one field of one, so their undo is a restore of
        // the previous structure. The rejected page below publishes all three and
        // then fails, and none of them may survive.
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. Frame(width: 4),
                .. Mesh(FirstPath, primId: 1),
                .. MaterialCommand(),
            ],
            3,
            1);
        Snapshot before = Snapshot.Of(scene);
        int retainedWidth = scene.Frame.Width;

        byte[] bad = Mesh(SecondPath, primId: 2);
        BinaryPrimitives.WriteUInt64LittleEndian(bad.AsSpan(8), 7UL);

        await Assert.That(() => scene.Apply(
                [
                    .. Frame(width: 64),
                    .. LightLinkTable(),
                    .. MaterialRemoval(),
                    .. bad,
                ],
                4,
                2))
            .Throws<InvalidDataException>();

        await before.AssertUnchanged(scene, "a rejected page that republished the frame");
        await Assert.That(scene.Frame.Width)
            .IsEqualTo(retainedWidth)
            .Because("The frame command preceded the rejection and must be undone.");
        await Assert.That(scene.LightLinks.HasLinks)
            .IsFalse()
            .Because("The link table preceded the rejection and must be undone.");
        await Assert.That(scene.Materials.ContainsKey(MaterialPath))
            .IsTrue()
            .Because("The material removal preceded the rejection and must be undone.");
    }

    [Test]
    public async Task ARejectedEnvironmentPageRestoresTheRetainedEnvironment()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(
            SilkEnvironmentLightingTests.CreateEnvironmentUpsert(DomePath, "/assets/a.hdr"),
            1,
            1);
        Snapshot before = Snapshot.Of(scene);
        SilkEnvironmentData retained = scene.Environments[DomePath];

        byte[] replacement = SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
            DomePath,
            "/assets/b.hdr");
        byte[] bad = Mesh(SecondPath, primId: 2);
        BinaryPrimitives.WriteUInt64LittleEndian(bad.AsSpan(8), 3UL);

        await Assert.That(() => scene.Apply([.. replacement, .. bad], 2, 2))
            .Throws<InvalidDataException>();

        await before.AssertUnchanged(scene, "a rejected page that re-authored a dome");
        await Assert.That(scene.Environments[DomePath]).IsSameReferenceAs(retained);
    }

    [Test]
    public async Task AnAppliedPageStillMovesEveryRevisionItShould()
    {
        // The journal must not become a filter: a page that is accepted has to
        // move exactly the revisions it always moved, or nothing downstream
        // rebuilds.
        var scene = new SilkSceneState();
        _ = scene.Apply(Mesh(FirstPath, primId: 1), 1, 1);
        Snapshot before = Snapshot.Of(scene);

        _ = scene.Apply(
            [.. Mesh(SecondPath, primId: 2), .. MaterialCommand()],
            2,
            2);

        await Assert.That(scene.Meshes.Count).IsEqualTo(2);
        await Assert.That(scene.Materials.Count).IsEqualTo(1);
        await Assert.That(scene.Revision).IsEqualTo(2UL);
        await Assert.That(scene.GeometryRevision).IsGreaterThan(before.Geometry);
        await Assert.That(scene.MaterialRevision).IsGreaterThan(before.Material);
        await Assert.That(scene.PickIdentities.Revision).IsGreaterThan(before.Pick);
    }

    [Test]
    public async Task ARejectedShadowTableKeepsTheDescriptorListAConsumerAlreadyHolds()
    {
        // Descriptors hands out the retained list itself, and a shadow map cache
        // holds that reference for the lifetime of the maps it rendered from it.
        // Putting a rejected page's table back by exchanging containers would
        // leave every such reader looking at the rejected table forever, which is
        // the exact state the rollback exists to prevent -- so the restore is made
        // in place, into capacity reserved before the page ran.
        var scene = new SilkSceneState();
        _ = scene.Apply(
            [
                .. Frame(width: 4, lightCount: 2),
                .. Mesh(FirstPath, primId: 1),
                .. ShadowTable(lightCount: 2, resolution: 256),
            ],
            3,
            1);

        IReadOnlyList<SilkShadowDescriptor> retained = scene.Shadows.Descriptors;
        await Assert.That(retained.Count).IsEqualTo(1);
        await Assert.That(retained[0].Resolution).IsEqualTo(256u);
        Snapshot before = Snapshot.Of(scene);

        byte[] bad = Mesh(SecondPath, primId: 2);
        BinaryPrimitives.WriteUInt64LittleEndian(bad.AsSpan(8), 23UL);

        await Assert.That(() => scene.Apply(
                [
                    .. Frame(width: 4, lightCount: 2),
                    .. ShadowTable(lightCount: 2, resolution: 1024, descriptorCount: 2),
                    .. bad,
                ],
                3,
                2))
            .Throws<InvalidDataException>();

        await before.AssertUnchanged(scene, "a rejected page that replaced the shadow table");
        await Assert.That(scene.Shadows.Descriptors)
            .IsSameReferenceAs(retained)
            .Because(
                "A consumer that resolved the descriptors once must not be left " +
                "holding the rejected page's table.");
        await Assert.That(retained.Count)
            .IsEqualTo(1)
            .Because("The view the consumer holds must show the restored table.");
        await Assert.That(retained[0].Resolution).IsEqualTo(256u);
        await Assert.That(scene.Shadows.ResolveSlot(0)).IsEqualTo(0);

        // And the same reference keeps tracking later, accepted updates.
        _ = scene.Apply(
            [
                .. Frame(width: 4, lightCount: 2),
                .. ShadowTable(lightCount: 2, resolution: 1024, descriptorCount: 2),
            ],
            2,
            3);

        await Assert.That(scene.Shadows.Descriptors).IsSameReferenceAs(retained);
        await Assert.That(retained.Count).IsEqualTo(2);
        await Assert.That(retained[0].Resolution).IsEqualTo(1024u);
    }

    /// <summary>Every observable a consumer keys a retained resource on.</summary>
    private readonly record struct Snapshot(
        int Meshes,
        int MeshesByPath,
        int Materials,
        int Environments,
        ulong Revision,
        ulong Geometry,
        ulong Material,
        ulong Environment,
        ulong Deformation,
        ulong Frame,
        ulong LightLinks,
        ulong Shadows,
        ulong Pick,
        int PickRanges,
        ulong PickAllocated)
    {
        internal static Snapshot Of(SilkSceneState scene) => new(
            scene.Meshes.Count,
            scene.MeshesByPath.Count,
            scene.Materials.Count,
            scene.Environments.Count,
            scene.Revision,
            scene.GeometryRevision,
            scene.MaterialRevision,
            scene.EnvironmentRevision,
            scene.DeformationRevision,
            scene.Frame.Revision,
            scene.LightLinks.Revision,
            scene.Shadows.Revision,
            scene.PickIdentities.Revision,
            scene.PickIdentities.ActiveRangeCount,
            scene.PickIdentities.AllocatedRangeCount);

        internal async Task AssertUnchanged(SilkSceneState scene, string because)
        {
            Snapshot actual = Of(scene);
            await Assert.That(actual)
                .IsEqualTo(this)
                .Because(
                    $"The scene must be exactly what it was before {because}: " +
                    "a rejected page changes nothing, so no consumer has anything " +
                    "to rebuild.");
        }
    }

    private static byte[] Frame(int width, uint lightCount = 0)
    {
        const int frameSize = 23368;
        var bytes = new byte[frameSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), frameSize);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), width);
        for (int element = 0; element < 16; element++)
        {
            double value = element % 5 == 0 ? 1d : 0d;
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16 + (element * 8)), value);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(144 + (element * 8)), value);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(536), lightCount);
        return bytes;
    }

    private static byte[] LightLinkTable(uint lightCount = 0)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(FirstPath);
        int entrySize = 44 + pathBytes.Length;
        var bytes = new byte[24 + entrySize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.LightLink);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), lightCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), 0u);
        BinaryPrimitives.WriteUInt128LittleEndian(bytes.AsSpan(24), UInt128.Zero);
        BinaryPrimitives.WriteUInt128LittleEndian(bytes.AsSpan(40), UInt128.Zero);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), 0u);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(60), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(64), (uint)pathBytes.Length);
        pathBytes.CopyTo(bytes, 68);
        return bytes;
    }

    /// <summary>Shadow descriptors for the leading frame lights, at one resolution.</summary>
    private static byte[] ShadowTable(
        uint lightCount,
        uint resolution = 256,
        uint descriptorCount = 1)
    {
        const int descriptorSize = 288;
        var bytes = new byte[24 + (descriptorSize * (int)descriptorCount)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Shadow);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), descriptorCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), lightCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), 0u);
        for (uint entry = 0; entry < descriptorCount; entry++)
        {
            Span<byte> descriptor = bytes.AsSpan(24 + ((int)entry * descriptorSize));
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor, entry);
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[4..], entry);
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[8..], resolution);
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[12..], 0u);
            for (int element = 0; element < 16; element++)
            {
                double value = element % 5 == 0 ? 1d : 0d;
                BinaryPrimitives.WriteDoubleLittleEndian(
                    descriptor[(16 + (element * 8))..],
                    value);
                BinaryPrimitives.WriteDoubleLittleEndian(
                    descriptor[(144 + (element * 8))..],
                    value);
            }
            BinaryPrimitives.WriteSingleLittleEndian(descriptor[272..], 0.001f);
            BinaryPrimitives.WriteSingleLittleEndian(descriptor[276..], 0.01f);
            BinaryPrimitives.WriteSingleLittleEndian(descriptor[280..], 1f);
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[284..], 0u);
        }
        return bytes;
    }

    private static byte[] MaterialCommand()
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(MaterialPath);
        List<byte> payload =
        [
            .. BitConverter.GetBytes(
                SilkEnvironmentLightingTests.ComputeStableHash(MaterialPath)),
            .. BitConverter.GetBytes((uint)pathBytes.Length),
            .. BitConverter.GetBytes((uint)SilkSurfaceKind.PreviewSurface),
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes(0u),
            .. pathBytes,
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes(0u),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(0f),
            .. BitConverter.GetBytes(0f),
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(0f),
            .. BitConverter.GetBytes(0f),
        ];
        var bytes = new byte[8 + payload.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MaterialUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        payload.CopyTo(bytes, 8);
        return bytes;
    }

    private static byte[] MaterialRemoval()
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(MaterialPath);
        var bytes = new byte[20 + pathBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MaterialRemove);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkEnvironmentLightingTests.ComputeStableHash(MaterialPath));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), (uint)pathBytes.Length);
        pathBytes.CopyTo(bytes, 20);
        return bytes;
    }

    private static byte[] Removal(string path)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        var bytes = new byte[24 + pathBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshRemove);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkEnvironmentLightingTests.ComputeStableHash(path));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), (uint)pathBytes.Length);
        pathBytes.CopyTo(bytes, 24);
        return bytes;
    }

    internal static byte[] Mesh(
        string path,
        int primId,
        ulong topologyRevision = 1,
        double x = 0)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        float[] points = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        uint[] indices = [0, 1, 2];
        int size = 268 +
            pathBytes.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            sizeof(uint);
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkEnvironmentLightingTests.ComputeStableHash(path));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), primId);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), topologyRevision);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), (uint)pathBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), 1);
        for (int component = 0; component < 4; component++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(64 + (component * 4)), 1f);
        }
        for (int element = 0; element < 16; element++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (element * 8)),
                element % 5 == 0 ? 1 : 0);
        }
        BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(80 + (12 * 8)), x);
        pathBytes.CopyTo(bytes, 268);
        int offset = 268 + pathBytes.Length;
        foreach (float value in points)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), value);
            offset += sizeof(float);
        }
        foreach (uint index in indices)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), index);
            offset += sizeof(uint);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), 0);
        return bytes;
    }

    [Test]
    public async Task LateFailureRestoresWideFrameLinksShadowsAndRevisions()
    {
        var scene = new SilkSceneState();
        UInt128 high127 = UInt128.One << 127;
        UInt128 high96 = UInt128.One << 96;
        byte[] seedShadow = ShadowTable(128, resolution: 512);
        BinaryPrimitives.WriteUInt32LittleEndian(seedShadow.AsSpan(16), 2u);
        BinaryPrimitives.WriteUInt32LittleEndian(seedShadow.AsSpan(24), 127u);
        BinaryPrimitives.WriteUInt32LittleEndian(seedShadow.AsSpan(36), 3u);
        BinaryPrimitives.WriteDoubleLittleEndian(seedShadow.AsSpan(136), 3d);
        BinaryPrimitives.WriteDoubleLittleEndian(seedShadow.AsSpan(144), -2d);
        BinaryPrimitives.WriteDoubleLittleEndian(seedShadow.AsSpan(168), 2d);
        BinaryPrimitives.WriteDoubleLittleEndian(seedShadow.AsSpan(208), 4d);
        _ = scene.Apply(
            [
                .. WideTransactionFrame(replacement: false),
                .. Mesh(FirstPath, primId: 1),
                .. MaterialCommand(),
                .. SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                    DomePath, "/assets/before.hdr", domeIndex: 0),
                .. WideTransactionLinks(128, 2, truncated: true,
                    (FirstPath, -1, high96 | 5, high127 | 2, 1u),
                    (FirstPath, 0, high127 | 1, high96 | 4, 2u)),
                .. seedShadow,
            ], 6, 40);
        Snapshot before = Snapshot.Of(scene);
        SilkFrameState retainedFrame = scene.Frame;
        SilkLightLinkTable retainedLinks = scene.LightLinks;
        SilkShadowTable retainedShadows = scene.Shadows;
        IReadOnlyList<SilkShadowDescriptor> retainedDescriptors = scene.Shadows.Descriptors;
        SilkMeshData retainedMesh = scene.MeshesByPath[(FirstPath, 0)];
        SilkMaterialData retainedMaterial = scene.Materials[MaterialPath];
        SilkEnvironmentData retainedEnvironment = scene.Environments[DomePath];
        double[] view = scene.Frame.View.ToArray();
        double[] projection = scene.Frame.Projection.ToArray();
        double[] clipPlanes = scene.Frame.ClipPlanes.ToArray();
        SilkFrameLight[] lights = scene.Frame.Lights.ToArray();
        SilkFrameDome[] domes = scene.Frame.Domes.ToArray();
        byte[] beforeGpu = WideTransactionGpu(scene);

        byte[] replacementShadow = ShadowTable(97, resolution: 1024, descriptorCount: 2);
        BinaryPrimitives.WriteUInt32LittleEndian(replacementShadow.AsSpan(24), 96u);
        BinaryPrimitives.WriteUInt32LittleEndian(replacementShadow.AsSpan(312), 64u);
        BinaryPrimitives.WriteDoubleLittleEndian(replacementShadow.AsSpan(136), 11d);
        byte[] bad = Mesh(SecondPath, primId: 2);
        BinaryPrimitives.WriteUInt64LittleEndian(bad.AsSpan(8), 0xDEADBEEFDEADBEEFUL);
        byte[] prefix =
        [
            .. WideTransactionFrame(replacement: true),
            .. WideTransactionLinks(97, 1, truncated: false,
                (FirstPath, -1, high96 | 8, UInt128.One << 64, 1u),
                (FirstPath, 0, UInt128.One << 64, high96 | 1, 1u)),
            .. replacementShadow,
            .. MaterialRemoval(),
            .. SilkEnvironmentLightingTests.CreateEnvironmentUpsert(
                DomePath, "/assets/rejected.hdr", domeIndex: 0),
            .. Mesh(FirstPath, primId: 1, topologyRevision: 2, x: 7),
            .. Mesh("/World/Geom/Transient", primId: 3, x: -9),
        ];

        // The bad hash is intentionally NOT a malformed wire record: only the
        // mutating retained-state pass compares it with its decoded path.
        InvalidDataException? failure = null;
        try
        {
            _ = scene.Apply([.. prefix, .. bad], 8, 41);
        }
        catch (InvalidDataException exception)
        {
            failure = exception;
        }
        await Assert.That(failure?.Message ?? string.Empty).Contains("stable hash");
        await Assert.That(failure?.Message ?? string.Empty).Contains(SecondPath);
        await Assert.That(failure?.Message ?? string.Empty).Contains("DEADBEEFDEADBEEF");

        await before.AssertUnchanged(scene, "a late failure after all seven valid replacements");
        await Assert.That(scene.Frame).IsSameReferenceAs(retainedFrame);
        await Assert.That(scene.LightLinks).IsSameReferenceAs(retainedLinks);
        await Assert.That(scene.Shadows).IsSameReferenceAs(retainedShadows);
        await Assert.That(scene.Shadows.Descriptors).IsSameReferenceAs(retainedDescriptors);
        await Assert.That(scene.MeshesByPath[(FirstPath, 0)]).IsSameReferenceAs(retainedMesh);
        await Assert.That(scene.Materials[MaterialPath]).IsSameReferenceAs(retainedMaterial);
        await Assert.That(scene.Environments[DomePath]).IsSameReferenceAs(retainedEnvironment);
        await Assert.That(scene.MeshesByPath.ContainsKey(("/World/Geom/Transient", 0))).IsFalse();
        await Assert.That(scene.PickIdentities.TryGetRange("/World/Geom/Transient", out _)).IsFalse();
        await Assert.That(scene.Frame.View.ToArray().SequenceEqual(view)).IsTrue();
        await Assert.That(scene.Frame.Projection.ToArray().SequenceEqual(projection)).IsTrue();
        await Assert.That(scene.Frame.ClipPlanes.ToArray().SequenceEqual(clipPlanes)).IsTrue();
        await Assert.That(scene.Frame.Lights.ToArray().SequenceEqual(lights)).IsTrue();
        await Assert.That(scene.Frame.Domes.ToArray().SequenceEqual(domes)).IsTrue();
        await Assert.That(scene.Frame.Width).IsEqualTo(320);
        await Assert.That(scene.Frame.Height).IsEqualTo(180);
        await Assert.That(scene.Frame.View.Span[12]).IsEqualTo(5d);
        await Assert.That(scene.Frame.Projection.Span[5]).IsEqualTo(4d);
        await Assert.That(scene.Frame.ClipPlaneCount).IsEqualTo(2u);
        await Assert.That(scene.Frame.LightCount).IsEqualTo(128u);
        await Assert.That(scene.Frame.DomeCount).IsEqualTo(2u);
        await Assert.That(scene.Frame.AmbientLight)
            .IsEqualTo(new System.Numerics.Vector4(0.25f, 0.5f, 0.75f, 0));
        await Assert.That(scene.Frame.Domes[0].IsTextured).IsTrue();
        await Assert.That(scene.Frame.Domes[1].AmbientColor)
            .IsEqualTo(new System.Numerics.Vector3(0.5f, 0.75f, 1));
        await Assert.That(scene.Frame.Lights[127]).IsEqualTo(new SilkFrameLight(
            3, 1, 32, 64, new System.Numerics.Vector3(1, 2, 4), 256,
            new System.Numerics.Matrix4x4(
                1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 128, 256, -128, 1),
            1, 0.5f, 0.75f, 8));
        await Assert.That(scene.LightLinks.Count).IsEqualTo(2);
        await Assert.That(scene.LightLinks.LightCount).IsEqualTo(128u);
        await Assert.That(scene.LightLinks.DomeCount).IsEqualTo(2u);
        await Assert.That(scene.LightLinks.UnsupportedFeatures)
            .IsEqualTo(SilkLightLinkUnsupportedFeatures.Truncated);
        await Assert.That(scene.LightLinks.HasDomeLinks).IsTrue();
        await Assert.That(scene.LightLinks.Resolve(FirstPath, 19))
            .IsEqualTo(new SilkLightLinkMasks(high96 | 5, high127 | 2, 1));
        await Assert.That(scene.LightLinks.Resolve(FirstPath, 0))
            .IsEqualTo(new SilkLightLinkMasks(high127 | 1, high96 | 4, 2));
        await Assert.That(scene.LightLinks.Resolve(SecondPath, 0))
            .IsEqualTo(new SilkLightLinkMasks(UInt128.MaxValue, UInt128.MaxValue, 255));
        await Assert.That(retainedDescriptors.Count).IsEqualTo(1);
        SilkShadowDescriptor restored = retainedDescriptors[0];
        await Assert.That((restored.LightIndex, restored.MapIndex, restored.Resolution, restored.Flags))
            .IsEqualTo((127u, 0u, 512u,
                SilkShadowDescriptorOptions.Orthographic | SilkShadowDescriptorOptions.CasterLinked));
        await Assert.That((restored.DepthBias, restored.NormalBias, restored.PcfRadius))
            .IsEqualTo((0.001f, 0.01f, 1f));
        await Assert.That(restored.View.SequenceEqual(
            new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 3, -2, 0, 1 })).IsTrue();
        await Assert.That(restored.Projection.SequenceEqual(
            new double[] { 2, 0, 0, 0, 0, 4, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 })).IsTrue();
        await Assert.That(scene.Shadows.LightCount).IsEqualTo(128u);
        await Assert.That(scene.Shadows.UnsupportedFeatures).IsEqualTo(SilkShadowUnsupportedFeatures.MapBudget);
        await Assert.That(scene.Shadows.ResolveSlot(127)).IsEqualTo(0);
        await Assert.That(scene.Shadows.ResolveSlot(96)).IsEqualTo(-1);
        byte[] restoredGpu = WideTransactionGpu(scene);
        await Assert.That(restoredGpu.SequenceEqual(beforeGpu)).IsTrue();
        await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(restoredGpu.AsSpan(15020)))
            .IsEqualTo(1f).Because("the candidate 0x1 authored-direct flag must also roll back");
        await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(
                restoredGpu.AsSpan(4320 + (127 * 16) + 12)))
            .IsEqualTo(512f);

        // The exact same prefix now commits when only the final mesh hash is
        // repaired. This also proves that no preceding command caused refusal.
        _ = scene.Apply([.. prefix, .. Mesh(SecondPath, primId: 2)], 8, 42);
        await Assert.That(scene.Revision).IsEqualTo(42UL);
        await Assert.That(scene.Frame.Width).IsEqualTo(640);
        await Assert.That(scene.Frame.LightCount).IsEqualTo(97u);
        await Assert.That(scene.Frame.DomeCount).IsEqualTo(1u);
        await Assert.That(scene.Frame.Domes[1].IsPresent).IsFalse();
        await Assert.That(scene.Frame.Revision).IsEqualTo(before.Frame + 1);
        await Assert.That(scene.LightLinks.Revision).IsEqualTo(before.LightLinks + 1);
        await Assert.That(scene.Shadows.Revision).IsEqualTo(before.Shadows + 1);
        await Assert.That(scene.MaterialRevision).IsEqualTo(before.Material + 1);
        await Assert.That(scene.EnvironmentRevision).IsEqualTo(before.Environment + 1);
        await Assert.That(scene.GeometryRevision).IsGreaterThan(before.Geometry);
        await Assert.That(scene.PickIdentities.Revision).IsGreaterThan(before.Pick);
        await Assert.That(scene.Shadows.Descriptors).IsSameReferenceAs(retainedDescriptors);
        await Assert.That(retainedDescriptors.Count).IsEqualTo(2);
        await Assert.That(scene.Shadows.ResolveSlot(127)).IsEqualTo(-1);
        await Assert.That(scene.Shadows.ResolveSlot(96)).IsEqualTo(0);
        await Assert.That(scene.Shadows.ResolveSlot(64)).IsEqualTo(1);
        await Assert.That(scene.Meshes.Count).IsEqualTo(3);
        await Assert.That(scene.Materials.ContainsKey(MaterialPath)).IsFalse();
        await Assert.That(scene.LightLinks.Resolve(FirstPath, 0))
            .IsEqualTo(new SilkLightLinkMasks(UInt128.One << 64, high96 | 1, 1));
        await Assert.That(scene.LightLinks.HasDomeLinks).IsFalse();
        await Assert.That(scene.LightLinks.DomeCount).IsEqualTo(1u);
        await Assert.That(scene.LightLinks.UnsupportedFeatures)
            .IsEqualTo(SilkLightLinkUnsupportedFeatures.None);
        byte[] acceptedGpu = WideTransactionGpu(scene);
        await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(acceptedGpu.AsSpan(15020)))
            .IsEqualTo(0f);
        await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(
                acceptedGpu.AsSpan(4320 + (127 * 16) + 12)))
            .IsEqualTo(0f);
    }

    private static byte[] WideTransactionFrame(bool replacement)
    {
        int count = replacement ? 97 : 128;
        var bytes = new byte[23368];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 23368u);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), replacement ? 640 : 320);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), replacement ? 360 : 180);
        for (int index = 0; index < 16; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16 + (index * 8)),
                index == 12 ? replacement ? 10 : 5 : index % 5 == 0 ? 1 : 0);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(144 + (index * 8)),
                index switch { 0 => replacement ? 4 : 2, 5 => 4, 10 => 8, 15 => 1, _ => 0 });
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(272), 2u);
        for (int index = 0; index < 8; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(280 + (index * 8)),
                (index + 1) * (replacement ? 2d : 1d));
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(536), (uint)count);
        // Explicit candidate flag encoding, pending the coordinator's confirmation.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(540), replacement ? 0u : 0x1u);
        for (int light = 0; light < count; light++)
        {
            int entry = 552 + (light * 176);
            float t = light + 1;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry), (uint)((light % 5) + 1));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 4), 1u);
            TransactionFloats(bytes, entry + 8, t / 4, t / 2,
                t / 128, t / 64, t / 32, t * (replacement ? 3 : 2));
            for (int index = 0; index < 16; index++)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(entry + 32 + (index * 8)),
                    index switch { 12 => t, 13 => 2 * t, 14 => -t, _ => index % 5 == 0 ? 1 : 0 });
            }
            TransactionFloats(bytes, entry + 160, 1, 0.5f, 0.75f, t / 16);
        }
        TransactionFloats(bytes, 23080,
            replacement ? 0.75f : 0.25f, replacement ? 0.125f : 0.5f,
            replacement ? 0.875f : 0.75f, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(23096), replacement ? 1u : 2u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(23128), 3u);
        if (!replacement)
        {
            TransactionFloats(bytes, 23144, 0.5f, 0.75f, 1);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(23160), 1u);
        }
        return bytes;
    }

    private static byte[] WideTransactionLinks(
        uint count, uint domeCount, bool truncated,
        params (string Path, int Instance, UInt128 Light, UInt128 Shadow, uint Dome)[] entries)
    {
        var bytes = new byte[24 + entries.Sum(entry => 44 + Encoding.UTF8.GetByteCount(entry.Path))];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.LightLink);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)entries.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), count);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), truncated ? 1u : 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), domeCount);
        int offset = 24;
        foreach ((string path, int instance, UInt128 light, UInt128 shadow, uint dome) in entries)
        {
            byte[] pathBytes = Encoding.UTF8.GetBytes(path);
            BinaryPrimitives.WriteUInt128LittleEndian(bytes.AsSpan(offset), light);
            BinaryPrimitives.WriteUInt128LittleEndian(bytes.AsSpan(offset + 16), shadow);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 32), dome);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 36), instance);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 40), (uint)pathBytes.Length);
            pathBytes.CopyTo(bytes, offset + 44);
            offset += 44 + pathBytes.Length;
        }
        return bytes;
    }

    private static byte[] WideTransactionGpu(SilkSceneState scene)
    {
        var bytes = new byte[15296];
        SilkShadowFrameBinding shadows = SilkShadowFrameBinding.Create(
            scene.Shadows.Descriptors, SilkShadowAtlasLayout.Create(scene.Shadows.Descriptors)!);
        SilkFrameUniformWriter.Write(
            scene.Frame, bytes, false, RenderOutputTransform.Identity, 0, shadows: shadows);
        return bytes;
    }

    private static void TransactionFloats(byte[] bytes, int offset, params float[] values)
    {
        for (int index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + (index * 4)), values[index]);
        }
    }
}
