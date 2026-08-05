// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Geom;
using OpenUsd.Physics;

namespace OpenUsd.NativeCoverage.Tests;

public sealed class UsdPhysicsPhysxSimulationTests
{
    [Test]
    public async Task StageStackSettlesAndRemainsSettledThroughPhysx()
    {
        RequirePhysx();
        string stagePath = CreateStagePath(nameof(StageStackSettlesAndRemainsSettledThroughPhysx));
        using UsdStage stage = UsdStage.Create(stagePath);
        DefinePhysicsScene(stage, gravityMagnitude: 9.81F);
        DefinePlane(stage, "/World/Ground", staticFriction: 0.8F, dynamicFriction: 0.6F);
        for (int index = 0; index < 10; ++index)
        {
            DefineBox(
                stage,
                $"/World/Box{index}",
                new UsdVec3d(0, 3.5 + (index * 1.05), 0),
                velocity: default,
                angularVelocity: default,
                staticFriction: 0.8F,
                dynamicFriction: 0.6F);
        }

        UsdPhysicsSimulation.Step(stage, 1.0F / 240.0F, 2400);
        double firstSettled = ReadTranslation(stage, "/World/Box0").Y;
        double lastSettled = ReadTranslation(stage, "/World/Box9").Y;
        UsdPhysicsSimulation.Step(stage, 1.0F / 240.0F, 480);
        double firstAfterHold = ReadTranslation(stage, "/World/Box0").Y;
        double lastAfterHold = ReadTranslation(stage, "/World/Box9").Y;

        await Assert.That(firstSettled > 0.49).IsTrue();
        await Assert.That(lastSettled > 9.0).IsTrue();
        await Assert.That(Math.Abs(firstAfterHold - firstSettled) < 0.05).IsTrue();
        await Assert.That(Math.Abs(lastAfterHold - lastSettled) < 0.05).IsTrue();
    }

    [Test]
    public async Task StageFrictionStopsSlidingBoxThroughPhysx()
    {
        RequirePhysx();
        string stagePath = CreateStagePath(nameof(StageFrictionStopsSlidingBoxThroughPhysx));
        using UsdStage stage = UsdStage.Create(stagePath);
        DefinePhysicsScene(stage, gravityMagnitude: 9.81F);
        DefinePlane(stage, "/World/Ground", staticFriction: 0.8F, dynamicFriction: 0.8F);
        DefineBox(
            stage,
            "/World/Slider",
            new UsdVec3d(0, 0.5, 0),
            new UsdVec3f(5, 0, 0),
            angularVelocity: default,
            staticFriction: 0.8F,
            dynamicFriction: 0.8F);

        UsdPhysicsSimulation.Step(stage, 1.0F / 240.0F, 2400);
        UsdVec3d translation = ReadTranslation(stage, "/World/Slider");

        await Assert.That(translation.X > 1.0 && translation.X < 3.0).IsTrue();
        await Assert.That(Math.Abs(translation.Y - 0.5) < 0.05).IsTrue();
    }

    [Test]
    public async Task StageSimulationIsDeterministicThroughPhysx()
    {
        RequirePhysx();
        string firstStagePath = CreateStagePath(nameof(StageSimulationIsDeterministicThroughPhysx) + "A");
        string secondStagePath = CreateStagePath(nameof(StageSimulationIsDeterministicThroughPhysx) + "B");
        CreateDeterministicStage(firstStagePath);
        CreateDeterministicStage(secondStagePath);

        using UsdStage first = UsdStage.Open(firstStagePath);
        using UsdStage second = UsdStage.Open(secondStagePath);
        UsdPhysicsSimulation.Step(first, 1.0F / 240.0F, 600);
        UsdPhysicsSimulation.Step(second, 1.0F / 240.0F, 600);

        await Assert.That(ReadTransformValues(first, "/World/Box0").SequenceEqual(
            ReadTransformValues(second, "/World/Box0"))).IsTrue();
        await Assert.That(ReadTransformValues(first, "/World/Box1").SequenceEqual(
            ReadTransformValues(second, "/World/Box1"))).IsTrue();
    }

