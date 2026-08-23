// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Tests;

public sealed class PhysicsRenderBindingTests
{
    [Test]
    public async Task BindsResolvesAndUnbindsStableIdentities()
    {
        var table = new PhysicsRenderBindingTable(4);
        var first = new PhysicsRenderObjectId(11, PhysicsRenderObjectKind.RigidBody);
        var second = new PhysicsRenderObjectId(11, PhysicsRenderObjectKind.RigidBody, 1);

        await Assert.That(table.TryBind(first, "/World/Cube")).IsTrue();
        await Assert.That(table.TryBind(second, "/World/Cube", 1)).IsTrue();

        await Assert.That(table.Count).IsEqualTo(2);
        await Assert.That(table.TryResolve(first, out PhysicsRenderBinding resolved)).IsTrue();
        await Assert.That(resolved.PrimPath).IsEqualTo("/World/Cube");
        await Assert.That(resolved.InstanceIndex).IsEqualTo(0);
        await Assert.That(table.TryResolve(second, out PhysicsRenderBinding instanced)).IsTrue();
        await Assert.That(instanced.InstanceIndex).IsEqualTo(1);
        await Assert.That(table.Unbind(first)).IsTrue();
        await Assert.That(table.TryResolve(first, out _)).IsFalse();
        await Assert.That(table.Unbind(first)).IsFalse();
    }

    [Test]
    public async Task RebindingTheSameIdentityDoesNotConsumeCapacity()
    {
        var table = new PhysicsRenderBindingTable(1);
        var id = new PhysicsRenderObjectId(3, PhysicsRenderObjectKind.RigidBody);
        _ = table.TryBind(id, "/World/A");
        ulong revision = table.Revision;

        await Assert.That(table.TryBind(id, "/World/A")).IsTrue();
        await Assert.That(table.Revision).IsEqualTo(revision);
        await Assert.That(table.TryBind(id, "/World/B")).IsTrue();
        await Assert.That(table.Revision).IsGreaterThan(revision);
        await Assert.That(table.Count).IsEqualTo(1);
        await Assert.That(table.TryResolve(id, out PhysicsRenderBinding binding)).IsTrue();
        await Assert.That(binding.PrimPath).IsEqualTo("/World/B");
    }

    [Test]
    public async Task FullTableRefusesAndCountsFurtherBindings()
    {
        var table = new PhysicsRenderBindingTable(1);
        _ = table.TryBind(new PhysicsRenderObjectId(1, PhysicsRenderObjectKind.RigidBody), "/A");

        bool refused = table.TryBind(
            new PhysicsRenderObjectId(2, PhysicsRenderObjectKind.RigidBody),
            "/B");

        await Assert.That(refused).IsFalse();
        await Assert.That(table.RefusedBindings).IsEqualTo(1L);
        await Assert.That(table.Count).IsEqualTo(1);
    }

