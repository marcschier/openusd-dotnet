// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Extraction;

namespace OpenUsd.NativeCoverage.Tests;

/// <summary>One stage that authors at least one prim for every supported physics domain.</summary>
internal static class PhysicsExtractionDomainStage
{
    internal const string Usda = """
        #usda 1.0
        (
            metersPerUnit = 1
            kilogramsPerUnit = 1
            upAxis = "Y"
            timeCodesPerSecond = 60
            startTimeCode = 0
            endTimeCode = 24
        )

        def PhysicsScene "Scene" (
            prepend apiSchemas = ["OpenUsdPhysicsSceneAPI", "OpenUsdPhysicsSimulationMetadataAPI"]
        )
        {
            vector3f physics:gravityDirection = (0, -1, 0)
            float physics:gravityMagnitude = 9.81
            int openUsdPhysics:scene:positionIterationCount = 8
            string openUsdPhysics:simulation:identity = "primary"
        }

        def Scope "PhysMaterial" (
            prepend apiSchemas = ["PhysicsMaterialAPI"]
        )
        {
            float physics:staticFriction = 0.6
            float physics:dynamicFriction = 0.5
            float physics:restitution = 0.2
            float physics:density = 1000
        }

        def PhysicsCollisionGroup "Group" (
            prepend apiSchemas = ["OpenUsdPhysicsCollisionFilterSettingsAPI"]
        )
        {
            bool openUsdPhysics:collisionFilter:invertFilteredGroups = 0
        }

        def Xform "Body" (
            prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsMassAPI",
                "PhysicsFilteredPairsAPI"]
        )
        {
            double3 xformOp:translate = (0, 4, 0)
            uniform token[] xformOpOrder = ["xformOp:translate"]
            float physics:mass = 12
            rel physics:simulationOwner = </Scene>
            rel physics:filteredPairs = </Body2>

            def Sphere "Shape" (
                prepend apiSchemas = ["PhysicsCollisionAPI"]
            )
            {
                double radius = 0.5
                float physics:contactOffset = 0.02
                rel material:binding:physics = </PhysMaterial>
            }
        }

        def Xform "Body2" (
            prepend apiSchemas = ["PhysicsRigidBodyAPI"]
        )
        {
            double3 xformOp:translate = (2, 4, 0)
            uniform token[] xformOpOrder = ["xformOp:translate"]
            rel physics:simulationOwner = </Scene>

            def Cube "Shape" (
                prepend apiSchemas = ["PhysicsCollisionAPI"]
            )
            {
                double size = 1
            }
        }

        def PhysicsFixedJoint "Joint"
        {
            rel physics:body0 = </Body>
            bool physics:jointEnabled = 1
            float physics:breakForce = 1000
        }

        def Xform "Articulation" (
            prepend apiSchemas = ["PhysicsArticulationRootAPI",
                "OpenUsdPhysicsArticulationSettingsAPI"]
        )
        {
            bool physics:articulationEnabled = 1
            int openUsdPhysics:articulation:positionIterationCount = 16
        }

        def Xform "Controller" (
            prepend apiSchemas = ["OpenUsdPhysicsCharacterControllerAPI"]
        )
        {
            float openUsdPhysics:controller:stepOffset = 0.3
        }

        def Xform "Vehicle" (
            prepend apiSchemas = ["OpenUsdPhysicsVehicleAPI", "OpenUsdPhysicsVehicleEngineAPI"]
        )
        {
            float openUsdPhysics:engine:peakTorque = 500
        }

        def Xform "Wheel" (
            prepend apiSchemas = ["OpenUsdPhysicsVehicleWheelAttachmentAPI",
                "OpenUsdPhysicsVehicleTireAPI"]
        )
        {
            float openUsdPhysics:wheel:radius = 0.35
        }

        def OpenUsdPhysicsVehicleTireFrictionTable "FrictionTable"
        {
            float[] openUsdPhysics:frictionTable:frictionValues = [0.8, 0.9]
        }

        def OpenUsdPhysicsFixedTendon "FixedTendon"
        {
            float openUsdPhysics:fixedTendon:stiffness = 10
        }

        def OpenUsdPhysicsSpatialTendon "SpatialTendon"
        {
            float openUsdPhysics:spatialTendon:damping = 2
        }

        def Xform "TendonAttachment" (
            prepend apiSchemas = ["OpenUsdPhysicsTendonAttachmentAPI"]
        )
        {
            float openUsdPhysics:tendonAttachment:gearing = 1
        }

        def Xform "Mimic" (
            prepend apiSchemas = ["OpenUsdPhysicsMimicJointAPI"]
        )
        {
            float openUsdPhysics:mimicJoint:gearing = -1
        }

        def Xform "ParticleSystem" (
            prepend apiSchemas = ["OpenUsdPhysicsParticleSystemAPI",
                "OpenUsdPhysicsParticleIsosurfaceAPI"]
        )
        {
            float openUsdPhysics:particleSystem:particleContactOffset = 0.01
        }

        def Points "ParticleSet" (
            prepend apiSchemas = ["OpenUsdPhysicsParticleSetAPI",
                "OpenUsdPhysicsDiffuseParticlesAPI"]
        )
        {
            point3f[] points = [(0, 0, 0), (0, 1, 0)]
            bool openUsdPhysics:particleSet:selfCollision = 1
        }

        def Mesh "Cloth" (
            prepend apiSchemas = ["OpenUsdPhysicsParticleClothAPI"]
        )
        {
            int[] faceVertexCounts = [3]
            int[] faceVertexIndices = [0, 1, 2]
            point3f[] points = [(0, 0, 0), (1, 0, 0), (0, 0, 1)]
            float openUsdPhysics:particleCloth:springStretchStiffness = 100
        }

        def Xform "PbdMaterial" (
            prepend apiSchemas = ["OpenUsdPhysicsPbdMaterialAPI"]
        )
        {
            float openUsdPhysics:pbdMaterial:viscosity = 0.1
        }

        def Mesh "SurfaceDeformable" (
            prepend apiSchemas = ["OpenUsdPhysicsSurfaceDeformableAPI"]
        )
        {
            int[] faceVertexCounts = [3]
            int[] faceVertexIndices = [0, 1, 2]
            point3f[] points = [(0, 0, 0), (1, 0, 0), (0, 0, 1)]
            float openUsdPhysics:surfaceDeformable:thickness = 0.01
        }

        def Xform "SurfaceDeformableMaterial" (
            prepend apiSchemas = ["OpenUsdPhysicsSurfaceDeformableMaterialAPI"]
        )
        {
            float openUsdPhysics:surfaceDeformableMaterial:youngsModulus = 5000
        }

        def Xform "VolumeDeformable" (
            prepend apiSchemas = ["OpenUsdPhysicsVolumeDeformableAPI"]
        )
        {
            bool openUsdPhysics:volumeDeformable:enabled = 1
        }

        def Xform "VolumeDeformableMaterial" (
            prepend apiSchemas = ["OpenUsdPhysicsVolumeDeformableMaterialAPI"]
        )
        {
            float openUsdPhysics:volumeDeformableMaterial:poissonsRatio = 0.45
        }

        def OpenUsdPhysicsAttachment "Attachment" (
            prepend apiSchemas = ["OpenUsdPhysicsAutoAttachmentAPI"]
        )
        {
            float openUsdPhysics:attachment:stiffness = 250
        }

        def Xform "Cooked" (
            prepend apiSchemas = ["OpenUsdPhysicsCookedDataAPI"]
        )
        {
            string openUsdPhysics:cookedData:identifier = "convex-0"
        }
        """;

