// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics;

namespace OpenUsd.NativeProbe;

internal static partial class Program
{
    private static void RunUsdPhysicsProbe(string directory)
    {
        RigidBodyMassLimitAndDriveRoundTripOnRealStage(directory);
        JointRelationshipsAndFramesRoundTripOnRealStage(directory);
        TryWrapWrongTypeFailsPredictably(directory);
    }

    private static void RigidBodyMassLimitAndDriveRoundTripOnRealStage(string directory)
    {
        string stagePath = Path.Combine(directory, "usdphysics-rigid-body-roundtrip.usda");
        File.Delete(stagePath);
        using UsdStage stage = UsdStage.Create(stagePath);
        UsdPrim bodyPrim = stage.DefinePrim("/World/Body", "Xform");

        UsdPhysicsRigidBodyAPI rigidBody = UsdPhysicsRigidBodyAPI.Apply(bodyPrim);
        rigidBody.RigidBodyEnabled = true;
        rigidBody.KinematicEnabled = true;
        rigidBody.StartsAsleep = true;
        rigidBody.Velocity = new UsdVec3f(1.25F, 2.5F, 3.75F);
        rigidBody.AngularVelocity = new UsdVec3f(4.25F, 5.5F, 6.75F);

        UsdPhysicsMassAPI mass = UsdPhysicsMassAPI.Apply(bodyPrim);
        mass.Mass = 42.5F;
        mass.Density = 7.25F;
        mass.CenterOfMass = new UsdVec3f(0.25F, 0.5F, 0.75F);
        mass.DiagonalInertia = new UsdVec3f(8.5F, 9.5F, 10.5F);
        mass.PrincipalAxes = new UsdQuatf(0.5F, 0.25F, 0.125F, 0.0625F);

        UsdPhysicsLimitAPI limit = UsdPhysicsLimitAPI.Apply(bodyPrim, UsdPhysicsTokens.RotX);
        limit.Low = -15.5F;
        limit.High = 30.25F;

        UsdPhysicsDriveAPI drive = UsdPhysicsDriveAPI.Apply(bodyPrim, UsdPhysicsTokens.Angular);
        drive.Type = UsdPhysicsDriveType.Acceleration;
        drive.MaxForce = 100.5F;
        drive.TargetPosition = 12.25F;
        drive.TargetVelocity = 3.5F;
        drive.Damping = 4.75F;
        drive.Stiffness = 5.125F;

        IReadOnlyList<string> appliedSchemas = bodyPrim.GetAppliedSchemas();
        Require(appliedSchemas.Contains("PhysicsRigidBodyAPI"), "Rigid body API was not applied.");
        Require(appliedSchemas.Contains("PhysicsMassAPI"), "Mass API was not applied.");
        Require(appliedSchemas.Contains("PhysicsLimitAPI:rotX"), "Limit API instance was not applied.");
        Require(appliedSchemas.Contains("PhysicsDriveAPI:angular"), "Drive API instance was not applied.");
        Require(UsdPhysicsRigidBodyAPI.Has(bodyPrim), "Rigid body API Has returned false.");
        Require(UsdPhysicsMassAPI.Has(bodyPrim), "Mass API Has returned false.");
        Require(UsdPhysicsLimitAPI.Has(bodyPrim, UsdPhysicsTokens.RotX), "Limit API Has returned false.");
        Require(UsdPhysicsDriveAPI.Has(bodyPrim, UsdPhysicsTokens.Angular), "Drive API Has returned false.");

        UsdPhysicsRigidBodyAPI readRigidBody = UsdPhysicsRigidBodyAPI.Wrap(bodyPrim);
        Require(readRigidBody.RigidBodyEnabled, "Rigid body enabled did not round-trip.");
        Require(readRigidBody.KinematicEnabled, "Kinematic enabled did not round-trip.");
        Require(readRigidBody.StartsAsleep, "Starts asleep did not round-trip.");
        Require(readRigidBody.Velocity == new UsdVec3f(1.25F, 2.5F, 3.75F), "Velocity did not round-trip.");
        Require(
            readRigidBody.AngularVelocity == new UsdVec3f(4.25F, 5.5F, 6.75F),
            "Angular velocity did not round-trip.");

        UsdPhysicsMassAPI readMass = UsdPhysicsMassAPI.Wrap(bodyPrim);
        RequireNear(readMass.Mass, 42.5F, "Mass");
        RequireNear(readMass.Density, 7.25F, "Density");
        Require(readMass.CenterOfMass == new UsdVec3f(0.25F, 0.5F, 0.75F), "Center of mass did not round-trip.");
        Require(
            readMass.DiagonalInertia == new UsdVec3f(8.5F, 9.5F, 10.5F),
            "Diagonal inertia did not round-trip.");
        Require(
            readMass.PrincipalAxes == new UsdQuatf(0.5F, 0.25F, 0.125F, 0.0625F),
            "Principal axes did not round-trip.");

        UsdPhysicsLimitAPI readLimit = UsdPhysicsLimitAPI.Wrap(bodyPrim, UsdPhysicsTokens.RotX);
        RequireNear(readLimit.Low, -15.5F, "Limit low");
        RequireNear(readLimit.High, 30.25F, "Limit high");

        UsdPhysicsDriveAPI readDrive = UsdPhysicsDriveAPI.Wrap(bodyPrim, UsdPhysicsTokens.Angular);
        Require(readDrive.Type == UsdPhysicsDriveType.Acceleration, "Drive type did not round-trip.");
        RequireNear(readDrive.MaxForce, 100.5F, "Drive max force");
        RequireNear(readDrive.TargetPosition, 12.25F, "Drive target position");
        RequireNear(readDrive.TargetVelocity, 3.5F, "Drive target velocity");
        RequireNear(readDrive.Damping, 4.75F, "Drive damping");
        RequireNear(readDrive.Stiffness, 5.125F, "Drive stiffness");
    }