    [Test]
    public async Task InvalidBindingsAreRejected()
    {
        var table = new PhysicsRenderBindingTable(4);
        var id = new PhysicsRenderObjectId(1, PhysicsRenderObjectKind.RigidBody);

        _ = await Assert.That(() => table.TryBind(id, "World/Cube")).Throws<ArgumentException>();
        _ = await Assert.That(() => table.TryBind(id, "/World/Cube", -1))
            .Throws<ArgumentOutOfRangeException>();
        _ = await Assert.That(() => table.TryBind(PhysicsRenderObjectId.None, "/World/Cube"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ClearRemovesEveryBinding()
    {
        var table = new PhysicsRenderBindingTable(4);
        _ = table.TryBind(new PhysicsRenderObjectId(1, PhysicsRenderObjectKind.RigidBody), "/A");
        _ = table.TryBind(new PhysicsRenderObjectId(2, PhysicsRenderObjectKind.RigidBody), "/B");

        table.Clear();

        await Assert.That(table.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ComposedTransformIsRowMajorWithTranslationInTheLastRow()
    {
        double[] destination = new double[PhysicsRenderTransforms.ElementCount];

        PhysicsRenderTransforms.Compose(
            new UsdVec3d(2, 3, 4),
            PhysicsRenderOrientation.Identity,
            [],
            destination);

        await Assert.That(destination[0]).IsEqualTo(1d);
        await Assert.That(destination[5]).IsEqualTo(1d);
        await Assert.That(destination[10]).IsEqualTo(1d);
        await Assert.That(destination[12]).IsEqualTo(2d);
        await Assert.That(destination[13]).IsEqualTo(3d);
        await Assert.That(destination[14]).IsEqualTo(4d);
        await Assert.That(destination[15]).IsEqualTo(1d);
    }

    [Test]
    public async Task ComposedTransformRotatesAboutZ()
    {
        double[] destination = new double[PhysicsRenderTransforms.ElementCount];
        double half = Math.PI / 4;

        PhysicsRenderTransforms.Compose(
            new UsdVec3d(0, 0, 0),
            new PhysicsRenderOrientation(0, 0, Math.Sin(half), Math.Cos(half)),
            [],
            destination);

        // A quarter turn about +Z maps the local X axis onto the world Y axis.
        await Assert.That(Math.Abs(destination[0]) < 1e-12).IsTrue();
        await Assert.That(Math.Abs(destination[1] - 1) < 1e-12).IsTrue();
        await Assert.That(Math.Abs(destination[4] + 1) < 1e-12).IsTrue();
        await Assert.That(Math.Abs(destination[5]) < 1e-12).IsTrue();
        await Assert.That(Math.Abs(destination[10] - 1) < 1e-12).IsTrue();
    }

    [Test]
    public async Task AuthoredScaleIsPreservedWhilePhysicsOwnsRotationAndTranslation()
    {
        double[] authored =
        [
            2, 0, 0, 0,
            0, 3, 0, 0,
            0, 0, 4, 0,
            9, 9, 9, 1
        ];
        double[] destination = new double[PhysicsRenderTransforms.ElementCount];

        PhysicsRenderTransforms.Compose(
            new PhysicsRenderTransformOverride(
                new PhysicsRenderObjectId(1, PhysicsRenderObjectKind.RigidBody),
                new UsdVec3d(1, 2, 3),
                PhysicsRenderOrientation.Identity,
                Snapped: false),
            authored,
            destination);

        await Assert.That(destination[0]).IsEqualTo(2d);
        await Assert.That(destination[5]).IsEqualTo(3d);
        await Assert.That(destination[10]).IsEqualTo(4d);
        await Assert.That(destination[12]).IsEqualTo(1d);
        await Assert.That(destination[13]).IsEqualTo(2d);
        await Assert.That(destination[14]).IsEqualTo(3d);
    }

    [Test]
    public async Task ComposeRejectsMalformedBuffers()
    {
        double[] destination = new double[15];
        double[] valid = new double[16];

        _ = await Assert.That(() => PhysicsRenderTransforms.Compose(
            new UsdVec3d(0, 0, 0),
            PhysicsRenderOrientation.Identity,
            [],
            destination)).Throws<ArgumentException>();
        _ = await Assert.That(() => PhysicsRenderTransforms.Compose(
            new UsdVec3d(0, 0, 0),
            PhysicsRenderOrientation.Identity,
            new double[3],
            valid)).Throws<ArgumentException>();
    }

    [Test]
    public async Task AuthoredShearSurvivesCompositionWithTheIdentityRotation()
    {
        // A symmetric basis is its own polar stretch, so an identity simulated rotation must
        // reproduce it exactly instead of collapsing it to its row lengths.
        double[] authored =
        [
            1.0, 0.3, 0.0, 0,
            0.3, 2.0, 0.4, 0,
            0.0, 0.4, 1.5, 0,
            7, 8, 9, 1
        ];
        double[] destination = new double[PhysicsRenderTransforms.ElementCount];

        PhysicsRenderTransforms.Compose(
            new UsdVec3d(1, 2, 3),
            PhysicsRenderOrientation.Identity,
            authored,
            destination);

        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                int index = (row * 4) + column;
                await Assert.That(Math.Abs(destination[index] - authored[index]) < 1e-9).IsTrue();
            }
        }

        await Assert.That(destination[12]).IsEqualTo(1d);
        await Assert.That(destination[13]).IsEqualTo(2d);
        await Assert.That(destination[14]).IsEqualTo(3d);
    }

    [Test]
    public async Task NonOrthogonalShearIsCarriedThroughAPhysicsRotation()
    {
        // The shear is not axis aligned, so retaining row lengths alone would silently drop it.
        double[] authored =
        [
            1.0, 0.5, 0.0, 0,
            0.0, 1.0, 0.0, 0,
            0.2, 0.0, 1.0, 0,
            0, 0, 0, 1
        ];
        double[] destination = new double[PhysicsRenderTransforms.ElementCount];
        double half = Math.PI / 6;

        PhysicsRenderTransforms.Compose(
            new UsdVec3d(0, 0, 0),
            new PhysicsRenderOrientation(0, Math.Sin(half), 0, Math.Cos(half)),
            authored,
            destination);

        // The stretch is rotation invariant, so the composed and authored Gram matrices agree even
        // though the simulated rotation replaced the authored one.
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                double composed = 0;
                double expected = 0;
                for (int inner = 0; inner < 3; inner++)
                {
                    composed += destination[(row * 4) + inner] * destination[(column * 4) + inner];
                    expected += authored[(row * 4) + inner] * authored[(column * 4) + inner];
                }

                await Assert.That(Math.Abs(composed - expected) < 1e-9).IsTrue();
            }
        }

        // A row-length composition would have produced an orthogonal basis scaled per row, which a
        // sheared authored basis is not.
        double offDiagonal = (destination[0] * destination[4]) +
            (destination[1] * destination[5]) +
            (destination[2] * destination[6]);
        await Assert.That(Math.Abs(offDiagonal) > 1e-3).IsTrue();
    }

    [Test]
    public async Task ComposingTheAuthoredRotationRestoresTheAuthoredBasis()
    {
        // The authored basis is built as stretch times rotation, so replaying that same rotation
        // must restore the authored render state byte for byte within tolerance.
        double half = Math.PI / 5;
        var authoredRotation = new PhysicsRenderOrientation(
            Math.Sin(half) * 0.6,
            Math.Sin(half) * 0.8,
            0,
            Math.Cos(half));
        double[] stretch =
        [
            2.0, 0.4, 0.1,
            0.4, 1.3, 0.2,
            0.1, 0.2, 0.7
        ];
        double[] rotation = new double[PhysicsRenderTransforms.ElementCount];
        PhysicsRenderTransforms.Compose(
            new UsdVec3d(0, 0, 0),
            authoredRotation,
            [],
            rotation);

        double[] authored = new double[PhysicsRenderTransforms.ElementCount];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                double sum = 0;
                for (int inner = 0; inner < 3; inner++)
                {
                    sum += stretch[(row * 3) + inner] * rotation[(inner * 4) + column];
                }

                authored[(row * 4) + column] = sum;
            }
        }

