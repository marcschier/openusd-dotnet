// Copyright (c) marcschier. Licensed under the MIT License.

// One PhysX vehicle2 CPU vehicle, built from one pointer free page record and
// the wheel window that record owns.
//
// The vehicle never creates its own chassis. It drives an existing dynamic
// actor that the actor section already declared, so the chassis keeps the
// identity, the shapes, and the body state it already had, and a consumer
// binds the chassis back to its prim through the same path it uses for every
// other body.
//
// Ownership: the instance owns only the PhysX custom suspension limit
// constraints it creates. The chassis actor, the scene, and the material stay
// owned by the world.

#ifndef OPENUSD_PHYSX_VEHICLE_H
#define OPENUSD_PHYSX_VEHICLE_H

#include "openusd_physx_world.h"

#include <PxPhysicsAPI.h>
#include <vehicle2/PxVehicleAPI.h>

#include <string>
#include <vector>

namespace openusd_physx_vehicle
{
// True once the process wide vehicle extension has been initialized. The world
// calls Initialize exactly once per process before it builds a vehicle and
// Shutdown when the last runtime reference goes away.
bool Initialize(physx::PxFoundation& foundation, std::string& reason);
void Shutdown();

// Creates the unit cylinder the sweep road geometry query needs. Returns null
// when the simulation SDK cannot cook it, which is the only reason a page that
// asks for a sweep query falls back to a raycast query.
physx::PxConvexMesh* CreateSweepMesh(
    physx::PxPhysics& physics,
    uint32_t longitudinal_axis,
    uint32_t lateral_axis,
    uint32_t vertical_axis);
void DestroySweepMesh(physx::PxConvexMesh* mesh);

// Keeps a road geometry query from finding the vehicle it is cast from. Without
// it the very first ray leaves the chassis collider and reports a road surface
// at zero distance, which fully compresses every suspension and launches the
// vehicle. The chassis is the only actor rejected, so a vehicle still finds
// every other body it drives over.
class ChassisFilter final : public physx::PxQueryFilterCallback
{
public:
    void SetChassis(const physx::PxRigidBody* chassis) noexcept
    {
        chassis_ = chassis;
    }

    physx::PxQueryHitType::Enum preFilter(
        const physx::PxFilterData& filterData,
        const physx::PxShape* shape,
        const physx::PxRigidActor* actor,
        physx::PxHitFlags& queryFlags) override;

