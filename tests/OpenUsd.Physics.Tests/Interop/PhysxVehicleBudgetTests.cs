// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests.Interop;

/// <summary>
/// Guards the vehicle wheel and gear budgets of the managed validator. Both bounds sit in front of
/// fixed arrays the simulation SDK owns, so a raw page carrying <c>0xFFFFFFFF</c> wheels or forward
/// gears must be refused rather than wrapping past the budget check and letting the runtime write
/// past a brake, steer, differential, axle or gear ratio table.
/// </summary>
public sealed class PhysxVehicleBudgetTests
{
    /// <summary>Where the forward gear count sits inside one vehicle record.</summary>
    private static readonly int ForwardGearCountOffset =
        (int)Marshal.OffsetOf<PhysxVehicleDesc>(nameof(PhysxVehicleDesc.ForwardGearCount));

    /// <summary>Where the wheel count sits inside one vehicle record.</summary>
    private static readonly int WheelCountOffset =
        (int)Marshal.OffsetOf<PhysxVehicleDesc>(nameof(PhysxVehicleDesc.WheelCount));

    [Test]
    [Arguments(uint.MaxValue)]
    [Arguments(32u)]
    [Arguments(PhysxAbi.MaxVehicleWheels + 1)]
    [Arguments(0u)]
    public async Task AWheelCountOutsideTheBudgetIsRejected(uint wheelCount)
    {
        byte[] page = CreateVehiclePage(4, PhysxAbi.MaxVehicleWheels);
        PatchVehicleField(page, WheelCountOffset, wheelCount);

        PhysxPageValidationResult result = PhysxPageValidator.Validate(page);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.ErrorCode).IsEqualTo(PhysxPageError.Range);
        await Assert.That(result.Validation.Section).IsEqualTo((uint)PhysxPageSection.Vehicles);
        await Assert.That(result.Message).Contains("wheel budget");
    }

    [Test]
    public async Task TheBuilderRefusesToEmitAPageOutsideTheWheelBudget()
    {
        InvalidOperationException? failure = null;
        try
        {
            CreateVehiclePage(4, PhysxAbi.MaxVehicleWheels + 1);
        }
        catch (InvalidOperationException error)
        {
            failure = error;
        }

        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Message).Contains("wheel budget");
    }

    [Test]
    public async Task TheWidestVehicleTheBudgetAllowsIsAccepted()
    {
        byte[] page = CreateVehiclePage(4, PhysxAbi.MaxVehicleWheels);
        PhysxPageValidationResult result = PhysxPageValidator.Validate(page);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.ErrorCode).IsEqualTo(PhysxPageError.None);
    }

    [Test]
    [Arguments(uint.MaxValue)]
    [Arguments(uint.MaxValue - 1)]
    [Arguments(PhysxAbi.MaxVehicleGears)]
    [Arguments(PhysxAbi.MaxVehicleGears - 1)]
    [Arguments(0u)]
    public async Task AForwardGearCountOutsideTheBudgetIsRejected(uint forwardGearCount)
    {
        byte[] page = CreateVehiclePage(PhysxAbi.MaxVehicleGears - 2);
        PatchForwardGearCount(page, forwardGearCount);

        PhysxPageValidationResult result = PhysxPageValidator.Validate(page);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.ErrorCode).IsEqualTo(PhysxPageError.Value);
        await Assert.That(result.Validation.Section).IsEqualTo((uint)PhysxPageSection.Vehicles);
        await Assert.That(result.Message).Contains("forward gear count");
    }

    [Test]
    [Arguments(uint.MaxValue)]
    [Arguments(PhysxAbi.MaxVehicleGears - 1)]
    [Arguments(0u)]
    public async Task TheBuilderRefusesToEmitAPageOutsideTheGearBudget(uint forwardGearCount)
    {
        InvalidOperationException? failure = null;
        try
        {
            CreateVehiclePage(forwardGearCount);
        }
        catch (InvalidOperationException error)
        {
            failure = error;
        }

        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Message).Contains("forward gear count");
    }

    [Test]
    public async Task TheWidestGearboxTheBudgetAllowsIsAccepted()
    {
        byte[] page = CreateVehiclePage(PhysxAbi.MaxVehicleGears - 2);
        PhysxPageValidationResult result = PhysxPageValidator.Validate(page);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.ErrorCode).IsEqualTo(PhysxPageError.None);
    }

    private static void PatchForwardGearCount(byte[] page, uint forwardGearCount) =>
        PatchVehicleField(page, ForwardGearCountOffset, forwardGearCount);

    private static void PatchVehicleField(byte[] page, int fieldOffset, uint value)
    {
        PhysxBuildPageHeader header = MemoryMarshal.Read<PhysxBuildPageHeader>(page);
        int offset = checked((int)header.Vehicles.Offset) + fieldOffset;
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(offset), value);
    }

    private static byte[] CreateVehiclePage(uint forwardGearCount, uint wheelCount = 1)
    {
        using var builder = new PhysxPageBuilder
        {
            Revision = 1,
            SourceHash = 2,
            MetersPerUnit = 1.0,
            KilogramsPerUnit = 1.0,
            TimeCodesPerSecond = 24.0,
            StartTimeCode = 0.0,
            EndTimeCode = 1.0,
            UpAxis = PhysxUpAxis.Y,
            SimulationRateHz = 60,
            MaxSubsteps = 1
        };

        builder.AddScene(new PhysxSceneDesc
        {
            Id = builder.DefineIdentity("/Car/PhysicsScene"),
            GravityDirection = new PhysxVec3f(0.0F, -1.0F, 0.0F),
            GravityMagnitude = 9.81F,
            PositionIterations = 4,
            VelocityIterations = 1,
            BounceThreshold = 0.2F,
            ContactOffset = 0.02F
        });

        builder.AddMaterial(new PhysxMaterialDesc
        {
            Id = builder.DefineIdentity("/Car/Material"),
            StaticFriction = 1.0F,
            DynamicFriction = 1.0F,
            Restitution = 0.0F,
            Density = 1000.0F
        });

        builder.AddShape(new PhysxShapeDesc
        {
            Id = builder.DefineIdentity("/Car/ChassisShape"),
            Type = (uint)PhysxShapeType.Box,
            LocalPose = PhysxTransform.Identity,
            Scale = new PhysxVec3f(1.0F, 1.0F, 1.0F),
            HalfExtents = new PhysxVec3f(0.9F, 0.25F, 2.0F),
            MaterialIndex = 0
        });

        builder.AddActorShape(new PhysxActorShapeRef(0, -1));
        builder.AddActor(new PhysxActorDesc
        {
            Id = builder.DefineIdentity("/Car/Chassis"),
            SceneIndex = 0,
            Type = (uint)PhysxActorType.Dynamic,
            WorldPose = PhysxTransform.Identity,
            Mass = 1500.0F,
            Inertia = new PhysxVec3f(3625.0F, 3625.0F, 750.0F),
            ShapeOffset = 0,
            ShapeCount = 1
        });

        builder.AddVehicle(new PhysxVehicleDesc
        {
            Id = builder.DefineIdentity("/Car"),
            SceneIndex = 0,
            ActorIndex = 0,
            WheelOffset = 0,
            WheelCount = wheelCount,
            Flags = (uint)PhysxVehicleFlags.AutoboxEnabled,
            Drive = (uint)PhysxVehicleDrive.Engine,
            Query = (uint)PhysxVehicleQuery.Raycast,
            LongitudinalAxis = (uint)PhysxAxis.Z,
            LateralAxis = (uint)PhysxAxis.X,
            VerticalAxis = (uint)PhysxAxis.Y,
            ChassisMass = 1500.0F,
            ChassisMoi = new PhysxVec3f(3625.0F, 3625.0F, 750.0F),
            EnginePeakTorque = 500.0F,
            EngineMoi = 1.0F,
            EngineIdleOmega = 75.0F,
            EngineMaxOmega = 600.0F,
            EngineDampingFullThrottle = 0.15F,
            EngineDampingZeroThrottleClutchEngaged = 2.0F,
            EngineDampingZeroThrottleClutchDisengaged = 0.35F,
            ClutchStrength = 10.0F,
            GearSwitchTime = 0.5F,
            FinalGearRatio = 4.0F,
            ReverseGearRatio = 4.0F,
            FirstGearRatio = 4.0F,
            TopGearRatio = 1.1F,
            ForwardGearCount = forwardGearCount,
            AutoboxUpRatio = 0.65F,
            AutoboxDownRatio = 0.15F,
            AutoboxLatency = 2.0F,
            MaxBrakeTorque = 3000.0F,
            MaxHandBrakeTorque = 5000.0F,
            MaxSteerAngle = 0.5F,
            DefaultFriction = 1.0F
        });

        for (uint index = 0; index < wheelCount; ++index)
        {
            builder.AddVehicleWheel(new PhysxVehicleWheelDesc
            {
                Id = builder.DefineIdentity($"/Car/Wheel{index}"),
                SuspensionAttachment = PhysxTransform.Identity,
                SuspensionTravelDir = new PhysxVec3f(0.0F, -1.0F, 0.0F),
                SuspensionTravelDist = 0.25F,
                WheelAttachment = PhysxTransform.Identity,
                Radius = 0.35F,
                HalfWidth = 0.15F,
                Mass = 20.0F,
                DampingRate = 0.25F,
                SuspensionStiffness = 35000.0F,
                SuspensionDamping = 4500.0F,
                SprungMass = 1500.0F / wheelCount,
                TireLatStiffX = 0.01F,
                TireLatStiffY = 18.0F,
                TireLongStiff = 5000.0F,
                TireFriction = 1.0F,
                BrakeResponse = 1.0F,
                DriveTorqueRatio = 1.0F / wheelCount,
                AxleIndex = index / 2u,
                Flags = (uint)(PhysxVehicleWheelFlags.Brakes | PhysxVehicleWheelFlags.Driven)
            });
        }

        using PhysxBuildPage page = builder.Build();
        return page.Bytes.ToArray();
    }
}