        authored[15] = 1;
        double[] destination = new double[PhysicsRenderTransforms.ElementCount];

        PhysicsRenderTransforms.Compose(
            new UsdVec3d(4, 5, 6),
            authoredRotation,
            authored,
            destination);

        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                int index = (row * 4) + column;
                await Assert.That(Math.Abs(destination[index] - authored[index]) < 1e-9).IsTrue();
            }
        }
    }

    [Test]
    public async Task SingularAuthoredAxesStayCollapsedAndFinite()
    {
        // The author flattened the basis onto a plane; the simulated pose must not resurrect the
        // collapsed axis and must not produce a NaN doing it.
        double[] authored =
        [
            2, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 3, 0,
            0, 0, 0, 1
        ];
        double[] destination = new double[PhysicsRenderTransforms.ElementCount];

        PhysicsRenderTransforms.Compose(
            new UsdVec3d(0, 0, 0),
            PhysicsRenderOrientation.Identity,
            authored,
            destination);

        foreach (double element in destination)
        {
            await Assert.That(double.IsFinite(element)).IsTrue();
        }

        await Assert.That(Math.Abs(destination[0] - 2) < 1e-12).IsTrue();
        await Assert.That(Math.Abs(destination[4]) < 1e-12).IsTrue();
        await Assert.That(Math.Abs(destination[5]) < 1e-12).IsTrue();
        await Assert.That(Math.Abs(destination[6]) < 1e-12).IsTrue();
        await Assert.That(Math.Abs(destination[10] - 3) < 1e-12).IsTrue();
    }

    [Test]
    public async Task NearSingularAndOverflowingAuthoredBasesRemainFinite()
    {
        double[] nearSingular =
        [
            1, 0, 0, 0,
            1, 1e-18, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1
        ];
        double[] overflowing =
        [
            1e200, 0, 0, 0,
            0, 1e200, 0, 0,
            0, 0, 1e200, 0,
            0, 0, 0, 1
        ];
        double[] destination = new double[PhysicsRenderTransforms.ElementCount];

        PhysicsRenderTransforms.Compose(
            new UsdVec3d(0, 0, 0),
            PhysicsRenderOrientation.Identity,
            nearSingular,
            destination);
        foreach (double element in destination)
        {
            await Assert.That(double.IsFinite(element)).IsTrue();
        }

        PhysicsRenderTransforms.Compose(
            new UsdVec3d(1, 2, 3),
            PhysicsRenderOrientation.Identity,
            overflowing,
            destination);

        // The Gram matrix overflows, so the documented fallback keeps the unstretched pose.
        foreach (double element in destination)
        {
            await Assert.That(double.IsFinite(element)).IsTrue();
        }

        await Assert.That(destination[0]).IsEqualTo(1d);
        await Assert.That(destination[5]).IsEqualTo(1d);
        await Assert.That(destination[10]).IsEqualTo(1d);
        await Assert.That(destination[12]).IsEqualTo(1d);
    }

    [Test]
    public async Task NonFiniteAuthoredBasisFallsBackToTheSimulatedPose()
    {
        double[] authored =
        [
            double.NaN, 0, 0, 0,
            0, 2, 0, 0,
            0, 0, 2, 0,
            0, 0, 0, 1
        ];
        double[] destination = new double[PhysicsRenderTransforms.ElementCount];

        PhysicsRenderTransforms.Compose(
            new UsdVec3d(1, 2, 3),
            PhysicsRenderOrientation.Identity,
            authored,
            destination);

        foreach (double element in destination)
        {
            await Assert.That(double.IsFinite(element)).IsTrue();
        }

        await Assert.That(destination[0]).IsEqualTo(1d);
        await Assert.That(destination[5]).IsEqualTo(1d);
        await Assert.That(destination[10]).IsEqualTo(1d);
        await Assert.That(destination[14]).IsEqualTo(3d);
    }

    [Test]
    public async Task ComposeDoesNotAllocateOnTheWarmedPath()
    {
        double[] authored =
        [
            1.0, 0.5, 0.1, 0,
            0.2, 1.4, 0.0, 0,
            0.0, 0.3, 0.9, 0,
            0, 0, 0, 1
        ];
        double[] destination = new double[PhysicsRenderTransforms.ElementCount];
        var orientation = new PhysicsRenderOrientation(0.1, 0.2, 0.3, 0.9);

        for (int warmup = 0; warmup < 32; warmup++)
        {
            PhysicsRenderTransforms.Compose(
                new UsdVec3d(1, 2, 3),
                orientation,
                authored,
                destination);
        }

        bool quiet = false;
        for (int attempt = 0; attempt < 8 && !quiet; attempt++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 1000; iteration++)
            {
                PhysicsRenderTransforms.Compose(
                    new UsdVec3d(1, 2, 3),
                    orientation,
                    authored,
                    destination);
            }

            quiet = GC.GetAllocatedBytesForCurrentThread() - before == 0;
        }

        await Assert.That(quiet).IsTrue();
    }

    [Test]
    public async Task DegenerateOrientationFallsBackToIdentity()
    {
        double[] destination = new double[PhysicsRenderTransforms.ElementCount];

        PhysicsRenderTransforms.Compose(
            new UsdVec3d(0, 0, 0),
            new PhysicsRenderOrientation(double.NaN, 0, 0, 0),
            [],
            destination);

        await Assert.That(destination[0]).IsEqualTo(1d);
        await Assert.That(destination[5]).IsEqualTo(1d);
        await Assert.That(destination[10]).IsEqualTo(1d);
    }
}