    physx::PxQueryHitType::Enum postFilter(
        const physx::PxFilterData& filterData,
        const physx::PxQueryHit& hit,
        const physx::PxShape* shape,
        const physx::PxRigidActor* actor) override;

private:
    const physx::PxRigidBody* chassis_ = nullptr;
};

class Instance final
    : public physx::vehicle2::PxVehiclePhysXActorBeginComponent
    , public physx::vehicle2::PxVehiclePhysXRoadGeometrySceneQueryComponent
    , public physx::vehicle2::PxVehicleSuspensionComponent
    , public physx::vehicle2::PxVehicleTireComponent
    , public physx::vehicle2::PxVehiclePhysXConstraintComponent
    , public physx::vehicle2::PxVehicleEngineDriveCommandResponseComponent
    , public physx::vehicle2::PxVehicleMultiWheelDriveDifferentialStateComponent
    , public physx::vehicle2::PxVehicleEngineDriveActuationStateComponent
    , public physx::vehicle2::PxVehicleEngineDrivetrainComponent
    , public physx::vehicle2::PxVehicleWheelComponent
    , public physx::vehicle2::PxVehicleRigidBodyComponent
{
public:
    Instance() = default;
    ~Instance();

    Instance(const Instance&) = delete;
    Instance& operator=(const Instance&) = delete;
    Instance(Instance&&) = delete;
    Instance& operator=(Instance&&) = delete;

    // Builds every parameter block and every state block from the page record.
    // Returns false and fills `reason` when the simulation SDK rejects a value
    // the page contract cannot reject on its own.
    bool Configure(
        const openusd_physx_vehicle_desc& desc,
        const openusd_physx_vehicle_wheel_desc* wheels,
        physx::PxRigidBody& chassis,
        physx::PxScene& scene,
        physx::PxPhysics& physics,
        physx::PxMaterial* material,
        physx::PxConvexMesh* sweep_mesh,
        std::string& reason);

    // Releases the custom constraints. Safe to call more than once.
    void Release();

    // Records the driver input the next step applies. `gear` is zero when the
    // gear is left to the drivetrain and otherwise the one based target gear
    // index. Zero asks the autobox to choose while the vehicle declares
    // `OPENUSD_PHYSX_VEHICLE_FLAG_AUTOBOX_ENABLED`, and holds the current gear
    // while it does not, because a vehicle without an autobox has nothing that
    // could choose a gear on the driver's behalf.
    void SetCommands(float throttle, float brake, float hand_brake, float steer, float clutch, uint32_t gear) noexcept;

    // Runs the whole component sequence for one substep and writes the result
    // back onto the chassis actor.
    void Step(float dt, const physx::PxVec3& gravity);

    // Restores the vehicle to the state it was configured with.
    void Reset();

    uint64_t Id() const noexcept
    {
        return id_;
    }

    uint32_t WheelCount() const noexcept
    {
        return static_cast<uint32_t>(wheel_ids_.size());
    }

    uint64_t WheelId(uint32_t index) const noexcept
    {
        return wheel_ids_[index];
    }

    bool PublishesWheels() const noexcept
    {
        return publish_wheels_;
    }

    // The wheel pose in world space, resolved from the chassis pose and the
    // wheel local pose the last step produced.
    physx::PxTransform WheelPose(uint32_t index) const noexcept;

    physx::PxVec3 WheelVelocity(uint32_t index) const noexcept;

    uint32_t CurrentGear() const noexcept
    {
        return gearbox_state_.currentGear;
    }

    // True when the vehicle declared an autobox, which is the only case in
    // which the drivetrain is allowed to choose a gear by itself.
    bool AutoboxEnabled() const noexcept
    {
        return autobox_enabled_;
    }

    float EngineSpeed() const noexcept
    {
        return engine_state_.rotationSpeed;
    }

    physx::PxRigidBody* Chassis() const noexcept
    {
        return physx_actor_.rigidBody;
    }

    // Component data hooks. Each one hands the simulation SDK the blocks this
    // instance owns; none of them allocates.
    void getDataForPhysXActorBeginComponent(
        const physx::vehicle2::PxVehicleAxleDescription*& axleDescription,
        const physx::vehicle2::PxVehicleCommandState*& commands,
        const physx::vehicle2::PxVehicleEngineDriveTransmissionCommandState*& transmissionCommands,
        const physx::vehicle2::PxVehicleGearboxParams*& gearParams,
        const physx::vehicle2::PxVehicleGearboxState*& gearState,
        const physx::vehicle2::PxVehicleEngineParams*& engineParams,
        physx::vehicle2::PxVehiclePhysXActor*& physxActor,
        physx::vehicle2::PxVehiclePhysXSteerState*& physxSteerState,
        physx::vehicle2::PxVehiclePhysXConstraints*& physxConstraints,
        physx::vehicle2::PxVehicleRigidBodyState*& rigidBodyState,
        physx::vehicle2::PxVehicleArrayData<physx::vehicle2::PxVehicleWheelRigidBody1dState>& wheelRigidBody1dStates,
        physx::vehicle2::PxVehicleEngineState*& engineState) override;

    void getDataForPhysXRoadGeometrySceneQueryComponent(
        const physx::vehicle2::PxVehicleAxleDescription*& axleDescription,
        const physx::vehicle2::PxVehiclePhysXRoadGeometryQueryParams*& roadGeomParams,
        physx::vehicle2::PxVehicleArrayData<const physx::PxReal>& steerResponseStates,
        const physx::vehicle2::PxVehicleRigidBodyState*& rigidBodyState,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleWheelParams>& wheelParams,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleSuspensionParams>& suspensionParams,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehiclePhysXMaterialFrictionParams>& materialFrictionParams,
        physx::vehicle2::PxVehicleArrayData<physx::vehicle2::PxVehicleRoadGeometryState>& roadGeometryStates,
        physx::vehicle2::PxVehicleArrayData<physx::vehicle2::PxVehiclePhysXRoadGeometryQueryState>& physxRoadGeometryStates) override;

    void getDataForSuspensionComponent(
        const physx::vehicle2::PxVehicleAxleDescription*& axleDescription,
        const physx::vehicle2::PxVehicleRigidBodyParams*& rigidBodyParams,
        const physx::vehicle2::PxVehicleSuspensionStateCalculationParams*& suspensionStateCalculationParams,
        physx::vehicle2::PxVehicleArrayData<const physx::PxReal>& steerResponseStates,
        const physx::vehicle2::PxVehicleRigidBodyState*& rigidBodyState,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleWheelParams>& wheelParams,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleSuspensionParams>& suspensionParams,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleSuspensionComplianceParams>& suspensionComplianceParams,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleSuspensionForceParams>& suspensionForceParams,
        physx::vehicle2::PxVehicleSizedArrayData<const physx::vehicle2::PxVehicleAntiRollForceParams>& antiRollForceParams,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleRoadGeometryState>& wheelRoadGeomStates,
        physx::vehicle2::PxVehicleArrayData<physx::vehicle2::PxVehicleSuspensionState>& suspensionStates,
        physx::vehicle2::PxVehicleArrayData<physx::vehicle2::PxVehicleSuspensionComplianceState>& suspensionComplianceStates,
        physx::vehicle2::PxVehicleArrayData<physx::vehicle2::PxVehicleSuspensionForce>& suspensionForces,
        physx::vehicle2::PxVehicleAntiRollTorque*& antiRollTorque) override;

    void getDataForTireComponent(
        const physx::vehicle2::PxVehicleAxleDescription*& axleDescription,
        physx::vehicle2::PxVehicleArrayData<const physx::PxReal>& steerResponseStates,
        const physx::vehicle2::PxVehicleRigidBodyState*& rigidBodyState,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleWheelActuationState>& actuationStates,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleWheelParams>& wheelParams,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleSuspensionParams>& suspensionParams,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleTireForceParams>& tireForceParams,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleRoadGeometryState>& roadGeomStates,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleSuspensionState>& suspensionStates,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleSuspensionComplianceState>& suspensionComplianceStates,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleSuspensionForce>& suspensionForces,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleWheelRigidBody1dState>& wheelRigidBody1DStates,
        physx::vehicle2::PxVehicleArrayData<physx::vehicle2::PxVehicleTireGripState>& tireGripStates,
        physx::vehicle2::PxVehicleArrayData<physx::vehicle2::PxVehicleTireDirectionState>& tireDirectionStates,
        physx::vehicle2::PxVehicleArrayData<physx::vehicle2::PxVehicleTireSpeedState>& tireSpeedStates,
        physx::vehicle2::PxVehicleArrayData<physx::vehicle2::PxVehicleTireSlipState>& tireSlipStates,
        physx::vehicle2::PxVehicleArrayData<physx::vehicle2::PxVehicleTireCamberAngleState>& tireCamberAngleStates,
        physx::vehicle2::PxVehicleArrayData<physx::vehicle2::PxVehicleTireStickyState>& tireStickyStates,
        physx::vehicle2::PxVehicleArrayData<physx::vehicle2::PxVehicleTireForce>& tireForces) override;

    void getDataForPhysXConstraintComponent(
        const physx::vehicle2::PxVehicleAxleDescription*& axleDescription,
        const physx::vehicle2::PxVehicleRigidBodyState*& rigidBodyState,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleSuspensionParams>& suspensionParams,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehiclePhysXSuspensionLimitConstraintParams>& suspensionLimitParams,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleSuspensionState>& suspensionStates,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleSuspensionComplianceState>& suspensionComplianceStates,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleRoadGeometryState>& wheelRoadGeomStates,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleTireDirectionState>& tireDirectionStates,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleTireStickyState>& tireStickyStates,
        physx::vehicle2::PxVehiclePhysXConstraints*& constraints) override;

    void getDataForEngineDriveCommandResponseComponent(
        const physx::vehicle2::PxVehicleAxleDescription*& axleDescription,
        physx::vehicle2::PxVehicleSizedArrayData<const physx::vehicle2::PxVehicleBrakeCommandResponseParams>& brakeResponseParams,
        const physx::vehicle2::PxVehicleSteerCommandResponseParams*& steerResponseParams,
        physx::vehicle2::PxVehicleSizedArrayData<const physx::vehicle2::PxVehicleAckermannParams>& ackermannParams,
        const physx::vehicle2::PxVehicleGearboxParams*& gearboxParams,
        const physx::vehicle2::PxVehicleClutchCommandResponseParams*& clutchResponseParams,
        const physx::vehicle2::PxVehicleEngineParams*& engineParams,
        const physx::vehicle2::PxVehicleRigidBodyState*& rigidBodyState,
        const physx::vehicle2::PxVehicleEngineState*& engineState,
        const physx::vehicle2::PxVehicleAutoboxParams*& autoboxParams,
        const physx::vehicle2::PxVehicleCommandState*& commands,
        const physx::vehicle2::PxVehicleEngineDriveTransmissionCommandState*& transmissionCommands,
        physx::vehicle2::PxVehicleArrayData<physx::PxReal>& brakeResponseStates,
        physx::vehicle2::PxVehicleEngineDriveThrottleCommandResponseState*& throttleResponseState,
        physx::vehicle2::PxVehicleArrayData<physx::PxReal>& steerResponseStates,
        physx::vehicle2::PxVehicleGearboxState*& gearboxResponseState,
        physx::vehicle2::PxVehicleClutchCommandResponseState*& clutchResponseState,
        physx::vehicle2::PxVehicleAutoboxState*& autoboxState) override;

    void getDataForMultiWheelDriveDifferentialStateComponent(
        const physx::vehicle2::PxVehicleAxleDescription*& axleDescription,
        const physx::vehicle2::PxVehicleMultiWheelDriveDifferentialParams*& differentialParams,
        physx::vehicle2::PxVehicleDifferentialState*& differentialState) override;

    void getDataForEngineDriveActuationStateComponent(
        const physx::vehicle2::PxVehicleAxleDescription*& axleDescription,
        const physx::vehicle2::PxVehicleGearboxParams*& gearboxParams,
        physx::vehicle2::PxVehicleArrayData<const physx::PxReal>& brakeResponseStates,
        const physx::vehicle2::PxVehicleEngineDriveThrottleCommandResponseState*& throttleResponseState,
        const physx::vehicle2::PxVehicleGearboxState*& gearboxState,
        const physx::vehicle2::PxVehicleDifferentialState*& differentialState,
        const physx::vehicle2::PxVehicleClutchCommandResponseState*& clutchResponseState,
        physx::vehicle2::PxVehicleArrayData<physx::vehicle2::PxVehicleWheelActuationState>& actuationStates) override;

    void getDataForEngineDrivetrainComponent(
        const physx::vehicle2::PxVehicleAxleDescription*& axleDescription,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleWheelParams>& wheelParams,
        const physx::vehicle2::PxVehicleEngineParams*& engineParams,
        const physx::vehicle2::PxVehicleClutchParams*& clutchParams,
        const physx::vehicle2::PxVehicleGearboxParams*& gearboxParams,
        physx::vehicle2::PxVehicleArrayData<const physx::PxReal>& brakeResponseStates,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleWheelActuationState>& actuationStates,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleTireForce>& tireForces,
        const physx::vehicle2::PxVehicleEngineDriveThrottleCommandResponseState*& throttleResponseState,
        const physx::vehicle2::PxVehicleClutchCommandResponseState*& clutchResponseState,
        const physx::vehicle2::PxVehicleDifferentialState*& differentialState,
        const physx::vehicle2::PxVehicleWheelConstraintGroupState*& constraintGroupState,
        physx::vehicle2::PxVehicleArrayData<physx::vehicle2::PxVehicleWheelRigidBody1dState>& wheelRigidBody1dStates,
        physx::vehicle2::PxVehicleEngineState*& engineState,
        physx::vehicle2::PxVehicleGearboxState*& gearboxState,
        physx::vehicle2::PxVehicleClutchSlipState*& clutchState) override;

    void getDataForWheelComponent(
        const physx::vehicle2::PxVehicleAxleDescription*& axleDescription,
        physx::vehicle2::PxVehicleArrayData<const physx::PxReal>& steerResponseStates,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleWheelParams>& wheelParams,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleSuspensionParams>& suspensionParams,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleWheelActuationState>& actuationStates,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleSuspensionState>& suspensionStates,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleSuspensionComplianceState>& suspensionComplianceStates,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleTireSpeedState>& tireSpeedStates,
        physx::vehicle2::PxVehicleArrayData<physx::vehicle2::PxVehicleWheelRigidBody1dState>& wheelRigidBody1dStates,
        physx::vehicle2::PxVehicleArrayData<physx::vehicle2::PxVehicleWheelLocalPose>& wheelLocalPoses) override;

    void getDataForRigidBodyComponent(
        const physx::vehicle2::PxVehicleAxleDescription*& axleDescription,
        const physx::vehicle2::PxVehicleRigidBodyParams*& rigidBodyParams,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleSuspensionForce>& suspensionForces,
        physx::vehicle2::PxVehicleArrayData<const physx::vehicle2::PxVehicleTireForce>& tireForces,
        const physx::vehicle2::PxVehicleAntiRollTorque*& antiRollTorque,
        physx::vehicle2::PxVehicleRigidBodyState*& rigidBodyState) override;

private:
    uint64_t id_ = OPENUSD_PHYSX_INVALID_ID;
    bool publish_wheels_ = false;
    bool autobox_enabled_ = false;

    std::vector<uint64_t> wheel_ids_;

    physx::vehicle2::PxVehicleAxleDescription axle_description_{};
    physx::vehicle2::PxVehicleRigidBodyParams rigid_body_params_{};
    physx::vehicle2::PxVehicleSuspensionStateCalculationParams suspension_calculation_{};
    physx::vehicle2::PxVehicleBrakeCommandResponseParams brake_response_params_[2]{};
    physx::vehicle2::PxVehicleSteerCommandResponseParams steer_response_params_{};

    std::vector<physx::vehicle2::PxVehicleWheelParams> wheel_params_;
    std::vector<physx::vehicle2::PxVehicleSuspensionParams> suspension_params_;
    std::vector<physx::vehicle2::PxVehicleSuspensionComplianceParams> suspension_compliance_params_;
    std::vector<physx::vehicle2::PxVehicleSuspensionForceParams> suspension_force_params_;
    std::vector<physx::vehicle2::PxVehicleTireForceParams> tire_force_params_;
    std::vector<physx::vehicle2::PxVehiclePhysXSuspensionLimitConstraintParams> suspension_limit_params_;
    std::vector<physx::vehicle2::PxVehiclePhysXMaterialFrictionParams> material_friction_params_;
    std::vector<physx::vehicle2::PxVehiclePhysXMaterialFriction> material_frictions_;

    physx::vehicle2::PxVehicleEngineParams engine_params_{};
    physx::vehicle2::PxVehicleClutchParams clutch_params_{};
    physx::vehicle2::PxVehicleClutchCommandResponseParams clutch_response_params_{};
    physx::vehicle2::PxVehicleGearboxParams gearbox_params_{};
    physx::vehicle2::PxVehicleAutoboxParams autobox_params_{};
    physx::vehicle2::PxVehicleMultiWheelDriveDifferentialParams differential_params_{};
    physx::vehicle2::PxVehiclePhysXRoadGeometryQueryParams road_geometry_params_;
    ChassisFilter chassis_filter_;

    physx::vehicle2::PxVehicleCommandState commands_{};
    physx::vehicle2::PxVehicleEngineDriveTransmissionCommandState transmission_commands_{};

    physx::vehicle2::PxVehicleRigidBodyState rigid_body_state_{};
    physx::vehicle2::PxVehicleEngineState engine_state_{};
    physx::vehicle2::PxVehicleGearboxState gearbox_state_{};
    physx::vehicle2::PxVehicleAutoboxState autobox_state_{};
    physx::vehicle2::PxVehicleClutchCommandResponseState clutch_response_state_{};
    physx::vehicle2::PxVehicleEngineDriveThrottleCommandResponseState throttle_response_state_{};
    physx::vehicle2::PxVehicleDifferentialState differential_state_{};
    physx::vehicle2::PxVehicleClutchSlipState clutch_slip_state_{};
    physx::vehicle2::PxVehicleWheelConstraintGroupState constraint_group_state_{};
    physx::vehicle2::PxVehicleAntiRollTorque anti_roll_torque_{};

    std::vector<physx::PxReal> brake_response_states_;
    std::vector<physx::PxReal> steer_response_states_;
    std::vector<physx::vehicle2::PxVehicleWheelActuationState> actuation_states_;
    std::vector<physx::vehicle2::PxVehicleWheelRigidBody1dState> wheel_rigid_body_states_;
    std::vector<physx::vehicle2::PxVehicleWheelLocalPose> wheel_local_poses_;
    std::vector<physx::vehicle2::PxVehicleRoadGeometryState> road_geometry_states_;
    std::vector<physx::vehicle2::PxVehiclePhysXRoadGeometryQueryState> physx_road_geometry_states_;
    std::vector<physx::vehicle2::PxVehicleSuspensionState> suspension_states_;
    std::vector<physx::vehicle2::PxVehicleSuspensionComplianceState> suspension_compliance_states_;
    std::vector<physx::vehicle2::PxVehicleSuspensionForce> suspension_forces_;
    std::vector<physx::vehicle2::PxVehicleTireGripState> tire_grip_states_;
    std::vector<physx::vehicle2::PxVehicleTireDirectionState> tire_direction_states_;
    std::vector<physx::vehicle2::PxVehicleTireSpeedState> tire_speed_states_;
    std::vector<physx::vehicle2::PxVehicleTireSlipState> tire_slip_states_;
    std::vector<physx::vehicle2::PxVehicleTireCamberAngleState> tire_camber_states_;
    std::vector<physx::vehicle2::PxVehicleTireStickyState> tire_sticky_states_;
    std::vector<physx::vehicle2::PxVehicleTireForce> tire_forces_;

    physx::vehicle2::PxVehiclePhysXActor physx_actor_{};
    physx::vehicle2::PxVehiclePhysXSteerState physx_steer_state_{};
    physx::vehicle2::PxVehiclePhysXConstraints physx_constraints_{};
    bool constraints_created_ = false;

    physx::vehicle2::PxVehicleComponentSequence sequence_{};
    physx::vehicle2::PxVehiclePhysXSimulationContext context_{};
};
}

#endif
