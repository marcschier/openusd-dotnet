// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Baking;

namespace OpenUsd.Physics.Tests;

/// <summary>
/// Proves the deformable half of a published frame reaches the preview and bake contract.
/// </summary>
/// <remarks>
/// A rigid pose reaches the session overlay and the bake as a transform, and a deforming body
/// reaches them as a point sample. Both renderers draw the stage, so a point sample authored into
/// the overlay is what makes a simulated cloth or volume visible without either backend growing a
/// per frame geometry upload path of its own.
/// </remarks>
internal sealed class UsdPhysicsDeformationSampleTests
{
    [Test]
    public async Task EveryPublishedWindowBecomesOnePointSample()
    {
        var frame = new UsdPhysicsFrame(1, 4, 32);
        WriteWindow(frame, 0x11UL, UsdPhysicsDeformationKind.Surface, 0, 3);
        WriteWindow(frame, 0x22UL, UsdPhysicsDeformationKind.Volume, 3, 4);
        WriteWindow(frame, 0x33UL, UsdPhysicsDeformationKind.Fluid, 7, 2);
        frame.SetDeformationCounts(3, 9, truncated: false);

        var samples = UsdPhysicsResultBatch.DeformationSamples(frame, topologyRevision: 42);

        await Assert.That(samples.Length).IsEqualTo(3);
        await Assert.That(samples[0].Domain).IsEqualTo(UsdPhysicsPointSampleDomain.Cloth);
        await Assert.That(samples[1].Domain).IsEqualTo(UsdPhysicsPointSampleDomain.Deformable);
        await Assert.That(samples[2].Domain).IsEqualTo(UsdPhysicsPointSampleDomain.Particles);
        await Assert.That(samples[0].Points.Length).IsEqualTo(3);
        await Assert.That(samples[1].Points.Length).IsEqualTo(4);
        await Assert.That(samples[2].Points.Length).IsEqualTo(2);
        foreach (UsdPhysicsPointSample sample in samples)
        {
            await Assert.That(sample.TopologyRevision).IsEqualTo(42UL);
        }
    }

    [Test]
    public async Task ASampleOutlivesTheFrameItWasCopiedFrom()
    {
        var frame = new UsdPhysicsFrame(1, 2, 8);
        WriteWindow(frame, 0x11UL, UsdPhysicsDeformationKind.Surface, 0, 3);
        frame.SetDeformationCounts(1, 3, truncated: false);

        var samples = UsdPhysicsResultBatch.DeformationSamples(frame, topologyRevision: 1);
        double first = samples[0].Points[0].Y;

        // The frame is reused forever, so the next step overwrites the very
        // vertices the sample was produced from. A sample that borrowed them
        // would silently describe a different step than the one it was taken at.
        WriteWindow(frame, 0x11UL, UsdPhysicsDeformationKind.Surface, 0, 3, offsetY: 100.0);
        frame.SetDeformationCounts(1, 3, truncated: false);

        await Assert.That(samples[0].Points[0].Y).IsEqualTo(first);
    }

    [Test]
    public async Task AFrameWithoutDeformationProducesNoSample()
    {
        var frame = new UsdPhysicsFrame(1, 0, 0);

        await Assert.That(UsdPhysicsResultBatch.DeformationSamples(frame, 1).IsEmpty).IsTrue();

        // Deriving samples must stay opt in: a host that has not bound its
        // deformable prims would otherwise turn every window into a rejected
        // bake record the moment a stage grew a cloth.
        await Assert.That(UsdPhysicsResultBatch.FromFrame(frame, 1).PointSamples.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ABatchCarriesTheSamplesItWasGiven()
    {
        var frame = new UsdPhysicsFrame(1, 2, 8);
        WriteWindow(frame, 0x11UL, UsdPhysicsDeformationKind.Surface, 0, 3);
        frame.SetDeformationCounts(1, 3, truncated: false);

        UsdPhysicsResultBatch batch = UsdPhysicsResultBatch.FromFrame(
            frame,
            identityRevision: 9,
            UsdPhysicsResultBatch.DeformationSamples(frame, topologyRevision: 9));

        await Assert.That(batch.PointSamples.Count).IsEqualTo(1);
        await Assert.That(batch.PointSamples[0].Id.Value).IsEqualTo(0x11UL);
        await Assert.That(batch.RecordCount).IsEqualTo(1);
    }

    private static void WriteWindow(
        UsdPhysicsFrame frame,
        ulong id,
        UsdPhysicsDeformationKind kind,
        int vertexOffset,
        int vertexCount,
        double offsetY = 0.0)
    {
        Span<UsdPhysicsDeformation> windows = frame.DeformationBuffer;
        Span<UsdVec3d> vertices = frame.DeformationVertexBuffer;
        int slot = 0;
        while (slot < windows.Length && windows[slot].VertexCount != 0 &&
            windows[slot].Id.Value != id)
        {
            slot++;
        }

        windows[slot] = new UsdPhysicsDeformation(
            new UsdPhysicsObjectId(id, UsdPhysicsObjectKind.Deformable),
            kind,
            vertexOffset,
            vertexCount,
            IsSleeping: false);
        for (int index = 0; index < vertexCount; index++)
        {
            vertices[vertexOffset + index] = new UsdVec3d(index, index + offsetY, index * 2);
        }
    }
}
