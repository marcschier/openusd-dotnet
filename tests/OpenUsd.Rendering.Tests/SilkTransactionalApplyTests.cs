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
        const int frameSize = 2248;
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
        int entrySize = 20 + pathBytes.Length;
        var bytes = new byte[24 + entrySize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.LightLink);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), lightCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32), 0u);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(36), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), (uint)pathBytes.Length);
        pathBytes.CopyTo(bytes, 44);
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
}