    internal static readonly UsdPhysicsExtractionObjectKind[] ExpectedKinds =
    [
        UsdPhysicsExtractionObjectKind.Scene,
        UsdPhysicsExtractionObjectKind.RigidBody,
        UsdPhysicsExtractionObjectKind.Collider,
        UsdPhysicsExtractionObjectKind.Material,
        UsdPhysicsExtractionObjectKind.CollisionGroup,
        UsdPhysicsExtractionObjectKind.Joint,
        UsdPhysicsExtractionObjectKind.ArticulationRoot,
        UsdPhysicsExtractionObjectKind.FilteredPairs,
        UsdPhysicsExtractionObjectKind.CharacterController,
        UsdPhysicsExtractionObjectKind.Vehicle,
        UsdPhysicsExtractionObjectKind.VehicleWheelAttachment,
        UsdPhysicsExtractionObjectKind.VehicleFrictionTable,
        UsdPhysicsExtractionObjectKind.FixedTendon,
        UsdPhysicsExtractionObjectKind.SpatialTendon,
        UsdPhysicsExtractionObjectKind.TendonAttachment,
        UsdPhysicsExtractionObjectKind.MimicJoint,
        UsdPhysicsExtractionObjectKind.ParticleSystem,
        UsdPhysicsExtractionObjectKind.ParticleSet,
        UsdPhysicsExtractionObjectKind.ParticleCloth,
        UsdPhysicsExtractionObjectKind.DiffuseParticles,
        UsdPhysicsExtractionObjectKind.SurfaceDeformable,
        UsdPhysicsExtractionObjectKind.SurfaceDeformableMaterial,
        UsdPhysicsExtractionObjectKind.VolumeDeformable,
        UsdPhysicsExtractionObjectKind.VolumeDeformableMaterial,
        UsdPhysicsExtractionObjectKind.Attachment,
        UsdPhysicsExtractionObjectKind.CollisionFilter,
        UsdPhysicsExtractionObjectKind.CookedData,
        UsdPhysicsExtractionObjectKind.PbdMaterial,
        UsdPhysicsExtractionObjectKind.SimulationMetadata,
    ];

    internal static readonly UsdPhysicsExtractionDomains[] ExpectedDomains =
    [
        UsdPhysicsExtractionDomains.Scene,
        UsdPhysicsExtractionDomains.RigidBody,
        UsdPhysicsExtractionDomains.Collision,
        UsdPhysicsExtractionDomains.Material,
        UsdPhysicsExtractionDomains.Joint,
        UsdPhysicsExtractionDomains.Articulation,
        UsdPhysicsExtractionDomains.Tendon,
        UsdPhysicsExtractionDomains.Mimic,
        UsdPhysicsExtractionDomains.Controller,
        UsdPhysicsExtractionDomains.Vehicle,
        UsdPhysicsExtractionDomains.Particle,
        UsdPhysicsExtractionDomains.Deformable,
        UsdPhysicsExtractionDomains.Attachment,
        UsdPhysicsExtractionDomains.Filtering,
        UsdPhysicsExtractionDomains.CookedData,
        UsdPhysicsExtractionDomains.SimulationMetadata,
    ];
}