    private static void JointRelationshipsAndFramesRoundTripOnRealStage(string directory)
    {
        string stagePath = Path.Combine(directory, "usdphysics-joint-roundtrip.usda");
        File.Delete(stagePath);
        using UsdStage stage = UsdStage.Create(stagePath);
        stage.DefinePrim("/World/Body0", "Xform");
        stage.DefinePrim("/World/Body1", "Xform");
        UsdPhysicsRevoluteJoint revolute = stage.DefinePhysicsRevoluteJoint("/World/Hinge");
        UsdPhysicsJoint joint = revolute.Joint;

        joint.SetBody0("/World/Body0");
        joint.SetBody1("/World/Body1");
        joint.LocalPos0 = new UsdVec3f(1, 2, 3);
        joint.LocalPos1 = new UsdVec3f(4, 5, 6);
        joint.LocalRot0 = new UsdQuatf(1, 0.1F, 0.2F, 0.3F);
        joint.LocalRot1 = new UsdQuatf(0.9F, 0.4F, 0.5F, 0.6F);
        joint.JointEnabled = false;
        joint.CollisionEnabled = true;
        joint.ExcludeFromArticulation = true;
        joint.BreakForce = 123.5F;
        joint.BreakTorque = 456.75F;
        revolute.Axis = UsdPhysicsAxis.Z;
        revolute.LowerLimit = -45;
        revolute.UpperLimit = 90;

        UsdPhysicsRevoluteJoint readRevolute = UsdPhysicsRevoluteJoint.Wrap(stage.GetPrim("/World/Hinge"));
        UsdPhysicsJoint readJoint = readRevolute.Joint;
        Require(readJoint.GetBody0().SequenceEqual(["/World/Body0"]), "Joint body0 relationship did not round-trip.");
        Require(readJoint.GetBody1().SequenceEqual(["/World/Body1"]), "Joint body1 relationship did not round-trip.");
        Require(readJoint.LocalPos0 == new UsdVec3f(1, 2, 3), "Joint localPos0 did not round-trip.");
        Require(readJoint.LocalPos1 == new UsdVec3f(4, 5, 6), "Joint localPos1 did not round-trip.");
        Require(readJoint.LocalRot0 == new UsdQuatf(1, 0.1F, 0.2F, 0.3F), "Joint localRot0 did not round-trip.");
        Require(readJoint.LocalRot1 == new UsdQuatf(0.9F, 0.4F, 0.5F, 0.6F), "Joint localRot1 did not round-trip.");
        Require(!readJoint.JointEnabled, "Joint enabled did not round-trip.");
        Require(readJoint.CollisionEnabled, "Joint collision enabled did not round-trip.");
        Require(readJoint.ExcludeFromArticulation, "Exclude from articulation did not round-trip.");
        RequireNear(readJoint.BreakForce, 123.5F, "Joint break force");
        RequireNear(readJoint.BreakTorque, 456.75F, "Joint break torque");
        Require(readRevolute.Axis == UsdPhysicsAxis.Z, "Revolute axis did not round-trip.");
        RequireNear(readRevolute.LowerLimit, -45, "Revolute lower limit");
        RequireNear(readRevolute.UpperLimit, 90, "Revolute upper limit");
    }

    private static void TryWrapWrongTypeFailsPredictably(string directory)
    {
        string stagePath = Path.Combine(directory, "usdphysics-wrong-type.usda");
        File.Delete(stagePath);
        using UsdStage stage = UsdStage.Create(stagePath);
        UsdPrim prim = stage.DefinePrim("/World/PlainXform", "Xform");

        bool wrappedScene = UsdPhysicsScene.TryWrap(prim, out UsdPhysicsScene scene);
        bool wrappedJoint = UsdPhysicsJoint.TryWrap(prim, out UsdPhysicsJoint joint);

        Require(!wrappedScene, "TryWrap unexpectedly returned a physics scene for an Xform prim.");
        Require(!wrappedJoint, "TryWrap unexpectedly returned a physics joint for an Xform prim.");
        Require(string.IsNullOrEmpty(scene.Path), "Failed scene TryWrap returned a usable path.");
        Require(string.IsNullOrEmpty(joint.Path), "Failed joint TryWrap returned a usable path.");
    }

    private static void RequireNear(float actual, float expected, string label)
    {
        if (Math.Abs(actual - expected) > 1e-6F)
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