    [Test]
    public async Task StageBulkWritebackUpdatesFirstMiddleAndLastBodiesThroughPhysx()
    {
        RequirePhysx();
        string stagePath = CreateStagePath(nameof(StageBulkWritebackUpdatesFirstMiddleAndLastBodiesThroughPhysx));
        using UsdStage stage = UsdStage.Create(stagePath);
        DefinePhysicsScene(stage, gravityMagnitude: 0.0F);
        for (int index = 0; index < 257; ++index)
        {
            DefineBox(
                stage,
                $"/World/Body{index}",
                new UsdVec3d(index * 3.0, 0.5, 0),
                new UsdVec3f(0, 0, 1.0F + (index * 0.01F)),
                angularVelocity: default,
                staticFriction: 0.0F,
                dynamicFriction: 0.0F);
        }

        UsdPhysicsSimulation.Step(stage, 1.0F / 60.0F, 60);

        await Assert.That(ReadTranslation(stage, "/World/Body0").Z > 0.9).IsTrue();
        await Assert.That(ReadTranslation(stage, "/World/Body128").Z > 2.0).IsTrue();
        await Assert.That(ReadTranslation(stage, "/World/Body256").Z > 3.4).IsTrue();
    }

    private static void RequirePhysx()
    {
        NativeCoverageRuntime.EnsureNativeLoaded();
        try
        {
            _ = UsdPhysicsSimulation.PhysxVersion;
        }
        catch (DllNotFoundException exception)
        {
            Skip.Test($"openusd_physx is not staged for this run: {exception.Message}");
        }
    }

    private static string CreateStagePath(string testName)
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(testName);
        return Path.Combine(directory, "physx-stage.usda");
    }

    private static void DefinePhysicsScene(UsdStage stage, float gravityMagnitude)
    {
        UsdPhysicsScene scene = stage.DefinePhysicsScene("/World/PhysicsScene");
        scene.GravityDirection = new UsdVec3f(0, -1, 0);
        scene.GravityMagnitude = gravityMagnitude;
    }

    private static void DefinePlane(
        UsdStage stage,
        string path,
        float staticFriction,
        float dynamicFriction)
    {
        UsdGeomPlane plane = stage.DefinePlane(path);
        UsdPhysicsCollisionAPI.Apply(plane.Prim).CollisionEnabled = true;
        UsdPhysicsMaterialAPI material = UsdPhysicsMaterialAPI.Apply(plane.Prim);
        material.StaticFriction = staticFriction;
        material.DynamicFriction = dynamicFriction;
        material.Restitution = 0.0F;
    }

    private static void DefineBox(
        UsdStage stage,
        string path,
        UsdVec3d translation,
        UsdVec3f velocity,
        UsdVec3f angularVelocity,
        float staticFriction,
        float dynamicFriction)
    {
        UsdGeomCube cube = stage.DefineCube(path);
        cube.Size = 1.0;
        cube.Xformable.SetLocalTransform(UsdMatrix4d.CreateTranslation(translation));
        UsdPhysicsCollisionAPI.Apply(cube.Prim).CollisionEnabled = true;
        UsdPhysicsRigidBodyAPI rigidBody = UsdPhysicsRigidBodyAPI.Apply(cube.Prim);
        rigidBody.RigidBodyEnabled = true;
        rigidBody.Velocity = velocity;
        rigidBody.AngularVelocity = angularVelocity;
        UsdPhysicsMaterialAPI material = UsdPhysicsMaterialAPI.Apply(cube.Prim);
        material.StaticFriction = staticFriction;
        material.DynamicFriction = dynamicFriction;
        material.Restitution = 0.0F;
    }

    private static void CreateDeterministicStage(string stagePath)
    {
        using UsdStage stage = UsdStage.Create(stagePath);
        DefinePhysicsScene(stage, gravityMagnitude: 9.81F);
        DefinePlane(stage, "/World/Ground", staticFriction: 0.8F, dynamicFriction: 0.6F);
        DefineBox(
            stage,
            "/World/Box0",
            new UsdVec3d(0, 2.0, 0),
            new UsdVec3f(0.25F, 0, 0),
            new UsdVec3f(0, 1.0F, 0),
            staticFriction: 0.8F,
            dynamicFriction: 0.6F);
        DefineBox(
            stage,
            "/World/Box1",
            new UsdVec3d(0, 3.2, 0),
            new UsdVec3f(-0.25F, 0, 0),
            new UsdVec3f(0, -1.0F, 0),
            staticFriction: 0.8F,
            dynamicFriction: 0.6F);
        stage.Save();
    }

    private static UsdVec3d ReadTranslation(UsdStage stage, string path) =>
        UsdGeomXformable.Wrap(stage.GetPrim(path)).GetLocalTransform().ExtractTranslation();

    private static double[] ReadTransformValues(UsdStage stage, string path) =>
        UsdGeomXformable.Wrap(stage.GetPrim(path)).GetLocalTransform().ToArray();
}
