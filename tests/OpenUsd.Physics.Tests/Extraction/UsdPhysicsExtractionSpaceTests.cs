// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using OpenUsd.Physics.Extraction;

namespace OpenUsd.Physics.Tests.Extraction;

public sealed class UsdPhysicsExtractionSpaceTests
{
    private static readonly (double X, double Y, double Z)[] Samples =
    [
        (0.0, 0.0, 0.0),
        (1.0, 2.0, 3.0),
        (-4.5, 0.25, 17.0),
        (123.5, -0.5, -9.75),
    ];

    private static readonly (double W, double X, double Y, double Z)[] Rotations =
    [
        (1.0, 0.0, 0.0, 0.0),
        (0.7071067811865476, 0.7071067811865476, 0.0, 0.0),
        (0.5, 0.5, 0.5, 0.5),
        (0.9238795325112867, 0.0, 0.3826834323650898, 0.0),
    ];

    [Test]
    [Arguments(UsdPhysicsExtractionUpAxis.Y, 1.0)]
    [Arguments(UsdPhysicsExtractionUpAxis.Y, 0.01)]
    [Arguments(UsdPhysicsExtractionUpAxis.Z, 1.0)]
    [Arguments(UsdPhysicsExtractionUpAxis.Z, 0.01)]
    [Arguments(UsdPhysicsExtractionUpAxis.X, 1.0)]
    [Arguments(UsdPhysicsExtractionUpAxis.X, 2.5)]
    public async Task PositionsRoundTripThroughSimulationSpace(
        UsdPhysicsExtractionUpAxis upAxis, double metersPerUnit)
    {
        UsdPhysicsExtractionSpace space =
            UsdPhysicsExtractionSpace.Create(metersPerUnit, upAxis);

        foreach ((double X, double Y, double Z) sample in Samples)
        {
            (double X, double Y, double Z) simulation = space.ToSimulation(sample);
            (double X, double Y, double Z) stage = space.ToStage(simulation);

            await Assert.That(stage.X).IsEqualTo(sample.X).Within(1e-9);
            await Assert.That(stage.Y).IsEqualTo(sample.Y).Within(1e-9);
            await Assert.That(stage.Z).IsEqualTo(sample.Z).Within(1e-9);
        }
    }

    [Test]
    [Arguments(UsdPhysicsExtractionUpAxis.Y)]
    [Arguments(UsdPhysicsExtractionUpAxis.Z)]
    [Arguments(UsdPhysicsExtractionUpAxis.X)]
    public async Task RotationsRoundTripThroughSimulationSpace(UsdPhysicsExtractionUpAxis upAxis)
    {
        UsdPhysicsExtractionSpace space = UsdPhysicsExtractionSpace.Create(0.01, upAxis);

        foreach ((double W, double X, double Y, double Z) sample in Rotations)
        {
            (double W, double X, double Y, double Z) simulation = space.ToSimulation(sample);
            (double W, double X, double Y, double Z) stage = space.ToStage(simulation);

            await Assert.That(stage.W).IsEqualTo(sample.W).Within(1e-9);
            await Assert.That(stage.X).IsEqualTo(sample.X).Within(1e-9);
            await Assert.That(stage.Y).IsEqualTo(sample.Y).Within(1e-9);
            await Assert.That(stage.Z).IsEqualTo(sample.Z).Within(1e-9);
        }
    }

    [Test]
    public async Task ZUpStageAxesMapOntoTheSimulationBasis()
    {
        UsdPhysicsExtractionSpace space =
            UsdPhysicsExtractionSpace.Create(1.0, UsdPhysicsExtractionUpAxis.Z);

        (double X, double Y, double Z) up = space.ToSimulation((0.0, 0.0, 1.0));
        (double X, double Y, double Z) forward = space.ToSimulation((0.0, 1.0, 0.0));

        await Assert.That(up.Y).IsEqualTo(1.0).Within(1e-9);
        await Assert.That(forward.Z).IsEqualTo(-1.0).Within(1e-9);
    }

    [Test]
    public async Task XUpStageAxesMapOntoTheSimulationBasis()
    {
        UsdPhysicsExtractionSpace space =
            UsdPhysicsExtractionSpace.Create(1.0, UsdPhysicsExtractionUpAxis.X);

        (double X, double Y, double Z) up = space.ToSimulation((1.0, 0.0, 0.0));

        await Assert.That(up.Y).IsEqualTo(1.0).Within(1e-9);
    }

    [Test]
    public async Task ScaleFollowsTheStageUnits()
    {
        UsdPhysicsExtractionSpace space =
            UsdPhysicsExtractionSpace.Create(0.01, UsdPhysicsExtractionUpAxis.Y);

        (double X, double Y, double Z) simulation = space.ToSimulation((100.0, 200.0, 300.0));

        await Assert.That(simulation.X).IsEqualTo(1.0).Within(1e-9);
        await Assert.That(simulation.Y).IsEqualTo(2.0).Within(1e-9);
        await Assert.That(simulation.Z).IsEqualTo(3.0).Within(1e-9);
        await Assert.That(space.MetersPerUnit).IsEqualTo(0.01).Within(1e-12);
        await Assert.That(space.UpAxis).IsEqualTo(UsdPhysicsExtractionUpAxis.Y);
    }

    [Test]
    public async Task NonPositiveUnitsAreRejected()
    {
        await Assert.That(
                () => UsdPhysicsExtractionSpace.Create(0.0, UsdPhysicsExtractionUpAxis.Y))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(
                () => UsdPhysicsExtractionSpace.Create(double.NaN, UsdPhysicsExtractionUpAxis.Y))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task EqualityComparesUnitsAndAxis()
    {
        UsdPhysicsExtractionSpace first =
            UsdPhysicsExtractionSpace.Create(0.01, UsdPhysicsExtractionUpAxis.Z);
        UsdPhysicsExtractionSpace same =
            UsdPhysicsExtractionSpace.Create(0.01, UsdPhysicsExtractionUpAxis.Z);
        UsdPhysicsExtractionSpace other =
            UsdPhysicsExtractionSpace.Create(0.01, UsdPhysicsExtractionUpAxis.Y);

        await Assert.That(first == same).IsTrue();
        await Assert.That(first != other).IsTrue();
        await Assert.That(first.GetHashCode()).IsEqualTo(same.GetHashCode());
    }

    [Test]
    public async Task UpAxisValuesMatchTheNativeAbi()
    {
        // The page stores the raw native value, so the managed enum must not be renumbered.
        int[] values =
        [
            Convert.ToInt32(UsdPhysicsExtractionUpAxis.X, CultureInfo.InvariantCulture),
            Convert.ToInt32(UsdPhysicsExtractionUpAxis.Y, CultureInfo.InvariantCulture),
            Convert.ToInt32(UsdPhysicsExtractionUpAxis.Z, CultureInfo.InvariantCulture),
        ];

        await Assert.That(values).IsEquivalentTo(ExpectedUpAxisValues);
    }

    private static readonly int[] ExpectedUpAxisValues = [0, 1, 2];
}
