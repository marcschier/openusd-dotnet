// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_physx_vehicle.h"

#include "openusd_physx_translate.h"

#include <algorithm>
#include <cmath>

namespace openusd_physx_vehicle
{
using namespace physx;
using namespace physx::vehicle2;

// Every wheel response table the runtime fills below is a fixed array sized by
// the simulation SDK, so the project wheel budget must never exceed it. The
// gear budget is asserted the same way for the fixed gear ratio arrays.
static_assert(
    OPENUSD_PHYSX_MAX_VEHICLE_WHEELS <= static_cast<uint32_t>(PxVehicleLimits::eMAX_NB_WHEELS),
    "The project vehicle wheel budget must fit the wheel budget of the simulation SDK.");
static_assert(
    OPENUSD_PHYSX_MAX_VEHICLE_GEARS <= static_cast<uint32_t>(PxVehicleGearboxParams::eMAX_NB_GEARS),
    "The project vehicle gear budget must fit the gear budget of the simulation SDK.");

namespace
{
bool g_extension_ready = false;

PxVehicleAxes::Enum PositiveAxis(uint32_t axis) noexcept
{
    switch (axis)
    {
    case OPENUSD_PHYSX_AXIS_X:
        return PxVehicleAxes::ePosX;
    case OPENUSD_PHYSX_AXIS_Y:
        return PxVehicleAxes::ePosY;
    default:
        return PxVehicleAxes::ePosZ;
    }
}

PxVehicleAxes::Enum NegativeAxis(uint32_t axis) noexcept
{
    switch (axis)
    {
    case OPENUSD_PHYSX_AXIS_X:
        return PxVehicleAxes::eNegX;
    case OPENUSD_PHYSX_AXIS_Y:
        return PxVehicleAxes::eNegY;
    default:
        return PxVehicleAxes::eNegZ;
    }
}

}

bool Initialize(PxFoundation& foundation, std::string& reason)
{
    if (g_extension_ready)
    {
        return true;
    }
    if (!PxInitVehicleExtension(foundation))
    {
        reason = "The simulation SDK refused to initialize its vehicle extension.";
        return false;
    }
    g_extension_ready = true;
    return true;
}

void Shutdown()
{
    if (!g_extension_ready)
    {
        return;
    }
    PxCloseVehicleExtension();
    g_extension_ready = false;
}

PxQueryHitType::Enum ChassisFilter::preFilter(
    const PxFilterData& filterData,
    const PxShape* shape,
    const PxRigidActor* actor,
    PxHitFlags& queryFlags)
{
    (void)filterData;
    (void)shape;
    (void)queryFlags;
    if (chassis_ != nullptr && actor == static_cast<const PxRigidActor*>(chassis_))
    {
        return PxQueryHitType::eNONE;
    }
    return PxQueryHitType::eBLOCK;
}

PxQueryHitType::Enum ChassisFilter::postFilter(
    const PxFilterData& filterData,
    const PxQueryHit& hit,
    const PxShape* shape,
    const PxRigidActor* actor)
{
    (void)filterData;
    (void)hit;
    (void)shape;
    (void)actor;
    return PxQueryHitType::eBLOCK;
}

PxConvexMesh* CreateSweepMesh(
    PxPhysics& physics,
    uint32_t longitudinal_axis,
    uint32_t lateral_axis,
    uint32_t vertical_axis)
{
    if (!g_extension_ready)
    {
        return nullptr;
    }
    PxVehicleFrame frame;
    frame.lngAxis = PositiveAxis(longitudinal_axis);
    frame.latAxis = PositiveAxis(lateral_axis);
    frame.vrtAxis = PositiveAxis(vertical_axis);
    const PxCookingParams cooking(physics.getTolerancesScale());
    return PxVehicleUnitCylinderSweepMeshCreate(frame, physics, cooking);
}

void DestroySweepMesh(PxConvexMesh* mesh)
{
    if (mesh != nullptr)
    {
        PxVehicleUnitCylinderSweepMeshDestroy(mesh);
    }
}

Instance::~Instance()
{
    Release();
}

void Instance::Release()
{
    if (constraints_created_)
    {
        PxVehicleConstraintsDestroy(physx_constraints_);
        constraints_created_ = false;
    }
    if (physx_actor_.rigidBody != nullptr)
    {
        physx_actor_.rigidBody->setActorFlag(PxActorFlag::eDISABLE_GRAVITY, false);
    }
    physx_actor_.rigidBody = nullptr;
}

bool Instance::Configure(
    const openusd_physx_vehicle_desc& desc,
    const openusd_physx_vehicle_wheel_desc* wheels,
    PxRigidBody& chassis,
    PxScene& scene,
    PxPhysics& physics,
    PxMaterial* material,
    PxConvexMesh* sweep_mesh,
    std::string& reason)
{
    if (!g_extension_ready)
    {
        reason = "The vehicle extension is not initialized.";
        return false;
    }
    const uint32_t wheel_count = desc.wheel_count;
    id_ = desc.id;
    publish_wheels_ = (desc.flags & OPENUSD_PHYSX_VEHICLE_FLAG_PUBLISH_WHEELS) != 0;
    autobox_enabled_ = (desc.flags & OPENUSD_PHYSX_VEHICLE_FLAG_AUTOBOX_ENABLED) != 0;

    // The page contract already bounds the wheel budget, but the runtime is
    // also reachable from a caller that hands a record straight to the world,
    // and every brake, steer, differential and axle table filled below is a
    // fixed array of exactly the simulation SDK wheel budget.
    if (wheel_count == 0U ||
        wheel_count > static_cast<uint32_t>(PxVehicleLimits::eMAX_NB_WHEELS))
    {
        reason = "The vehicle declares a wheel count outside the supported range.";
        return false;
    }
    // The axle tables are indexed by the axle a wheel declares, so an axle
    // index past the wheel budget would walk past them just as a wheel count
    // would. A vehicle can never have more axles than it has wheels.
    for (uint32_t index = 0; index < wheel_count; ++index)
    {
        if (wheels[index].axle_index >= wheel_count)
        {
            reason = "The vehicle declares a wheel on an axle outside the supported range.";
            return false;
        }
    }

    // The page contract already bounds the gear budget, but the runtime is also
    // reachable from a caller that hands a record straight to the world, and a
    // forward gear count near the top of the unsigned range would otherwise
    // walk past the fixed ratio arrays. The bound is written as a subtraction
    // so that adding reverse and neutral cannot wrap.
    if (desc.forward_gear_count == 0U ||
        desc.forward_gear_count > static_cast<uint32_t>(PxVehicleGearboxParams::eMAX_NB_GEARS) - 2U)
    {
        reason = "The vehicle declares a forward gear count outside the supported range.";
        return false;
    }

    // ------------------------------------------------------------------
    // Frame, scale and simulation context.
    // ------------------------------------------------------------------
    context_.setToDefault();
    context_.frame.lngAxis = PositiveAxis(desc.longitudinal_axis);
    context_.frame.latAxis = PositiveAxis(desc.lateral_axis);
    context_.frame.vrtAxis = PositiveAxis(desc.vertical_axis);
    context_.scale.scale = 1.0F;
    context_.gravity = scene.getGravity();
    context_.physxScene = &scene;
    context_.physxActorUpdateMode = PxVehiclePhysXActorUpdateMode::eAPPLY_VELOCITY;
    context_.physxUnitCylinderSweepMesh = sweep_mesh;

    // ------------------------------------------------------------------
    // Axles. Wheels declare the axle they sit on; the axle description is
    // rebuilt from that so wheel identity order stays exactly the page order.
    // ------------------------------------------------------------------
    axle_description_.setToDefault();
    wheel_ids_.assign(wheel_count, OPENUSD_PHYSX_INVALID_ID);
    uint32_t axle_count = 0;
    for (uint32_t index = 0; index < wheel_count; ++index)
    {
        axle_count = std::max(axle_count, wheels[index].axle_index + 1U);
    }
    std::vector<PxU32> axle_wheels;
    axle_wheels.reserve(wheel_count);
    for (uint32_t axle = 0; axle < axle_count; ++axle)
    {
        axle_wheels.clear();
        for (uint32_t index = 0; index < wheel_count; ++index)
        {
            if (wheels[index].axle_index == axle)
            {
                axle_wheels.push_back(static_cast<PxU32>(index));
            }
        }
        if (axle_wheels.empty())
        {
            continue;
        }
        axle_description_.addAxle(static_cast<PxU32>(axle_wheels.size()), axle_wheels.data());
    }
    if (axle_description_.nbWheels != wheel_count)
    {
        reason = "The vehicle wheels do not tile their axles.";
        return false;
    }

    // ------------------------------------------------------------------
    // Chassis mass frame.
    // ------------------------------------------------------------------
    rigid_body_params_.mass = desc.chassis_mass > 0.0F ? desc.chassis_mass : chassis.getMass();
    if (desc.chassis_moi.x > 0.0F && desc.chassis_moi.y > 0.0F && desc.chassis_moi.z > 0.0F)
    {
        rigid_body_params_.moi = openusd_physx_translate::ToPx(desc.chassis_moi);
    }
    else
    {
        rigid_body_params_.moi = chassis.getMassSpaceInertiaTensor();
    }
    if (!(rigid_body_params_.mass > 0.0F))
    {
        reason = "The vehicle chassis must have a positive mass.";
        return false;
    }
    if (desc.chassis_mass > 0.0F || (desc.chassis_moi.x > 0.0F && desc.chassis_moi.y > 0.0F && desc.chassis_moi.z > 0.0F))
    {
        PxVehiclePhysXRigidActorParams actor_params(rigid_body_params_, nullptr);
        PxVehiclePhysXActorConfigure(actor_params, chassis.getCMassLocalPose(), chassis);
    }

    // The vehicle rigid body component integrates gravity itself and writes the
    // result back as a velocity, so the scene must not integrate gravity a
    // second time for the same step. PxVehiclePhysXActor documents this as a
    // requirement rather than a tuning choice.
    chassis.setActorFlag(PxActorFlag::eDISABLE_GRAVITY, true);

    suspension_calculation_.suspensionJounceCalculationType =
        desc.query == OPENUSD_PHYSX_VEHICLE_QUERY_SWEEP && sweep_mesh != nullptr
        ? PxVehicleSuspensionJounceCalculationType::eSWEEP
        : PxVehicleSuspensionJounceCalculationType::eRAYCAST;
    suspension_calculation_.limitSuspensionExpansionVelocity =
        (desc.flags & OPENUSD_PHYSX_VEHICLE_FLAG_LIMIT_SUSPENSION_EXPANSION) != 0;

    // ------------------------------------------------------------------
    // Per wheel parameters.
    // ------------------------------------------------------------------
    wheel_params_.assign(wheel_count, PxVehicleWheelParams{});
    suspension_params_.assign(wheel_count, PxVehicleSuspensionParams{});
    suspension_compliance_params_.assign(wheel_count, PxVehicleSuspensionComplianceParams{});
    suspension_force_params_.assign(wheel_count, PxVehicleSuspensionForceParams{});
    tire_force_params_.assign(wheel_count, PxVehicleTireForceParams{});
    suspension_limit_params_.assign(wheel_count, PxVehiclePhysXSuspensionLimitConstraintParams{});
    material_friction_params_.assign(wheel_count, PxVehiclePhysXMaterialFrictionParams{});
    material_frictions_.assign(wheel_count, PxVehiclePhysXMaterialFriction{});

    // Sprung masses are resolved once for every wheel that leaves the value
    // unauthored, from the attachment points the page already carries.
    std::vector<PxVec3> attachment_points(wheel_count, PxVec3(0.0F));
    std::vector<PxReal> resolved_sprung_masses(wheel_count, 0.0F);
    for (uint32_t index = 0; index < wheel_count; ++index)
    {
        attachment_points[index] = openusd_physx_translate::ToPx(wheels[index].suspension_attachment.position);
    }
    const float sprung_total = desc.sprung_mass_total > 0.0F ? desc.sprung_mass_total : rigid_body_params_.mass;
    const bool sprung_resolved = PxVehicleComputeSprungMasses(
        static_cast<PxU32>(wheel_count),
        attachment_points.data(),
        sprung_total,
        NegativeAxis(desc.vertical_axis),
        resolved_sprung_masses.data());

    for (uint32_t index = 0; index < wheel_count; ++index)
    {
        const openusd_physx_vehicle_wheel_desc& wheel = wheels[index];
        wheel_ids_[index] = wheel.id;

        PxVehicleWheelParams& wheel_param = wheel_params_[index];
        wheel_param.radius = wheel.radius;
        wheel_param.halfWidth = wheel.half_width;
        wheel_param.mass = wheel.mass;
        // A solid disc is the only inertia a wheel record without an authored
        // moment of inertia can mean.
        wheel_param.moi = wheel.moi > 0.0F ? wheel.moi : 0.5F * wheel.mass * wheel.radius * wheel.radius;
        wheel_param.dampingRate = wheel.damping_rate;

        PxVehicleSuspensionParams& suspension = suspension_params_[index];
        suspension.suspensionAttachment = openusd_physx_translate::ToPx(wheel.suspension_attachment);
        suspension.suspensionTravelDir = openusd_physx_translate::ToPx(wheel.suspension_travel_dir).getNormalized();
        suspension.suspensionTravelDist = wheel.suspension_travel_dist;
        suspension.wheelAttachment = openusd_physx_translate::ToPx(wheel.wheel_attachment);

        PxVehicleSuspensionForceParams& force = suspension_force_params_[index];
        force.stiffness = wheel.suspension_stiffness;
        force.damping = wheel.suspension_damping;
        if (wheel.sprung_mass > 0.0F)
        {
            force.sprungMass = wheel.sprung_mass;
        }
        else if (sprung_resolved && resolved_sprung_masses[index] > 0.0F)
        {
            force.sprungMass = resolved_sprung_masses[index];
        }
        else
        {
            force.sprungMass = sprung_total / static_cast<float>(wheel_count);
        }

        // Compliance is authored as flat lookup tables: one entry means the
        // value never varies with jounce, which is what an unauthored record
        // means.
        PxVehicleSuspensionComplianceParams& compliance = suspension_compliance_params_[index];
        compliance.wheelToeAngle.clear();
        compliance.wheelCamberAngle.clear();
        compliance.suspForceAppPoint.clear();
        compliance.tireForceAppPoint.clear();
        compliance.wheelToeAngle.addPair(0.0F, 0.0F);
        compliance.wheelCamberAngle.addPair(0.0F, 0.0F);
        compliance.suspForceAppPoint.addPair(0.0F, PxVec3(0.0F));
        compliance.tireForceAppPoint.addPair(0.0F, PxVec3(0.0F));

        PxVehicleTireForceParams& tire = tire_force_params_[index];
        tire.latStiffX = wheel.tire_lat_stiff_x > 0.0F ? wheel.tire_lat_stiff_x : 2.0F;
        tire.latStiffY = wheel.tire_lat_stiff_y > 0.0F
            ? wheel.tire_lat_stiff_y
            : 17.0F * force.sprungMass * 9.81F;
        tire.longStiff = wheel.tire_long_stiff > 0.0F ? wheel.tire_long_stiff : 5000.0F;
        tire.camberStiff = wheel.tire_camber_stiff;
        tire.restLoad = wheel.tire_rest_load > 0.0F ? wheel.tire_rest_load : force.sprungMass * 9.81F;
        tire.frictionVsSlip[0][0] = 0.0F;
        tire.frictionVsSlip[0][1] = 1.0F;
        tire.frictionVsSlip[1][0] = 0.1F;
        tire.frictionVsSlip[1][1] = 1.0F;
        tire.frictionVsSlip[2][0] = 1.0F;
        tire.frictionVsSlip[2][1] = 1.0F;
        tire.loadFilter[0][0] = 0.0F;
        tire.loadFilter[0][1] = 0.23F;
        tire.loadFilter[1][0] = 3.0F;
        tire.loadFilter[1][1] = 3.0F;

        PxVehiclePhysXSuspensionLimitConstraintParams& limit = suspension_limit_params_[index];
        limit.restitution = 0.0F;
        limit.directionForSuspensionLimitConstraint =
            PxVehiclePhysXSuspensionLimitConstraintParams::DirectionSpecifier::eSUSPENSION;

        const float friction = wheel.tire_friction > 0.0F
            ? wheel.tire_friction
            : (desc.default_friction > 0.0F ? desc.default_friction : 1.0F);
        material_frictions_[index].material = material;
        material_frictions_[index].friction = friction;
        material_friction_params_[index].materialFrictions = &material_frictions_[index];
        material_friction_params_[index].nbMaterialFrictions = material != nullptr ? 1U : 0U;
        material_friction_params_[index].defaultFriction = friction;
    }

    // ------------------------------------------------------------------
    // Command response. Brake index zero is the foot brake, index one the
    // handbrake; both are linear, so the nonlinear table stays empty.
    // ------------------------------------------------------------------
    for (uint32_t slot = 0; slot < 2; ++slot)
    {
        brake_response_params_[slot].nonlinearResponse.clear();
        brake_response_params_[slot].maxResponse =
            slot == 0 ? desc.max_brake_torque : desc.max_hand_brake_torque;
        for (uint32_t index = 0; index < PxVehicleLimits::eMAX_NB_WHEELS; ++index)
        {
            brake_response_params_[slot].wheelResponseMultipliers[index] = 0.0F;
        }
    }
    steer_response_params_.nonlinearResponse.clear();
    steer_response_params_.maxResponse = desc.max_steer_angle;
    for (uint32_t index = 0; index < PxVehicleLimits::eMAX_NB_WHEELS; ++index)
    {
        steer_response_params_.wheelResponseMultipliers[index] = 0.0F;
    }
    for (uint32_t index = 0; index < wheel_count; ++index)
    {
        const openusd_physx_vehicle_wheel_desc& wheel = wheels[index];
        brake_response_params_[0].wheelResponseMultipliers[index] =
            (wheel.flags & OPENUSD_PHYSX_VEHICLE_WHEEL_FLAG_BRAKES) != 0
            ? (wheel.brake_response > 0.0F ? wheel.brake_response : 1.0F)
            : 0.0F;
        brake_response_params_[1].wheelResponseMultipliers[index] =
            (wheel.flags & OPENUSD_PHYSX_VEHICLE_WHEEL_FLAG_HAND_BRAKES) != 0
            ? (wheel.hand_brake_response > 0.0F ? wheel.hand_brake_response : 1.0F)
            : 0.0F;
        steer_response_params_.wheelResponseMultipliers[index] =
            (wheel.flags & OPENUSD_PHYSX_VEHICLE_WHEEL_FLAG_STEERS) != 0
            ? (wheel.steer_response > 0.0F ? wheel.steer_response : 1.0F)
            : 0.0F;
    }

    // ------------------------------------------------------------------
    // Engine, clutch, gearbox, autobox and differential.
    // ------------------------------------------------------------------
    engine_params_.torqueCurve.clear();
    engine_params_.torqueCurve.addPair(0.0F, 0.8F);
    engine_params_.torqueCurve.addPair(0.33F, 1.0F);
    engine_params_.torqueCurve.addPair(1.0F, 0.8F);
    engine_params_.moi = desc.engine_moi;
    engine_params_.peakTorque = desc.engine_peak_torque;
    engine_params_.idleOmega = desc.engine_idle_omega;
    engine_params_.maxOmega = desc.engine_max_omega;
    engine_params_.dampingRateFullThrottle = desc.engine_damping_full_throttle;
    engine_params_.dampingRateZeroThrottleClutchEngaged =
        desc.engine_damping_zero_throttle_clutch_engaged;
    engine_params_.dampingRateZeroThrottleClutchDisengaged =
        desc.engine_damping_zero_throttle_clutch_disengaged;

    clutch_params_.accuracyMode = physx::vehicle2::PxVehicleClutchAccuracyMode::eESTIMATE;
    clutch_params_.estimateIterations = 5;
    clutch_response_params_.maxResponse = desc.clutch_strength;

    // Gear zero is reverse, gear one is neutral, and the forward gears follow
    // in a strictly decreasing ratio series.
    gearbox_params_.neutralGear = 1;
    gearbox_params_.nbRatios = desc.forward_gear_count + 2U;
    gearbox_params_.finalRatio = desc.final_gear_ratio;
    gearbox_params_.switchTime = desc.gear_switch_time;
    gearbox_params_.ratios[0] = -desc.reverse_gear_ratio;
    gearbox_params_.ratios[1] = 0.0F;
    if (desc.forward_gear_count == 1U)
    {
        gearbox_params_.ratios[2] = desc.first_gear_ratio;
    }
    else
    {
        const float span = desc.first_gear_ratio - desc.top_gear_ratio;
        const float step = span / static_cast<float>(desc.forward_gear_count - 1U);
        for (uint32_t gear = 0; gear < desc.forward_gear_count; ++gear)
        {
            gearbox_params_.ratios[2 + gear] = desc.first_gear_ratio - (step * static_cast<float>(gear));
        }
    }
    for (uint32_t gear = gearbox_params_.nbRatios; gear < PxVehicleGearboxParams::eMAX_NB_GEARS; ++gear)
    {
        gearbox_params_.ratios[gear] = 0.0F;
    }

    for (uint32_t gear = 0; gear < PxVehicleGearboxParams::eMAX_NB_GEARS; ++gear)
    {
        autobox_params_.upRatios[gear] = desc.autobox_up_ratio > 0.0F ? desc.autobox_up_ratio : 0.65F;
        autobox_params_.downRatios[gear] = desc.autobox_down_ratio > 0.0F ? desc.autobox_down_ratio : 0.15F;
    }
    // Neutral and the top gear can never shift further in their direction.
    autobox_params_.upRatios[gearbox_params_.neutralGear] = 0.0F;
    autobox_params_.downRatios[gearbox_params_.neutralGear] = 0.0F;
    if (gearbox_params_.nbRatios > 0U)
    {
        autobox_params_.upRatios[gearbox_params_.nbRatios - 1U] = 0.0F;
    }
    autobox_params_.downRatios[0] = 0.0F;
    autobox_params_.latency = desc.autobox_latency;

    float torque_share = 0.0F;
    uint32_t driven_wheels = 0;
    for (uint32_t index = 0; index < wheel_count; ++index)
    {
        if ((wheels[index].flags & OPENUSD_PHYSX_VEHICLE_WHEEL_FLAG_DRIVEN) == 0)
        {
            continue;
        }
        ++driven_wheels;
        torque_share += wheels[index].drive_torque_ratio;
    }
    for (uint32_t index = 0; index < PxVehicleLimits::eMAX_NB_WHEELS; ++index)
    {
        differential_params_.torqueRatios[index] = 0.0F;
        differential_params_.aveWheelSpeedRatios[index] = 0.0F;
    }
    for (uint32_t index = 0; index < wheel_count; ++index)
    {
        if ((wheels[index].flags & OPENUSD_PHYSX_VEHICLE_WHEEL_FLAG_DRIVEN) == 0)
        {
            continue;
        }
        const float share = torque_share > 0.0F
            ? wheels[index].drive_torque_ratio / torque_share
            : 1.0F / static_cast<float>(driven_wheels);
        differential_params_.torqueRatios[index] = share;
        differential_params_.aveWheelSpeedRatios[index] = 1.0F / static_cast<float>(driven_wheels);
    }

    // ------------------------------------------------------------------
    // Road geometry queries.
    // ------------------------------------------------------------------
    road_geometry_params_.roadGeometryQueryType =
        desc.query == OPENUSD_PHYSX_VEHICLE_QUERY_SWEEP && sweep_mesh != nullptr
        ? PxVehiclePhysXRoadGeometryQueryType::eSWEEP
        : PxVehiclePhysXRoadGeometryQueryType::eRAYCAST;
    road_geometry_params_.defaultFilterData = PxQueryFilterData();
    road_geometry_params_.defaultFilterData.flags |= PxQueryFlag::ePREFILTER;
    road_geometry_params_.filterDataEntries = nullptr;
    chassis_filter_.SetChassis(&chassis);
    road_geometry_params_.filterCallback = &chassis_filter_;

    // ------------------------------------------------------------------
    // States. Every state block is sized here and never resized again, which
    // is what keeps a warm step allocation free.
    // ------------------------------------------------------------------
    brake_response_states_.assign(wheel_count, 0.0F);
    steer_response_states_.assign(wheel_count, 0.0F);
    actuation_states_.assign(wheel_count, PxVehicleWheelActuationState{});
    wheel_rigid_body_states_.assign(wheel_count, PxVehicleWheelRigidBody1dState{});
    wheel_local_poses_.assign(wheel_count, PxVehicleWheelLocalPose{});
    road_geometry_states_.assign(wheel_count, PxVehicleRoadGeometryState{});
    physx_road_geometry_states_.assign(wheel_count, PxVehiclePhysXRoadGeometryQueryState{});
    suspension_states_.assign(wheel_count, PxVehicleSuspensionState{});
    suspension_compliance_states_.assign(wheel_count, PxVehicleSuspensionComplianceState{});
    suspension_forces_.assign(wheel_count, PxVehicleSuspensionForce{});
    tire_grip_states_.assign(wheel_count, PxVehicleTireGripState{});
    tire_direction_states_.assign(wheel_count, PxVehicleTireDirectionState{});
    tire_speed_states_.assign(wheel_count, PxVehicleTireSpeedState{});
    tire_slip_states_.assign(wheel_count, PxVehicleTireSlipState{});
    tire_camber_states_.assign(wheel_count, PxVehicleTireCamberAngleState{});
    tire_sticky_states_.assign(wheel_count, PxVehicleTireStickyState{});
    tire_forces_.assign(wheel_count, PxVehicleTireForce{});

    physx_actor_.setToDefault();
    physx_actor_.rigidBody = &chassis;
    physx_constraints_.setToDefault();
    PxVehicleConstraintsCreate(axle_description_, physics, chassis, physx_constraints_);
    constraints_created_ = true;

    Reset();

    // ------------------------------------------------------------------
    // Component order. Everything that resolves forces from the current pose
    // runs once, and the drivetrain plus integration loop substeps inside the
    // group so a stiff suspension stays stable at the page step rate.
    // ------------------------------------------------------------------
    sequence_ = PxVehicleComponentSequence();
    bool added = true;
    added = sequence_.add(static_cast<PxVehiclePhysXActorBeginComponent*>(this)) && added;
    added = sequence_.add(static_cast<PxVehiclePhysXRoadGeometrySceneQueryComponent*>(this)) && added;
    added = sequence_.add(static_cast<PxVehicleSuspensionComponent*>(this)) && added;
    added = sequence_.add(static_cast<PxVehicleTireComponent*>(this)) && added;
    added = sequence_.add(static_cast<PxVehicleEngineDriveCommandResponseComponent*>(this)) && added;
    added = sequence_.add(static_cast<PxVehicleMultiWheelDriveDifferentialStateComponent*>(this)) && added;
    added = sequence_.add(static_cast<PxVehicleEngineDriveActuationStateComponent*>(this)) && added;
    const PxU8 substep_group = sequence_.beginSubstepGroup(3);
    added = sequence_.add(static_cast<PxVehiclePhysXConstraintComponent*>(this)) && added;
    added = sequence_.add(static_cast<PxVehicleEngineDrivetrainComponent*>(this)) && added;
    added = sequence_.add(static_cast<PxVehicleWheelComponent*>(this)) && added;
    added = sequence_.add(static_cast<PxVehicleRigidBodyComponent*>(this)) && added;
    sequence_.endSubstepGroup();
    if (!added || substep_group == PxVehicleComponentSequence::eINVALID_SUBSTEP_GROUP)
    {
        reason = "The simulation SDK refused the vehicle component sequence.";
        return false;
    }
    return true;
}

void Instance::Reset()
{
    commands_.setToDefault();
    transmission_commands_.setToDefault();
    rigid_body_state_.setToDefault();
    if (physx_actor_.rigidBody != nullptr)
    {
        rigid_body_state_.pose = physx_actor_.rigidBody->getGlobalPose();
    }
    engine_state_.setToDefault();
    gearbox_state_.setToDefault();
    // A vehicle with an autobox starts in neutral because the autobox pulls it
    // out of neutral as soon as the engine turns. A vehicle without one has
    // nothing that could shift for the driver, so it starts in its first
    // forward gear and stays there until a gear command moves it.
    const uint32_t start_gear = autobox_enabled_ || gearbox_params_.nbRatios <= gearbox_params_.neutralGear + 1U
        ? static_cast<uint32_t>(gearbox_params_.neutralGear)
        : static_cast<uint32_t>(gearbox_params_.neutralGear) + 1U;
    gearbox_state_.currentGear = start_gear;
    gearbox_state_.targetGear = start_gear;
    autobox_state_.setToDefault();
    clutch_response_state_.setToDefault();
    throttle_response_state_.setToDefault();
    differential_state_.setToDefault();
    clutch_slip_state_.setToDefault();
    constraint_group_state_.setToDefault();
    anti_roll_torque_.setToDefault();
    physx_steer_state_.setToDefault();
    for (size_t index = 0; index < brake_response_states_.size(); ++index)
    {
        brake_response_states_[index] = 0.0F;
        steer_response_states_[index] = 0.0F;
        actuation_states_[index].setToDefault();
        wheel_rigid_body_states_[index].setToDefault();
        wheel_local_poses_[index].setToDefault();
        road_geometry_states_[index].setToDefault();
        physx_road_geometry_states_[index].setToDefault();
        suspension_states_[index].setToDefault();
        suspension_compliance_states_[index].setToDefault();
        suspension_forces_[index].setToDefault();
        tire_grip_states_[index].setToDefault();
        tire_direction_states_[index].setToDefault();
        tire_speed_states_[index].setToDefault();
        tire_slip_states_[index].setToDefault();
        tire_camber_states_[index].setToDefault();
        tire_sticky_states_[index].setToDefault();
        tire_forces_[index].setToDefault();
    }
    physx_constraints_.setToDefault();
}

void Instance::SetCommands(
    float throttle,
    float brake,
    float hand_brake,
    float steer,
    float clutch,
    uint32_t gear) noexcept
{
    commands_.throttle = PxClamp(throttle, 0.0F, 1.0F);
    commands_.nbBrakes = 2;
    commands_.brakes[0] = PxClamp(brake, 0.0F, 1.0F);
    commands_.brakes[1] = PxClamp(hand_brake, 0.0F, 1.0F);
    commands_.steer = PxClamp(steer, -1.0F, 1.0F);
    transmission_commands_.clutch = PxClamp(clutch, 0.0F, 1.0F);
    if (gear == 0U)
    {
        // With an autobox the automatic gear hands the choice to the autobox.
        // Without one the simulation SDK reads the same value as "keep the gear
        // the gearbox is already targeting", which is the only honest meaning a
        // vehicle that cannot shift for itself can give the command.
        transmission_commands_.targetGear = PxVehicleEngineDriveTransmissionCommandState::eAUTOMATIC_GEAR;
        return;
    }
    // The gear command is one based so that zero can mean "ask the autobox".
    // Bounding it against the gearbox this vehicle actually declares keeps the
    // index inside the fixed gear ratio and autobox arrays no matter what the
    // caller submitted; a gear past the top gear simply selects the top gear.
    const uint32_t top_gear = gearbox_params_.nbRatios == 0
        ? 0U
        : static_cast<uint32_t>(gearbox_params_.nbRatios) - 1U;
    transmission_commands_.targetGear = PxMin(gear - 1U, top_gear);
}

void Instance::Step(float dt, const PxVec3& gravity)
{
    if (physx_actor_.rigidBody == nullptr || !(dt > 0.0F))
    {
        return;
    }
    context_.gravity = gravity;
    PxVehicleConstraintsDirtyStateUpdate(physx_constraints_);
    sequence_.update(dt, context_);
    PxVehicleWriteRigidBodyStateToPhysXActor(
        context_.physxActorUpdateMode, rigid_body_state_, dt, *physx_actor_.rigidBody);
}

PxTransform Instance::WheelPose(uint32_t index) const noexcept
{
    if (physx_actor_.rigidBody == nullptr)
    {
        return PxTransform(PxIdentity);
    }
    return physx_actor_.rigidBody->getGlobalPose() * wheel_local_poses_[index].localPose;
}

PxVec3 Instance::WheelVelocity(uint32_t index) const noexcept
{
    // A wheel is not a simulated body, so what it publishes is its rolling
    // speed expressed along the vehicle forward axis of the chassis frame.
    const float rolling = wheel_rigid_body_states_[index].rotationSpeed * wheel_params_[index].radius;
    PxVec3 forward(0.0F);
    switch (context_.frame.lngAxis)
    {
    case PxVehicleAxes::ePosX:
        forward = PxVec3(1.0F, 0.0F, 0.0F);
        break;
    case PxVehicleAxes::eNegX:
        forward = PxVec3(-1.0F, 0.0F, 0.0F);
        break;
    case PxVehicleAxes::ePosY:
        forward = PxVec3(0.0F, 1.0F, 0.0F);
        break;
    case PxVehicleAxes::eNegY:
        forward = PxVec3(0.0F, -1.0F, 0.0F);
        break;
    case PxVehicleAxes::eNegZ:
        forward = PxVec3(0.0F, 0.0F, -1.0F);
        break;
    default:
        forward = PxVec3(0.0F, 0.0F, 1.0F);
        break;
    }
    if (physx_actor_.rigidBody == nullptr)
    {
        return forward * rolling;
    }
    return physx_actor_.rigidBody->getGlobalPose().rotate(forward) * rolling;
}

void Instance::getDataForPhysXActorBeginComponent(
    const PxVehicleAxleDescription*& axleDescription,
    const PxVehicleCommandState*& commands,
    const PxVehicleEngineDriveTransmissionCommandState*& transmissionCommands,
    const PxVehicleGearboxParams*& gearParams,
    const PxVehicleGearboxState*& gearState,
    const PxVehicleEngineParams*& engineParams,
    PxVehiclePhysXActor*& physxActor,
    PxVehiclePhysXSteerState*& physxSteerState,
    PxVehiclePhysXConstraints*& physxConstraints,
    PxVehicleRigidBodyState*& rigidBodyState,
    PxVehicleArrayData<PxVehicleWheelRigidBody1dState>& wheelRigidBody1dStates,
    PxVehicleEngineState*& engineState)
{
    axleDescription = &axle_description_;
    commands = &commands_;
    transmissionCommands = &transmission_commands_;
    gearParams = &gearbox_params_;
    gearState = &gearbox_state_;
    engineParams = &engine_params_;
    physxActor = &physx_actor_;
    physxSteerState = &physx_steer_state_;
    physxConstraints = &physx_constraints_;
    rigidBodyState = &rigid_body_state_;
    wheelRigidBody1dStates.setData(wheel_rigid_body_states_.data());
    engineState = &engine_state_;
}

void Instance::getDataForPhysXRoadGeometrySceneQueryComponent(
    const PxVehicleAxleDescription*& axleDescription,
    const PxVehiclePhysXRoadGeometryQueryParams*& roadGeomParams,
    PxVehicleArrayData<const PxReal>& steerResponseStates,
    const PxVehicleRigidBodyState*& rigidBodyState,
    PxVehicleArrayData<const PxVehicleWheelParams>& wheelParams,
    PxVehicleArrayData<const PxVehicleSuspensionParams>& suspensionParams,
    PxVehicleArrayData<const PxVehiclePhysXMaterialFrictionParams>& materialFrictionParams,
    PxVehicleArrayData<PxVehicleRoadGeometryState>& roadGeometryStates,
    PxVehicleArrayData<PxVehiclePhysXRoadGeometryQueryState>& physxRoadGeometryStates)
{
    axleDescription = &axle_description_;
    roadGeomParams = &road_geometry_params_;
    steerResponseStates.setData(steer_response_states_.data());
    rigidBodyState = &rigid_body_state_;
    wheelParams.setData(wheel_params_.data());
    suspensionParams.setData(suspension_params_.data());
    materialFrictionParams.setData(material_friction_params_.data());
    roadGeometryStates.setData(road_geometry_states_.data());
    physxRoadGeometryStates.setData(physx_road_geometry_states_.data());
}

void Instance::getDataForSuspensionComponent(
    const PxVehicleAxleDescription*& axleDescription,
    const PxVehicleRigidBodyParams*& rigidBodyParams,
    const PxVehicleSuspensionStateCalculationParams*& suspensionStateCalculationParams,
    PxVehicleArrayData<const PxReal>& steerResponseStates,
    const PxVehicleRigidBodyState*& rigidBodyState,
    PxVehicleArrayData<const PxVehicleWheelParams>& wheelParams,
    PxVehicleArrayData<const PxVehicleSuspensionParams>& suspensionParams,
    PxVehicleArrayData<const PxVehicleSuspensionComplianceParams>& suspensionComplianceParams,
    PxVehicleArrayData<const PxVehicleSuspensionForceParams>& suspensionForceParams,
    PxVehicleSizedArrayData<const PxVehicleAntiRollForceParams>& antiRollForceParams,
    PxVehicleArrayData<const PxVehicleRoadGeometryState>& wheelRoadGeomStates,
    PxVehicleArrayData<PxVehicleSuspensionState>& suspensionStates,
    PxVehicleArrayData<PxVehicleSuspensionComplianceState>& suspensionComplianceStates,
    PxVehicleArrayData<PxVehicleSuspensionForce>& suspensionForces,
    PxVehicleAntiRollTorque*& antiRollTorque)
{
    axleDescription = &axle_description_;
    rigidBodyParams = &rigid_body_params_;
    suspensionStateCalculationParams = &suspension_calculation_;
    steerResponseStates.setData(steer_response_states_.data());
    rigidBodyState = &rigid_body_state_;
    wheelParams.setData(wheel_params_.data());
    suspensionParams.setData(suspension_params_.data());
    suspensionComplianceParams.setData(suspension_compliance_params_.data());
    suspensionForceParams.setData(suspension_force_params_.data());
    antiRollForceParams.setEmpty();
    wheelRoadGeomStates.setData(road_geometry_states_.data());
    suspensionStates.setData(suspension_states_.data());
    suspensionComplianceStates.setData(suspension_compliance_states_.data());
    suspensionForces.setData(suspension_forces_.data());
    antiRollTorque = &anti_roll_torque_;
}

void Instance::getDataForTireComponent(
    const PxVehicleAxleDescription*& axleDescription,
    PxVehicleArrayData<const PxReal>& steerResponseStates,
    const PxVehicleRigidBodyState*& rigidBodyState,
    PxVehicleArrayData<const PxVehicleWheelActuationState>& actuationStates,
    PxVehicleArrayData<const PxVehicleWheelParams>& wheelParams,
    PxVehicleArrayData<const PxVehicleSuspensionParams>& suspensionParams,
    PxVehicleArrayData<const PxVehicleTireForceParams>& tireForceParams,
    PxVehicleArrayData<const PxVehicleRoadGeometryState>& roadGeomStates,
    PxVehicleArrayData<const PxVehicleSuspensionState>& suspensionStates,
    PxVehicleArrayData<const PxVehicleSuspensionComplianceState>& suspensionComplianceStates,
    PxVehicleArrayData<const PxVehicleSuspensionForce>& suspensionForces,
    PxVehicleArrayData<const PxVehicleWheelRigidBody1dState>& wheelRigidBody1DStates,
    PxVehicleArrayData<PxVehicleTireGripState>& tireGripStates,
    PxVehicleArrayData<PxVehicleTireDirectionState>& tireDirectionStates,
    PxVehicleArrayData<PxVehicleTireSpeedState>& tireSpeedStates,
    PxVehicleArrayData<PxVehicleTireSlipState>& tireSlipStates,
    PxVehicleArrayData<PxVehicleTireCamberAngleState>& tireCamberAngleStates,
    PxVehicleArrayData<PxVehicleTireStickyState>& tireStickyStates,
    PxVehicleArrayData<PxVehicleTireForce>& tireForces)
{
    axleDescription = &axle_description_;
    steerResponseStates.setData(steer_response_states_.data());
    rigidBodyState = &rigid_body_state_;
    actuationStates.setData(actuation_states_.data());
    wheelParams.setData(wheel_params_.data());
    suspensionParams.setData(suspension_params_.data());
    tireForceParams.setData(tire_force_params_.data());
    roadGeomStates.setData(road_geometry_states_.data());
    suspensionStates.setData(suspension_states_.data());
    suspensionComplianceStates.setData(suspension_compliance_states_.data());
    suspensionForces.setData(suspension_forces_.data());
    wheelRigidBody1DStates.setData(wheel_rigid_body_states_.data());
    tireGripStates.setData(tire_grip_states_.data());
    tireDirectionStates.setData(tire_direction_states_.data());
    tireSpeedStates.setData(tire_speed_states_.data());
    tireSlipStates.setData(tire_slip_states_.data());
    tireCamberAngleStates.setData(tire_camber_states_.data());
    tireStickyStates.setData(tire_sticky_states_.data());
    tireForces.setData(tire_forces_.data());
}

void Instance::getDataForPhysXConstraintComponent(
    const PxVehicleAxleDescription*& axleDescription,
    const PxVehicleRigidBodyState*& rigidBodyState,
    PxVehicleArrayData<const PxVehicleSuspensionParams>& suspensionParams,
    PxVehicleArrayData<const PxVehiclePhysXSuspensionLimitConstraintParams>& suspensionLimitParams,
    PxVehicleArrayData<const PxVehicleSuspensionState>& suspensionStates,
    PxVehicleArrayData<const PxVehicleSuspensionComplianceState>& suspensionComplianceStates,
    PxVehicleArrayData<const PxVehicleRoadGeometryState>& wheelRoadGeomStates,
    PxVehicleArrayData<const PxVehicleTireDirectionState>& tireDirectionStates,
    PxVehicleArrayData<const PxVehicleTireStickyState>& tireStickyStates,
    PxVehiclePhysXConstraints*& constraints)
{
    axleDescription = &axle_description_;
    rigidBodyState = &rigid_body_state_;
    suspensionParams.setData(suspension_params_.data());
    suspensionLimitParams.setData(suspension_limit_params_.data());
    suspensionStates.setData(suspension_states_.data());
    suspensionComplianceStates.setData(suspension_compliance_states_.data());
    wheelRoadGeomStates.setData(road_geometry_states_.data());
    tireDirectionStates.setData(tire_direction_states_.data());
    tireStickyStates.setData(tire_sticky_states_.data());
    constraints = &physx_constraints_;
}

void Instance::getDataForEngineDriveCommandResponseComponent(
    const PxVehicleAxleDescription*& axleDescription,
    PxVehicleSizedArrayData<const PxVehicleBrakeCommandResponseParams>& brakeResponseParams,
    const PxVehicleSteerCommandResponseParams*& steerResponseParams,
    PxVehicleSizedArrayData<const PxVehicleAckermannParams>& ackermannParams,
    const PxVehicleGearboxParams*& gearboxParams,
    const PxVehicleClutchCommandResponseParams*& clutchResponseParams,
    const PxVehicleEngineParams*& engineParams,
    const PxVehicleRigidBodyState*& rigidBodyState,
    const PxVehicleEngineState*& engineState,
    const PxVehicleAutoboxParams*& autoboxParams,
    const PxVehicleCommandState*& commands,
    const PxVehicleEngineDriveTransmissionCommandState*& transmissionCommands,
    PxVehicleArrayData<PxReal>& brakeResponseStates,
    PxVehicleEngineDriveThrottleCommandResponseState*& throttleResponseState,
    PxVehicleArrayData<PxReal>& steerResponseStates,
    PxVehicleGearboxState*& gearboxResponseState,
    PxVehicleClutchCommandResponseState*& clutchResponseState,
    PxVehicleAutoboxState*& autoboxState)
{
    axleDescription = &axle_description_;
    brakeResponseParams.setDataAndCount(brake_response_params_, 2);
    steerResponseParams = &steer_response_params_;
    ackermannParams.setEmpty();
    gearboxParams = &gearbox_params_;
    clutchResponseParams = &clutch_response_params_;
    engineParams = &engine_params_;
    rigidBodyState = &rigid_body_state_;
    engineState = &engine_state_;
    // The autobox is only offered to the drivetrain when the record asked for
    // one. Passing null is how the simulation SDK is told that nothing may
    // choose a gear on the driver's behalf, and it also makes the automatic
    // gear command fall back to the gear the gearbox already targets.
    autoboxParams = autobox_enabled_ ? &autobox_params_ : nullptr;
    commands = &commands_;
    transmissionCommands = &transmission_commands_;
    brakeResponseStates.setData(brake_response_states_.data());
    throttleResponseState = &throttle_response_state_;
    steerResponseStates.setData(steer_response_states_.data());
    gearboxResponseState = &gearbox_state_;
    clutchResponseState = &clutch_response_state_;
    autoboxState = &autobox_state_;
}

void Instance::getDataForMultiWheelDriveDifferentialStateComponent(
    const PxVehicleAxleDescription*& axleDescription,
    const PxVehicleMultiWheelDriveDifferentialParams*& differentialParams,
    PxVehicleDifferentialState*& differentialState)
{
    axleDescription = &axle_description_;
    differentialParams = &differential_params_;
    differentialState = &differential_state_;
}

void Instance::getDataForEngineDriveActuationStateComponent(
    const PxVehicleAxleDescription*& axleDescription,
    const PxVehicleGearboxParams*& gearboxParams,
    PxVehicleArrayData<const PxReal>& brakeResponseStates,
    const PxVehicleEngineDriveThrottleCommandResponseState*& throttleResponseState,
    const PxVehicleGearboxState*& gearboxState,
    const PxVehicleDifferentialState*& differentialState,
    const PxVehicleClutchCommandResponseState*& clutchResponseState,
    PxVehicleArrayData<PxVehicleWheelActuationState>& actuationStates)
{
    axleDescription = &axle_description_;
    gearboxParams = &gearbox_params_;
    brakeResponseStates.setData(brake_response_states_.data());
    throttleResponseState = &throttle_response_state_;
    gearboxState = &gearbox_state_;
    differentialState = &differential_state_;
    clutchResponseState = &clutch_response_state_;
    actuationStates.setData(actuation_states_.data());
}

void Instance::getDataForEngineDrivetrainComponent(
    const PxVehicleAxleDescription*& axleDescription,
    PxVehicleArrayData<const PxVehicleWheelParams>& wheelParams,
    const PxVehicleEngineParams*& engineParams,
    const PxVehicleClutchParams*& clutchParams,
    const PxVehicleGearboxParams*& gearboxParams,
    PxVehicleArrayData<const PxReal>& brakeResponseStates,
    PxVehicleArrayData<const PxVehicleWheelActuationState>& actuationStates,
    PxVehicleArrayData<const PxVehicleTireForce>& tireForces,
    const PxVehicleEngineDriveThrottleCommandResponseState*& throttleResponseState,
    const PxVehicleClutchCommandResponseState*& clutchResponseState,
    const PxVehicleDifferentialState*& differentialState,
    const PxVehicleWheelConstraintGroupState*& constraintGroupState,
    PxVehicleArrayData<PxVehicleWheelRigidBody1dState>& wheelRigidBody1dStates,
    PxVehicleEngineState*& engineState,
    PxVehicleGearboxState*& gearboxState,
    PxVehicleClutchSlipState*& clutchState)
{
    axleDescription = &axle_description_;
    wheelParams.setData(wheel_params_.data());
    engineParams = &engine_params_;
    clutchParams = &clutch_params_;
    gearboxParams = &gearbox_params_;
    brakeResponseStates.setData(brake_response_states_.data());
    actuationStates.setData(actuation_states_.data());
    tireForces.setData(tire_forces_.data());
    throttleResponseState = &throttle_response_state_;
    clutchResponseState = &clutch_response_state_;
    differentialState = &differential_state_;
    constraintGroupState = nullptr;
    wheelRigidBody1dStates.setData(wheel_rigid_body_states_.data());
    engineState = &engine_state_;
    gearboxState = &gearbox_state_;
    clutchState = &clutch_slip_state_;
}

void Instance::getDataForWheelComponent(
    const PxVehicleAxleDescription*& axleDescription,
    PxVehicleArrayData<const PxReal>& steerResponseStates,
    PxVehicleArrayData<const PxVehicleWheelParams>& wheelParams,
    PxVehicleArrayData<const PxVehicleSuspensionParams>& suspensionParams,
    PxVehicleArrayData<const PxVehicleWheelActuationState>& actuationStates,
    PxVehicleArrayData<const PxVehicleSuspensionState>& suspensionStates,
    PxVehicleArrayData<const PxVehicleSuspensionComplianceState>& suspensionComplianceStates,
    PxVehicleArrayData<const PxVehicleTireSpeedState>& tireSpeedStates,
    PxVehicleArrayData<PxVehicleWheelRigidBody1dState>& wheelRigidBody1dStates,
    PxVehicleArrayData<PxVehicleWheelLocalPose>& wheelLocalPoses)
{
    axleDescription = &axle_description_;
    steerResponseStates.setData(steer_response_states_.data());
    wheelParams.setData(wheel_params_.data());
    suspensionParams.setData(suspension_params_.data());
    actuationStates.setData(actuation_states_.data());
    suspensionStates.setData(suspension_states_.data());
    suspensionComplianceStates.setData(suspension_compliance_states_.data());
    tireSpeedStates.setData(tire_speed_states_.data());
    wheelRigidBody1dStates.setData(wheel_rigid_body_states_.data());
    wheelLocalPoses.setData(wheel_local_poses_.data());
}

void Instance::getDataForRigidBodyComponent(
    const PxVehicleAxleDescription*& axleDescription,
    const PxVehicleRigidBodyParams*& rigidBodyParams,
    PxVehicleArrayData<const PxVehicleSuspensionForce>& suspensionForces,
    PxVehicleArrayData<const PxVehicleTireForce>& tireForces,
    const PxVehicleAntiRollTorque*& antiRollTorque,
    PxVehicleRigidBodyState*& rigidBodyState)
{
    axleDescription = &axle_description_;
    rigidBodyParams = &rigid_body_params_;
    suspensionForces.setData(suspension_forces_.data());
    tireForces.setData(tire_forces_.data());
    antiRollTorque = &anti_roll_torque_;
    rigidBodyState = &rigid_body_state_;
}
}
