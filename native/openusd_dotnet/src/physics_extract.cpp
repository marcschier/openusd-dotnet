// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"
#include "openusd_physics_extract.h"
#include "pxr/base/gf/matrix3d.h"
#include "pxr/base/gf/quatd.h"
#include "pxr/base/gf/rotation.h"
#include "pxr/usd/sdf/listOp.h"
#include "pxr/usd/usd/tokens.h"
#include "pxr/usd/usdGeom/metrics.h"
#include "pxr/usd/usdPhysics/metrics.h"
#include "pxr/usd/usdPhysics/tokens.h"

#include <cmath>
#include <map>
#include <unordered_map>

/*
 * Physics extraction: exactly one composed stage traversal per call.
 *
 * The traversal collects every authored physics opinion into process-local intermediate
 * state, then a second pass that touches no USD object at all resolves cross references
 * and serializes one immutable, pointer-free page. Once openusd_physics_extract_stage
 * returns, the page has no relationship to the stage: it stores paths as text, identities
 * as content hashes and indices, and nothing else.
 *
 * Record layout, mirrored by the managed reader and validated by the managed validator.
 * All sizes are byte counts and every section starts on an 8 byte boundary.
 *
 *   header (216 bytes)
 *      0 u64 magic
 *      8 u32 abi_version
 *     12 u32 header_size
 *     16 u64 byte_size
 *     24 u64 fingerprint_low
 *     32 u64 fingerprint_high
 *     40 f64 meters_per_unit
 *     48 f64 kilograms_per_unit
 *     56 f64 time_codes_per_second
 *     64 f64 start_time_code
 *     72 f64 end_time_code
 *     80 f64 time_code
 *     88 u32 up_axis
 *     92 u32 flags
 *     96 i32 default_scene_index
 *    100 u32 truncation_flags
 *    104 10 spans of (u32 offset, u32 count): strings, objects, properties,
 *        relationships, targets, numbers, texts, points, indices, diagnostics
 *    184 4 reserved u64
 *
 *   object       208 bytes
 *   property      40 bytes
 *   relationship  24 bytes
 *   target        16 bytes
 *   number         8 bytes (double)
 *   text           8 bytes (u32 string offset, u32 byte length)
 *   point         12 bytes (3 floats, local space)
 *   index          4 bytes (u32)
 *   diagnostic    32 bytes
 */

namespace
{

constexpr uint32_t kAlignment = OPENUSD_PHYSICS_EXTRACT_ALIGNMENT;

// One-call proof counters. Extraction is the only writer.
std::atomic<uint64_t> gTraversalCount{0};
std::atomic<uint64_t> gVisitedPrimCount{0};

// ---------------------------------------------------------------------------------------
// Deterministic 128 bit content fingerprint.
// ---------------------------------------------------------------------------------------

class Fingerprint
{
public:
    void AddBytes(const void* data, size_t size) noexcept
    {
        const unsigned char* bytes = static_cast<const unsigned char*>(data);
        for (size_t i = 0; i < size; ++i)
        {
            low_ = (low_ ^ static_cast<uint64_t>(bytes[i])) * UINT64_C(0x00000100000001B3);
            high_ = (high_ ^ static_cast<uint64_t>(bytes[i])) * UINT64_C(0x0000010000000195);
            high_ ^= (high_ >> 29);
        }
    }

    void AddText(std::string_view text) noexcept
    {
        const uint64_t size = static_cast<uint64_t>(text.size());
        AddBytes(&size, sizeof(size));
        AddBytes(text.data(), text.size());
    }

    void AddUInt(uint64_t value) noexcept
    {
        AddBytes(&value, sizeof(value));
    }

    void AddReal(double value) noexcept
    {
        // Normalize the two zero encodings and every NaN payload so that equivalent
        // authored values always fingerprint identically.
        if (std::isnan(value))
        {
            AddUInt(UINT64_C(0x7FF8000000000000));
            return;
        }
        if (value == 0.0)
        {
            value = 0.0;
        }
        uint64_t bits = 0;
        std::memcpy(&bits, &value, sizeof(bits));
        AddUInt(bits);
    }

    uint64_t Low() const noexcept
    {
        return low_;
    }

    uint64_t High() const noexcept
    {
        return high_;
    }

private:
    uint64_t low_ = UINT64_C(0xCBF29CE484222325);
    uint64_t high_ = UINT64_C(0x9E3779B97F4A7C15);
};

uint64_t HashIdentity(std::string_view text) noexcept
{
    uint64_t hash = UINT64_C(0xCBF29CE484222325);
    for (const char character : text)
    {
        hash ^= static_cast<uint64_t>(static_cast<unsigned char>(character));
        hash *= UINT64_C(0x00000100000001B3);
    }
    // Zero is the reserved invalid identity.
    return hash == OPENUSD_PHYSICS_EXTRACT_INVALID_ID ? UINT64_C(1) : hash;
}

// ---------------------------------------------------------------------------------------
// Bounded byte writer with checked arithmetic.
// ---------------------------------------------------------------------------------------

class ByteWriter
{
public:
    void PutU32(uint32_t value)
    {
        Append(&value, sizeof(value));
    }

    void PutI32(int32_t value)
    {
        Append(&value, sizeof(value));
    }

    void PutU64(uint64_t value)
    {
        Append(&value, sizeof(value));
    }

    void PutF64(double value)
    {
        Append(&value, sizeof(value));
    }

    void PutF32(float value)
    {
        Append(&value, sizeof(value));
    }

    void Append(const void* data, size_t size)
    {
        const unsigned char* bytes = static_cast<const unsigned char*>(data);
        bytes_.insert(bytes_.end(), bytes, bytes + size);
    }

    void AlignTo(uint32_t alignment)
    {
        while ((bytes_.size() % alignment) != 0)
        {
            bytes_.push_back(0);
        }
    }

    size_t Size() const noexcept
    {
        return bytes_.size();
    }

    std::vector<unsigned char>& Bytes() noexcept
    {
        return bytes_;
    }

private:
    std::vector<unsigned char> bytes_;
};

// Interns strings once and hands out offsets inside the string section.
class StringTable
{
public:
    StringTable()
    {
        bytes_.push_back(0);
    }

    uint32_t Add(std::string_view text)
    {
        if (text.empty())
        {
            return 0;
        }
        const std::string key(text);
        const auto existing = offsets_.find(key);
        if (existing != offsets_.end())
        {
            return existing->second;
        }
        if (bytes_.size() + text.size() + 1 > static_cast<size_t>(maxBytes_))
        {
            truncated_ = true;
            return 0;
        }
        const uint32_t offset = static_cast<uint32_t>(bytes_.size());
        bytes_.insert(bytes_.end(), text.begin(), text.end());
        bytes_.push_back(0);
        offsets_.emplace(key, offset);
        return offset;
    }

    void SetMaxBytes(uint32_t value) noexcept
    {
        maxBytes_ = value;
    }

    bool Truncated() const noexcept
    {
        return truncated_;
    }

    const std::vector<unsigned char>& Bytes() const noexcept
    {
        return bytes_;
    }

private:
    std::vector<unsigned char> bytes_;
    std::unordered_map<std::string, uint32_t> offsets_;
    uint32_t maxBytes_ = OPENUSD_PHYSICS_EXTRACT_MAX_STRING_BYTES;
    bool truncated_ = false;
};

// ---------------------------------------------------------------------------------------
// Collected intermediate state. None of it references a USD object.
// ---------------------------------------------------------------------------------------

struct PropertyData
{
    uint32_t key = OPENUSD_PHYSICS_EXTRACT_KEY_UNMAPPED;
    std::string name;
    uint32_t valueType = OPENUSD_PHYSICS_EXTRACT_VALUE_NONE;
    uint32_t flags = OPENUSD_PHYSICS_EXTRACT_PROPERTY_FLAG_NONE;
    uint32_t source = OPENUSD_PHYSICS_EXTRACT_SOURCE_FALLBACK;
    double scalar = 0.0;
    std::vector<double> numbers;
    std::vector<std::string> texts;
};

struct RelationshipData
{
    uint32_t key = OPENUSD_PHYSICS_EXTRACT_KEY_UNMAPPED;
    std::string name;
    uint32_t flags = OPENUSD_PHYSICS_EXTRACT_PROPERTY_FLAG_NONE;
    std::vector<std::string> targets;
    std::vector<int32_t> targetIndices;
};

struct ObjectData
{
    uint64_t id = OPENUSD_PHYSICS_EXTRACT_INVALID_ID;
    uint64_t parentId = OPENUSD_PHYSICS_EXTRACT_INVALID_ID;
    uint64_t prototypeId = OPENUSD_PHYSICS_EXTRACT_INVALID_ID;
    std::string path;
    std::string name;
    std::string typeName;
    uint32_t objectType = OPENUSD_PHYSICS_EXTRACT_OBJECT_UNKNOWN;
    uint32_t domains = OPENUSD_PHYSICS_EXTRACT_DOMAIN_NONE;
    uint32_t flags = OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_NONE;
    uint32_t geometry = OPENUSD_PHYSICS_EXTRACT_GEOMETRY_NONE;
    uint32_t geometryAxis = 1u;
    int32_t sceneIndex = -1;
    int32_t parentBodyIndex = -1;
    double position[3] = {0.0, 0.0, 0.0};
    double rotation[4] = {1.0, 0.0, 0.0, 0.0};
    double scale[3] = {1.0, 1.0, 1.0};
    double extent[3] = {0.0, 0.0, 0.0};
    std::vector<PropertyData> properties;
    std::vector<RelationshipData> relationships;
    // Authored physics opinions that carry no canonical meaning. They always contribute to
    // the fingerprint; they only become page records when the caller asks for them.
    std::vector<PropertyData> hiddenProperties;
    std::vector<RelationshipData> hiddenRelationships;
    std::vector<float> points;
    std::vector<uint32_t> indices;
    std::string simulationOwnerPath;
    uint32_t simulationOwnerCount = 0;
    uint32_t diagnosticCount = 0;
};

struct DiagnosticData
{
    uint32_t severity = OPENUSD_PHYSICS_EXTRACT_SEVERITY_INFORMATION;
    uint32_t category = OPENUSD_PHYSICS_EXTRACT_CATEGORY_SCHEMA;
    uint32_t code = OPENUSD_PHYSICS_EXTRACT_CODE_NONE;
    int32_t objectIndex = -1;
    std::string message;
    uint32_t key = OPENUSD_PHYSICS_EXTRACT_KEY_UNMAPPED;
    uint64_t objectId = OPENUSD_PHYSICS_EXTRACT_INVALID_ID;
};

// ---------------------------------------------------------------------------------------
// Canonical property table. One row per simulated quantity.
// ---------------------------------------------------------------------------------------

struct CanonicalEntry
{
    uint32_t key;
    const char* project;
    const char* standard;
    const char* leaf;
    uint32_t domain;
    bool relationship;
};

// Some relationships are authored on whatever object owns the prim rather than on the object
// that names the domain, so they resolve against every domain that may carry them.
constexpr uint32_t kSimulationOwnerDomains =
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_SCENE |
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY |
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_COLLISION |
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_ARTICULATION |
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE |
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE;

constexpr uint32_t kFilteringDomains =
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_FILTERING |
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY |
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_COLLISION |
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_ARTICULATION |
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE |
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE;

constexpr uint32_t kMaterialBindingDomains =
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_MATERIAL |
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_COLLISION |
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY |
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE;

// Ordered by key. Table order is also the deterministic order in which canonical
// properties are emitted for one object.
const CanonicalEntry kCanonicalEntries[] = {
    {OPENUSD_PHYSICS_EXTRACT_KEY_SCENE_GRAVITY_DIRECTION, nullptr, "physics:gravityDirection",
     "gravityDirection", OPENUSD_PHYSICS_EXTRACT_DOMAIN_SCENE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_SCENE_GRAVITY_MAGNITUDE, nullptr, "physics:gravityMagnitude",
     "gravityMagnitude", OPENUSD_PHYSICS_EXTRACT_DOMAIN_SCENE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_SCENE_POSITION_ITERATIONS,
     "openUsdPhysics:scene:positionIterationCount", nullptr, "positionIterationCount",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_SCENE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_SCENE_VELOCITY_ITERATIONS,
     "openUsdPhysics:scene:velocityIterationCount", nullptr, "velocityIterationCount",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_SCENE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_SCENE_BOUNCE_THRESHOLD, "openUsdPhysics:scene:bounceThreshold",
     nullptr, "bounceThreshold", OPENUSD_PHYSICS_EXTRACT_DOMAIN_SCENE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_SCENE_ENABLE_CCD, "openUsdPhysics:scene:enableCCD", nullptr,
     "enableCCD", OPENUSD_PHYSICS_EXTRACT_DOMAIN_SCENE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_SCENE_ENABLE_STABILIZATION,
     "openUsdPhysics:scene:enableStabilization", nullptr, "enableStabilization",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_SCENE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_SCENE_ENABLE_DETERMINISM,
     "openUsdPhysics:scene:enableEnhancedDeterminism", nullptr, "enableEnhancedDeterminism",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_SCENE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_SCENE_TIME_STEPS_PER_SECOND,
     "openUsdPhysics:scene:timeStepsPerSecond", nullptr, "timeStepsPerSecond",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_SCENE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_SCENE_MAX_SUBSTEPS, "openUsdPhysics:scene:maxSubStepCount",
     nullptr, "maxSubStepCount", OPENUSD_PHYSICS_EXTRACT_DOMAIN_SCENE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_SCENE_GPU_REQUEST_MODE, "openUsdPhysics:scene:gpuRequestMode",
     nullptr, "gpuRequestMode", OPENUSD_PHYSICS_EXTRACT_DOMAIN_SCENE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_SCENE_REPORT_CONTACTS, "openUsdPhysics:scene:reportContacts",
     nullptr, "sceneReportContacts", OPENUSD_PHYSICS_EXTRACT_DOMAIN_SCENE, false},

    {OPENUSD_PHYSICS_EXTRACT_KEY_BODY_ENABLED, nullptr, "physics:rigidBodyEnabled",
     "rigidBodyEnabled", OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_BODY_KINEMATIC, nullptr, "physics:kinematicEnabled",
     "kinematicEnabled", OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_BODY_STARTS_ASLEEP, nullptr, "physics:startsAsleep",
     "startsAsleep", OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_BODY_VELOCITY, nullptr, "physics:velocity", "velocity",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_BODY_ANGULAR_VELOCITY, nullptr, "physics:angularVelocity",
     "angularVelocity", OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_BODY_DISABLE_GRAVITY, "openUsdPhysics:body:disableGravity",
     nullptr, "disableGravity", OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_BODY_MAX_LINEAR_VELOCITY,
     "openUsdPhysics:body:maxLinearVelocity", nullptr, "maxLinearVelocity",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_BODY_MAX_ANGULAR_VELOCITY,
     "openUsdPhysics:body:maxAngularVelocity", nullptr, "maxAngularVelocity",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_BODY_POSITION_ITERATIONS,
     "openUsdPhysics:body:positionIterationCount", nullptr, "bodyPositionIterationCount",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_BODY_VELOCITY_ITERATIONS,
     "openUsdPhysics:body:velocityIterationCount", nullptr, "bodyVelocityIterationCount",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_BODY_ENABLE_CCD, "openUsdPhysics:body:enableCCD", nullptr,
     "bodyEnableCCD", OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_BODY_SLEEP_THRESHOLD, "openUsdPhysics:body:sleepThreshold",
     nullptr, "sleepThreshold", OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_BODY_LINEAR_DAMPING, nullptr, nullptr, "linearDamping",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_BODY_ANGULAR_DAMPING, nullptr, nullptr, "angularDamping",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY, false},

    {OPENUSD_PHYSICS_EXTRACT_KEY_MASS_MASS, nullptr, "physics:mass", "mass",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_MASS_DENSITY, nullptr, "physics:density", "bodyDensity",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_MASS_CENTER_OF_MASS, nullptr, "physics:centerOfMass",
     "centerOfMass", OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_MASS_DIAGONAL_INERTIA, nullptr, "physics:diagonalInertia",
     "diagonalInertia", OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_MASS_PRINCIPAL_AXES, nullptr, "physics:principalAxes",
     "principalAxes", OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY, false},

    {OPENUSD_PHYSICS_EXTRACT_KEY_COLLISION_ENABLED, nullptr, "physics:collisionEnabled",
     "collisionEnabled", OPENUSD_PHYSICS_EXTRACT_DOMAIN_COLLISION, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_COLLISION_APPROXIMATION, nullptr, "physics:approximation",
     "approximation", OPENUSD_PHYSICS_EXTRACT_DOMAIN_COLLISION, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_COLLISION_CONTACT_OFFSET,
     "openUsdPhysics:collision:contactOffset", nullptr, "contactOffset",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_COLLISION, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_COLLISION_REST_OFFSET, "openUsdPhysics:collision:restOffset",
     nullptr, "restOffset", OPENUSD_PHYSICS_EXTRACT_DOMAIN_COLLISION, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_COLLISION_REPORT_CONTACTS,
     "openUsdPhysics:collision:reportContacts", nullptr, "reportContacts",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_COLLISION, false},

    {OPENUSD_PHYSICS_EXTRACT_KEY_MATERIAL_STATIC_FRICTION, nullptr, "physics:staticFriction",
     "staticFriction", OPENUSD_PHYSICS_EXTRACT_DOMAIN_MATERIAL, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_MATERIAL_DYNAMIC_FRICTION, nullptr, "physics:dynamicFriction",
     "dynamicFriction", OPENUSD_PHYSICS_EXTRACT_DOMAIN_MATERIAL, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_MATERIAL_RESTITUTION, nullptr, "physics:restitution",
     "restitution", OPENUSD_PHYSICS_EXTRACT_DOMAIN_MATERIAL, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_MATERIAL_DENSITY, nullptr, "physics:density",
     "materialDensity", OPENUSD_PHYSICS_EXTRACT_DOMAIN_MATERIAL, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_MATERIAL_FRICTION_COMBINE,
     "openUsdPhysics:material:frictionCombineMode", nullptr, "frictionCombineMode",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_MATERIAL, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_MATERIAL_RESTITUTION_COMBINE,
     "openUsdPhysics:material:restitutionCombineMode", nullptr, "restitutionCombineMode",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_MATERIAL, false},

    {OPENUSD_PHYSICS_EXTRACT_KEY_JOINT_ENABLED, nullptr, "physics:jointEnabled",
     "jointEnabled", OPENUSD_PHYSICS_EXTRACT_DOMAIN_JOINT, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_JOINT_LOCAL_POS0, nullptr, "physics:localPos0", "localPos0",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_JOINT, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_JOINT_LOCAL_ROT0, nullptr, "physics:localRot0", "localRot0",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_JOINT, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_JOINT_LOCAL_POS1, nullptr, "physics:localPos1", "localPos1",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_JOINT, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_JOINT_LOCAL_ROT1, nullptr, "physics:localRot1", "localRot1",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_JOINT, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_JOINT_AXIS, nullptr, "physics:axis", "axis",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_JOINT, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_JOINT_LOWER_LIMIT, nullptr, "physics:lowerLimit",
     "lowerLimit", OPENUSD_PHYSICS_EXTRACT_DOMAIN_JOINT, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_JOINT_UPPER_LIMIT, nullptr, "physics:upperLimit",
     "upperLimit", OPENUSD_PHYSICS_EXTRACT_DOMAIN_JOINT, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_JOINT_MIN_DISTANCE, nullptr, "physics:minDistance",
     "minDistance", OPENUSD_PHYSICS_EXTRACT_DOMAIN_JOINT, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_JOINT_MAX_DISTANCE, nullptr, "physics:maxDistance",
     "maxDistance", OPENUSD_PHYSICS_EXTRACT_DOMAIN_JOINT, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_JOINT_CONE_ANGLE0, nullptr, "physics:coneAngle0Limit",
     "coneAngle0Limit", OPENUSD_PHYSICS_EXTRACT_DOMAIN_JOINT, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_JOINT_CONE_ANGLE1, nullptr, "physics:coneAngle1Limit",
     "coneAngle1Limit", OPENUSD_PHYSICS_EXTRACT_DOMAIN_JOINT, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_JOINT_BREAK_FORCE, nullptr, "physics:breakForce",
     "breakForce", OPENUSD_PHYSICS_EXTRACT_DOMAIN_JOINT, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_JOINT_BREAK_TORQUE, nullptr, "physics:breakTorque",
     "breakTorque", OPENUSD_PHYSICS_EXTRACT_DOMAIN_JOINT, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_JOINT_COLLISION_ENABLED, nullptr, "physics:collisionEnabled",
     "jointCollisionEnabled", OPENUSD_PHYSICS_EXTRACT_DOMAIN_JOINT, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_JOINT_EXCLUDE_FROM_ARTICULATION, nullptr,
     "physics:excludeFromArticulation", "excludeFromArticulation",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_JOINT, false},

    {OPENUSD_PHYSICS_EXTRACT_KEY_FILTER_INVERT_GROUPS,
     "openUsdPhysics:collisionFilter:invertFilteredGroups", "physics:invertFilteredGroups",
     "invertFilteredGroups", OPENUSD_PHYSICS_EXTRACT_DOMAIN_FILTERING, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_FILTER_MERGE_GROUP,
     "openUsdPhysics:collisionFilter:mergeGroupName", "physics:mergeGroup", "mergeGroup",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_FILTERING, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_FILTER_ENABLED, "openUsdPhysics:collisionFilter:enabled",
     nullptr, "filterEnabled", OPENUSD_PHYSICS_EXTRACT_DOMAIN_FILTERING, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_FILTER_MODE,
     "openUsdPhysics:collisionFilter:pairFilterMode", nullptr, "pairFilterMode",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_FILTERING, false},

    {OPENUSD_PHYSICS_EXTRACT_KEY_ARTICULATION_FIX_BASE,
     "openUsdPhysics:articulation:fixBase", nullptr, "fixBase",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_ARTICULATION, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_ARTICULATION_SELF_COLLISIONS,
     "openUsdPhysics:articulation:enabledSelfCollisions", nullptr, "enabledSelfCollisions",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_ARTICULATION, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_ARTICULATION_POSITION_ITERATIONS,
     "openUsdPhysics:articulation:positionIterationCount", nullptr,
     "articulationPositionIterationCount", OPENUSD_PHYSICS_EXTRACT_DOMAIN_ARTICULATION, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_ARTICULATION_VELOCITY_ITERATIONS,
     "openUsdPhysics:articulation:velocityIterationCount", nullptr,
     "articulationVelocityIterationCount", OPENUSD_PHYSICS_EXTRACT_DOMAIN_ARTICULATION, false},

    {OPENUSD_PHYSICS_EXTRACT_KEY_SIMULATION_IDENTITY, "openUsdPhysics:simulation:identity",
     nullptr, "identity", OPENUSD_PHYSICS_EXTRACT_DOMAIN_SIMULATION_METADATA, false},    {OPENUSD_PHYSICS_EXTRACT_KEY_SIMULATION_IDENTITY_DOMAIN,
     "openUsdPhysics:simulation:identityDomain", nullptr, "identityDomain",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_SIMULATION_METADATA, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_SIMULATION_IDENTITY_INDEX,
     "openUsdPhysics:simulation:identityIndex", nullptr, "identityIndex",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_SIMULATION_METADATA, false},

    {OPENUSD_PHYSICS_EXTRACT_KEY_TENDON_ENABLED, "openUsdPhysics:fixedTendon:enabled",
     nullptr, "tendonEnabled", OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_TENDON_ENABLED, "openUsdPhysics:spatialTendon:enabled",
     nullptr, "spatialTendonEnabled", OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_TENDON_STIFFNESS, "openUsdPhysics:fixedTendon:stiffness",
     nullptr, "tendonStiffness", OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_TENDON_STIFFNESS, "openUsdPhysics:spatialTendon:stiffness",
     nullptr, "spatialTendonStiffness", OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_TENDON_DAMPING, "openUsdPhysics:fixedTendon:damping",
     nullptr, "tendonDamping", OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_TENDON_DAMPING, "openUsdPhysics:spatialTendon:damping",
     nullptr, "spatialTendonDamping", OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_TENDON_LIMIT_STIFFNESS,
     "openUsdPhysics:fixedTendon:limitStiffness", nullptr, "tendonLimitStiffness",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_TENDON_LIMIT_STIFFNESS,
     "openUsdPhysics:spatialTendon:limitStiffness", nullptr, "spatialTendonLimitStiffness",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_TENDON_OFFSET, "openUsdPhysics:fixedTendon:offset", nullptr,
     "tendonOffset", OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_TENDON_OFFSET, "openUsdPhysics:spatialTendon:offset", nullptr,
     "spatialTendonOffset", OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_TENDON_REST_LENGTH, "openUsdPhysics:fixedTendon:restLength",
     nullptr, "tendonRestLength", OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_TENDON_LOWER_LIMIT, "openUsdPhysics:fixedTendon:lowerLimit",
     nullptr, "tendonLowerLimit", OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_TENDON_UPPER_LIMIT, "openUsdPhysics:fixedTendon:upperLimit",
     nullptr, "tendonUpperLimit", OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_TENDON_GEARINGS, "openUsdPhysics:fixedTendon:gearings",
     nullptr, "tendonGearings", OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_TENDON_FORCE_COEFFICIENTS,
     "openUsdPhysics:fixedTendon:forceCoefficients", nullptr, "tendonForceCoefficients",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_ATTACHMENT_GEARING,
     "openUsdPhysics:tendonAttachment:gearing", nullptr, "tendonAttachmentGearing",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_ATTACHMENT_LOCAL_POSITION,
     "openUsdPhysics:tendonAttachment:localPosition", nullptr, "tendonAttachmentLocalPosition",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_ATTACHMENT_REST_LENGTH,
     "openUsdPhysics:tendonAttachment:restLength", nullptr, "tendonAttachmentRestLength",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_ATTACHMENT_LOWER_LIMIT,
     "openUsdPhysics:tendonAttachment:lowerLimit", nullptr, "tendonAttachmentLowerLimit",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_ATTACHMENT_UPPER_LIMIT,
     "openUsdPhysics:tendonAttachment:upperLimit", nullptr, "tendonAttachmentUpperLimit",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_ATTACHMENT_ROLE, "openUsdPhysics:tendonAttachment:role",
     nullptr, "tendonAttachmentRole", OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, false},

    {OPENUSD_PHYSICS_EXTRACT_KEY_MIMIC_ENABLED, "openUsdPhysics:mimicJoint:enabled", nullptr,
     "mimicEnabled", OPENUSD_PHYSICS_EXTRACT_DOMAIN_MIMIC, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_MIMIC_GEARING, "openUsdPhysics:mimicJoint:gearing", nullptr,
     "mimicGearing", OPENUSD_PHYSICS_EXTRACT_DOMAIN_MIMIC, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_MIMIC_OFFSET, "openUsdPhysics:mimicJoint:offset", nullptr,
     "mimicOffset", OPENUSD_PHYSICS_EXTRACT_DOMAIN_MIMIC, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_MIMIC_AXIS, "openUsdPhysics:mimicJoint:axis", nullptr,
     "mimicAxis", OPENUSD_PHYSICS_EXTRACT_DOMAIN_MIMIC, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_MIMIC_REFERENCE_AXIS,
     "openUsdPhysics:mimicJoint:referenceAxis", nullptr, "mimicReferenceAxis",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_MIMIC, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_MIMIC_NATURAL_FREQUENCY,
     "openUsdPhysics:mimicJoint:naturalFrequency", nullptr, "mimicNaturalFrequency",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_MIMIC, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_MIMIC_DAMPING_RATIO,
     "openUsdPhysics:mimicJoint:dampingRatio", nullptr, "mimicDampingRatio",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_MIMIC, false},

    {OPENUSD_PHYSICS_EXTRACT_KEY_VEHICLE_ENABLED, "openUsdPhysics:vehicle:enabled", nullptr,
     "vehicleEnabled", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_VEHICLE_DRIVE_TYPE, "openUsdPhysics:vehicle:driveType",
     nullptr, "vehicleDriveType", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_VEHICLE_LONGITUDINAL_AXIS,
     "openUsdPhysics:vehicle:longitudinalAxis", nullptr, "vehicleLongitudinalAxis",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_VEHICLE_LATERAL_AXIS, "openUsdPhysics:vehicle:lateralAxis",
     nullptr, "vehicleLateralAxis", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_VEHICLE_VERTICAL_AXIS, "openUsdPhysics:vehicle:verticalAxis",
     nullptr, "vehicleVerticalAxis", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_VEHICLE_SUSPENSION_QUERY_TYPE,
     "openUsdPhysics:vehicle:suspensionQueryType", nullptr, "vehicleSuspensionQueryType",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},

    {OPENUSD_PHYSICS_EXTRACT_KEY_ENGINE_PEAK_TORQUE, "openUsdPhysics:engine:peakTorque",
     nullptr, "enginePeakTorque", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_ENGINE_MAX_ROTATION_SPEED,
     "openUsdPhysics:engine:maxRotationSpeed", nullptr, "engineMaxRotationSpeed",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_ENGINE_IDLE_ROTATION_SPEED,
     "openUsdPhysics:engine:idleRotationSpeed", nullptr, "engineIdleRotationSpeed",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_ENGINE_MOMENT_OF_INERTIA,
     "openUsdPhysics:engine:momentOfInertia", nullptr, "engineMomentOfInertia",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_ENGINE_DAMPING_FULL_THROTTLE,
     "openUsdPhysics:engine:dampingRateFullThrottle", nullptr, "engineDampingFullThrottle",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_ENGINE_DAMPING_ZERO_THROTTLE_CLUTCH_ENGAGED,
     "openUsdPhysics:engine:dampingRateZeroThrottleClutchEngaged", nullptr,
     "engineDampingZeroThrottleClutchEngaged", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_ENGINE_DAMPING_ZERO_THROTTLE_CLUTCH_DISENGAGED,
     "openUsdPhysics:engine:dampingRateZeroThrottleClutchDisengaged", nullptr,
     "engineDampingZeroThrottleClutchDisengaged", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE,
     false},

    {OPENUSD_PHYSICS_EXTRACT_KEY_GEARS_RATIOS, "openUsdPhysics:gears:ratios", nullptr,
     "gearsRatios", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_GEARS_RATIO_SCALE, "openUsdPhysics:gears:ratioScale", nullptr,
     "gearsRatioScale", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_GEARS_SWITCH_TIME, "openUsdPhysics:gears:switchTime", nullptr,
     "gearsSwitchTime", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_AUTO_GEAR_BOX_UP_RATIOS,
     "openUsdPhysics:autoGearBox:upRatios", nullptr, "autoGearBoxUpRatios",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_AUTO_GEAR_BOX_DOWN_RATIOS,
     "openUsdPhysics:autoGearBox:downRatios", nullptr, "autoGearBoxDownRatios",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_AUTO_GEAR_BOX_LATENCY, "openUsdPhysics:autoGearBox:latency",
     nullptr, "autoGearBoxLatency", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_CLUTCH_STRENGTH, "openUsdPhysics:clutch:strength", nullptr,
     "clutchStrength", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DIFFERENTIAL_WHEELS, "openUsdPhysics:differential:wheels",
     nullptr, "differentialWheels", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DIFFERENTIAL_TORQUE_RATIOS,
     "openUsdPhysics:differential:torqueRatios", nullptr, "differentialTorqueRatios",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_BRAKES_MAX_BRAKE_TORQUE,
     "openUsdPhysics:brakes:primaryMaxBrakeTorque", nullptr, "brakesPrimaryMaxBrakeTorque",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_BRAKES_WHEELS, "openUsdPhysics:brakes:primaryWheels", nullptr,
     "brakesPrimaryWheels", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_BRAKES_TORQUE_MULTIPLIERS,
     "openUsdPhysics:brakes:primaryTorqueMultipliers", nullptr,
     "brakesPrimaryTorqueMultipliers", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_STEERING_MAX_STEER_ANGLE,
     "openUsdPhysics:steering:maxSteerAngle", nullptr, "steeringMaxSteerAngle",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_STEERING_WHEELS, "openUsdPhysics:steering:wheels", nullptr,
     "steeringWheels", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_STEERING_ANGLE_MULTIPLIERS,
     "openUsdPhysics:steering:angleMultipliers", nullptr, "steeringAngleMultipliers",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_BRAKES_SECONDARY_MAX_BRAKE_TORQUE,
     "openUsdPhysics:brakes:secondaryMaxBrakeTorque", nullptr, "brakesSecondaryMaxBrakeTorque",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_BRAKES_SECONDARY_WHEELS,
     "openUsdPhysics:brakes:secondaryWheels", nullptr, "brakesSecondaryWheels",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_BRAKES_SECONDARY_TORQUE_MULTIPLIERS,
     "openUsdPhysics:brakes:secondaryTorqueMultipliers", nullptr,
     "brakesSecondaryTorqueMultipliers", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},

    {OPENUSD_PHYSICS_EXTRACT_KEY_WHEEL_ATTACHMENT_INDEX,
     "openUsdPhysics:wheelAttachment:index", nullptr, "wheelAttachmentIndex",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_WHEEL_ATTACHMENT_SUSPENSION_POSITION,
     "openUsdPhysics:wheelAttachment:suspensionFramePosition", nullptr,
     "wheelAttachmentSuspensionFramePosition", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_WHEEL_ATTACHMENT_SUSPENSION_TRAVEL_DIR,
     "openUsdPhysics:wheelAttachment:suspensionTravelDirection", nullptr,
     "wheelAttachmentSuspensionTravelDirection", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_WHEEL_ATTACHMENT_WHEEL_POSITION,
     "openUsdPhysics:wheelAttachment:wheelFramePosition", nullptr,
     "wheelAttachmentWheelFramePosition", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_WHEEL_RADIUS, "openUsdPhysics:wheel:radius", nullptr,
     "wheelRadius", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_WHEEL_WIDTH, "openUsdPhysics:wheel:width", nullptr,
     "wheelWidth", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_WHEEL_MASS, "openUsdPhysics:wheel:mass", nullptr, "wheelMass",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_WHEEL_MOMENT_OF_INERTIA,
     "openUsdPhysics:wheel:momentOfInertia", nullptr, "wheelMomentOfInertia",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_WHEEL_DAMPING_RATE, "openUsdPhysics:wheel:dampingRate",
     nullptr, "wheelDampingRate", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_SUSPENSION_SPRING_STRENGTH,
     "openUsdPhysics:suspension:springStrength", nullptr, "suspensionSpringStrength",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_SUSPENSION_SPRING_DAMPER_RATE,
     "openUsdPhysics:suspension:springDamperRate", nullptr, "suspensionSpringDamperRate",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_SUSPENSION_TRAVEL_DISTANCE,
     "openUsdPhysics:suspension:travelDistance", nullptr, "suspensionTravelDistance",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_SUSPENSION_SPRUNG_MASS,
     "openUsdPhysics:suspension:sprungMass", nullptr, "suspensionSprungMass",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_TIRE_LONGITUDINAL_STIFFNESS,
     "openUsdPhysics:tire:longitudinalStiffness", nullptr, "tireLongitudinalStiffness",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_TIRE_CAMBER_STIFFNESS,
     "openUsdPhysics:tire:camberStiffness", nullptr, "tireCamberStiffness",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_TIRE_REST_LOAD, "openUsdPhysics:tire:restLoad", nullptr,
     "tireRestLoad", OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE, false},

    {OPENUSD_PHYSICS_EXTRACT_KEY_CONTROLLER_ENABLED, "openUsdPhysics:controller:enabled",
     nullptr, "controllerEnabled", OPENUSD_PHYSICS_EXTRACT_DOMAIN_CONTROLLER, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_CONTROLLER_SHAPE_TYPE, "openUsdPhysics:controller:shapeType",
     nullptr, "controllerShapeType", OPENUSD_PHYSICS_EXTRACT_DOMAIN_CONTROLLER, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_CONTROLLER_RADIUS, "openUsdPhysics:controller:radius", nullptr,
     "controllerRadius", OPENUSD_PHYSICS_EXTRACT_DOMAIN_CONTROLLER, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_CONTROLLER_HEIGHT, "openUsdPhysics:controller:height", nullptr,
     "controllerHeight", OPENUSD_PHYSICS_EXTRACT_DOMAIN_CONTROLLER, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_CONTROLLER_HALF_EXTENTS,
     "openUsdPhysics:controller:halfExtents", nullptr, "controllerHalfExtents",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_CONTROLLER, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_CONTROLLER_UP_AXIS, "openUsdPhysics:controller:upAxis", nullptr,
     "controllerUpAxis", OPENUSD_PHYSICS_EXTRACT_DOMAIN_CONTROLLER, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_CONTROLLER_SLOPE_LIMIT, "openUsdPhysics:controller:slopeLimit",
     nullptr, "controllerSlopeLimit", OPENUSD_PHYSICS_EXTRACT_DOMAIN_CONTROLLER, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_CONTROLLER_STEP_OFFSET, "openUsdPhysics:controller:stepOffset",
     nullptr, "controllerStepOffset", OPENUSD_PHYSICS_EXTRACT_DOMAIN_CONTROLLER, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_CONTROLLER_CONTACT_OFFSET,
     "openUsdPhysics:controller:contactOffset", nullptr, "controllerContactOffset",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_CONTROLLER, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_CONTROLLER_DENSITY, "openUsdPhysics:controller:density",
     nullptr, "controllerDensity", OPENUSD_PHYSICS_EXTRACT_DOMAIN_CONTROLLER, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_CONTROLLER_SCALE_COEFF, "openUsdPhysics:controller:scaleCoeff",
     nullptr, "controllerScaleCoeff", OPENUSD_PHYSICS_EXTRACT_DOMAIN_CONTROLLER, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_CONTROLLER_VOLUME_GROWTH,
     "openUsdPhysics:controller:volumeGrowth", nullptr, "controllerVolumeGrowth",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_CONTROLLER, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_CONTROLLER_NON_WALKABLE_MODE,
     "openUsdPhysics:controller:nonWalkableMode", nullptr, "controllerNonWalkableMode",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_CONTROLLER, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_CONTROLLER_CLIMBING_MODE,
     "openUsdPhysics:controller:climbingMode", nullptr, "controllerClimbingMode",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_CONTROLLER, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_CONTROLLER_MIN_MOVE_DISTANCE,
     "openUsdPhysics:controller:minMoveDistance", nullptr, "controllerMinMoveDistance",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_CONTROLLER, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_CONTROLLER_MAX_JUMP_HEIGHT,
     "openUsdPhysics:controller:maxJumpHeight", nullptr, "controllerMaxJumpHeight",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_CONTROLLER, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_CONTROLLER_INVISIBLE_WALL_HEIGHT,
     "openUsdPhysics:controller:invisibleWallHeight", nullptr, "controllerInvisibleWallHeight",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_CONTROLLER, false},

    /* Position based dynamics particle systems, particle bodies, and their
     * material response. Every one of these is a CUDA accelerated domain, but
     * extraction is namespace neutral and device neutral: the values are read
     * whether or not a device exists, and the runtime is what decides whether
     * the objects can be built. */
    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_SYSTEM_ENABLED,
     "openUsdPhysics:particleSystem:enabled", nullptr, "particleSystemEnabled",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_SYSTEM_CONTACT_OFFSET,
     "openUsdPhysics:particleSystem:contactOffset", nullptr, "particleSystemContactOffset",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_SYSTEM_REST_OFFSET,
     "openUsdPhysics:particleSystem:restOffset", nullptr, "particleSystemRestOffset",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_SYSTEM_PARTICLE_CONTACT_OFFSET,
     "openUsdPhysics:particleSystem:particleContactOffset", nullptr,
     "particleSystemParticleContactOffset", OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_SYSTEM_SOLID_REST_OFFSET,
     "openUsdPhysics:particleSystem:solidRestOffset", nullptr, "particleSystemSolidRestOffset",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_SYSTEM_FLUID_REST_OFFSET,
     "openUsdPhysics:particleSystem:fluidRestOffset", nullptr, "particleSystemFluidRestOffset",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_SYSTEM_MAX_DEPENETRATION_VELOCITY,
     "openUsdPhysics:particleSystem:maxDepenetrationVelocity", nullptr,
     "particleSystemMaxDepenetrationVelocity", OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_SYSTEM_NEIGHBORHOOD_SCALE,
     "openUsdPhysics:particleSystem:neighborhoodScale", nullptr, "particleSystemNeighborhoodScale",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_SYSTEM_MAX_NEIGHBORHOOD,
     "openUsdPhysics:particleSystem:maxNeighborhood", nullptr, "particleSystemMaxNeighborhood",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_SYSTEM_SOLVER_POSITION_ITERATIONS,
     "openUsdPhysics:particleSystem:solverPositionIterationCount", nullptr,
     "particleSystemSolverPositionIterationCount", OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_SYSTEM_WIND,
     "openUsdPhysics:particleSystem:wind", nullptr, "particleSystemWind",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_SYSTEM_ENABLE_CCD,
     "openUsdPhysics:particleSystem:enableCCD", nullptr, "particleSystemEnableCCD",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_SYSTEM_GLOBAL_SELF_COLLISION,
     "openUsdPhysics:particleSystem:globalSelfCollisionEnabled", nullptr,
     "particleSystemGlobalSelfCollisionEnabled", OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_SYSTEM_NON_PARTICLE_COLLISION,
     "openUsdPhysics:particleSystem:nonParticleCollisionEnabled", nullptr,
     "particleSystemNonParticleCollisionEnabled", OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},

    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_BODY_ENABLED,
     "openUsdPhysics:particleSet:enabled", nullptr, "particleSetEnabled",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_BODY_FLUID,
     "openUsdPhysics:particleSet:fluid", nullptr, "particleSetFluid",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_BODY_MASS,
     "openUsdPhysics:particleSet:mass", nullptr, "particleSetMass",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_BODY_GROUP,
     "openUsdPhysics:particleSet:particleGroup", nullptr, "particleSetParticleGroup",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_BODY_SELF_COLLISION,
     "openUsdPhysics:particleSet:selfCollision", nullptr, "particleSetSelfCollision",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_BODY_REST_POINTS,
     "openUsdPhysics:particleSet:restPoints", nullptr, "particleSetRestPoints",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_BODY_ENABLED,
     "openUsdPhysics:particleCloth:enabled", nullptr, "particleClothEnabled",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_BODY_GROUP,
     "openUsdPhysics:particleCloth:particleGroup", nullptr, "particleClothParticleGroup",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_BODY_SELF_COLLISION,
     "openUsdPhysics:particleCloth:selfCollision", nullptr, "particleClothSelfCollision",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PARTICLE_BODY_REST_POINTS,
     "openUsdPhysics:particleCloth:restPoints", nullptr, "particleClothRestPoints",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},

    {OPENUSD_PHYSICS_EXTRACT_KEY_PBD_MATERIAL_FRICTION,
     "openUsdPhysics:pbdMaterial:friction", nullptr, "pbdMaterialFriction",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PBD_MATERIAL_DAMPING,
     "openUsdPhysics:pbdMaterial:damping", nullptr, "pbdMaterialDamping",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PBD_MATERIAL_ADHESION,
     "openUsdPhysics:pbdMaterial:adhesion", nullptr, "pbdMaterialAdhesion",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PBD_MATERIAL_ADHESION_OFFSET_SCALE,
     "openUsdPhysics:pbdMaterial:adhesionOffsetScale", nullptr, "pbdMaterialAdhesionOffsetScale",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PBD_MATERIAL_PARTICLE_FRICTION_SCALE,
     "openUsdPhysics:pbdMaterial:particleFrictionScale", nullptr, "pbdMaterialParticleFrictionScale",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PBD_MATERIAL_PARTICLE_ADHESION_SCALE,
     "openUsdPhysics:pbdMaterial:particleAdhesionScale", nullptr, "pbdMaterialParticleAdhesionScale",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PBD_MATERIAL_VISCOSITY,
     "openUsdPhysics:pbdMaterial:viscosity", nullptr, "pbdMaterialViscosity",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PBD_MATERIAL_SURFACE_TENSION,
     "openUsdPhysics:pbdMaterial:surfaceTension", nullptr, "pbdMaterialSurfaceTension",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PBD_MATERIAL_COHESION,
     "openUsdPhysics:pbdMaterial:cohesion", nullptr, "pbdMaterialCohesion",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PBD_MATERIAL_VORTICITY_CONFINEMENT,
     "openUsdPhysics:pbdMaterial:vorticityConfinement", nullptr, "pbdMaterialVorticityConfinement",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PBD_MATERIAL_DRAG,
     "openUsdPhysics:pbdMaterial:drag", nullptr, "pbdMaterialDrag",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PBD_MATERIAL_LIFT,
     "openUsdPhysics:pbdMaterial:lift", nullptr, "pbdMaterialLift",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PBD_MATERIAL_GRAVITY_SCALE,
     "openUsdPhysics:pbdMaterial:gravityScale", nullptr, "pbdMaterialGravityScale",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PBD_MATERIAL_DENSITY,
     "openUsdPhysics:pbdMaterial:density", nullptr, "pbdMaterialDensity",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_PBD_MATERIAL_CFL_COEFFICIENT,
     "openUsdPhysics:pbdMaterial:cflCoefficient", nullptr, "pbdMaterialCflCoefficient",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, false},

    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_ENABLED,
     "openUsdPhysics:surfaceDeformable:enabled", nullptr, "surfaceDeformableEnabled",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_ENABLE_CCD,
     "openUsdPhysics:surfaceDeformable:enableCCD", nullptr, "surfaceDeformableEnableCCD",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_SELF_COLLISION,
     "openUsdPhysics:surfaceDeformable:selfCollision", nullptr, "surfaceDeformableSelfCollision",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_SELF_COLLISION_FILTER_DISTANCE,
     "openUsdPhysics:surfaceDeformable:selfCollisionFilterDistance", nullptr,
     "surfaceDeformableSelfCollisionFilterDistance", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_SOLVER_POSITION_ITERATIONS,
     "openUsdPhysics:surfaceDeformable:solverPositionIterationCount", nullptr,
     "surfaceDeformableSolverPositionIterationCount", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_VERTEX_VELOCITY_DAMPING,
     "openUsdPhysics:surfaceDeformable:vertexVelocityDamping", nullptr,
     "surfaceDeformableVertexVelocityDamping", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_MAX_DISPLACEMENT,
     "openUsdPhysics:surfaceDeformable:maxDisplacement", nullptr, "surfaceDeformableMaxDisplacement",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_COLLISION_ITERATION_MULTIPLIER,
     "openUsdPhysics:surfaceDeformable:collisionIterationMultiplier", nullptr,
     "surfaceDeformableCollisionIterationMultiplier", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_COLLISION_PAIR_UPDATE_FREQUENCY,
     "openUsdPhysics:surfaceDeformable:collisionPairUpdateFrequency", nullptr,
     "surfaceDeformableCollisionPairUpdateFrequency", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_REST_POINTS,
     "openUsdPhysics:surfaceDeformable:restPoints", nullptr, "surfaceDeformableRestPoints",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_SIMULATION_INDICES,
     "openUsdPhysics:surfaceDeformable:simulationIndices", nullptr,
     "surfaceDeformableSimulationIndices", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},

    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_ENABLED,
     "openUsdPhysics:volumeDeformable:enabled", nullptr, "volumeDeformableEnabled",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_ENABLE_CCD,
     "openUsdPhysics:volumeDeformable:enableCCD", nullptr, "volumeDeformableEnableCCD",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_SELF_COLLISION,
     "openUsdPhysics:volumeDeformable:selfCollision", nullptr, "volumeDeformableSelfCollision",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_SELF_COLLISION_FILTER_DISTANCE,
     "openUsdPhysics:volumeDeformable:selfCollisionFilterDistance", nullptr,
     "volumeDeformableSelfCollisionFilterDistance", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_SOLVER_POSITION_ITERATIONS,
     "openUsdPhysics:volumeDeformable:solverPositionIterationCount", nullptr,
     "volumeDeformableSolverPositionIterationCount", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_VERTEX_VELOCITY_DAMPING,
     "openUsdPhysics:volumeDeformable:vertexVelocityDamping", nullptr,
     "volumeDeformableVertexVelocityDamping", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_KINEMATIC,
     "openUsdPhysics:volumeDeformable:kinematicEnabled", nullptr, "volumeDeformableKinematicEnabled",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_MAX_DEPENETRATION_VELOCITY,
     "openUsdPhysics:volumeDeformable:maxDepenetrationVelocity", nullptr,
     "volumeDeformableMaxDepenetrationVelocity", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_SETTLING_THRESHOLD,
     "openUsdPhysics:volumeDeformable:settlingThreshold", nullptr, "volumeDeformableSettlingThreshold",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_SLEEP_THRESHOLD,
     "openUsdPhysics:volumeDeformable:sleepThreshold", nullptr, "volumeDeformableSleepThreshold",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_SIMULATION_REST_POINTS,
     "openUsdPhysics:volumeDeformable:simulationRestPoints", nullptr,
     "volumeDeformableSimulationRestPoints", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_SIMULATION_INDICES,
     "openUsdPhysics:volumeDeformable:simulationIndices", nullptr,
     "volumeDeformableSimulationIndices", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_COLLISION_REST_POINTS,
     "openUsdPhysics:volumeDeformable:collisionRestPoints", nullptr,
     "volumeDeformableCollisionRestPoints", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_COLLISION_INDICES,
     "openUsdPhysics:volumeDeformable:collisionIndices", nullptr, "volumeDeformableCollisionIndices",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_HEXAHEDRAL_RESOLUTION,
     "openUsdPhysics:volumeDeformable:simulationHexahedralResolution", nullptr,
     "volumeDeformableSimulationHexahedralResolution", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},

    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_MATERIAL_YOUNGS_MODULUS,
     "openUsdPhysics:surfaceDeformableMaterial:youngsModulus", nullptr,
     "surfaceDeformableMaterialYoungsModulus", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_MATERIAL_POISSONS_RATIO,
     "openUsdPhysics:surfaceDeformableMaterial:poissonsRatio", nullptr,
     "surfaceDeformableMaterialPoissonsRatio", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_MATERIAL_DYNAMIC_FRICTION,
     "openUsdPhysics:surfaceDeformableMaterial:dynamicFriction", nullptr,
     "surfaceDeformableMaterialDynamicFriction", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_MATERIAL_DENSITY,
     "openUsdPhysics:surfaceDeformableMaterial:density", nullptr, "surfaceDeformableMaterialDensity",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_MATERIAL_ELASTICITY_DAMPING,
     "openUsdPhysics:surfaceDeformableMaterial:elasticityDamping", nullptr,
     "surfaceDeformableMaterialElasticityDamping", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_MATERIAL_BENDING_STIFFNESS,
     "openUsdPhysics:surfaceDeformableMaterial:bendingStiffness", nullptr,
     "surfaceDeformableMaterialBendingStiffness", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_MATERIAL_BENDING_DAMPING,
     "openUsdPhysics:surfaceDeformableMaterial:bendingDamping", nullptr,
     "surfaceDeformableMaterialBendingDamping", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_MATERIAL_THICKNESS,
     "openUsdPhysics:surfaceDeformableMaterial:thickness", nullptr,
     "surfaceDeformableMaterialThickness", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},

    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_MATERIAL_YOUNGS_MODULUS,
     "openUsdPhysics:volumeDeformableMaterial:youngsModulus", nullptr,
     "volumeDeformableMaterialYoungsModulus", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_MATERIAL_POISSONS_RATIO,
     "openUsdPhysics:volumeDeformableMaterial:poissonsRatio", nullptr,
     "volumeDeformableMaterialPoissonsRatio", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_MATERIAL_DYNAMIC_FRICTION,
     "openUsdPhysics:volumeDeformableMaterial:dynamicFriction", nullptr,
     "volumeDeformableMaterialDynamicFriction", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_MATERIAL_DENSITY,
     "openUsdPhysics:volumeDeformableMaterial:density", nullptr, "volumeDeformableMaterialDensity",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_MATERIAL_ELASTICITY_DAMPING,
     "openUsdPhysics:volumeDeformableMaterial:elasticityDamping", nullptr,
     "volumeDeformableMaterialElasticityDamping", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_MATERIAL_DAMPING,
     "openUsdPhysics:volumeDeformableMaterial:damping", nullptr, "volumeDeformableMaterialDamping",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},
    {OPENUSD_PHYSICS_EXTRACT_KEY_DEFORMABLE_MATERIAL_DAMPING_SCALE,
     "openUsdPhysics:volumeDeformableMaterial:dampingScale", nullptr,
     "volumeDeformableMaterialDampingScale", OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, false},

    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_SIMULATION_OWNER, nullptr, "physics:simulationOwner",
     "simulationOwner", kSimulationOwnerDomains, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_BODY0, nullptr, "physics:body0", "body0",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_JOINT, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_BODY1, nullptr, "physics:body1", "body1",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_JOINT, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_FILTERED_PAIRS,
     "openUsdPhysics:collisionFilter:filteredPairs", "physics:filteredPairs", "filteredPairs",
     kFilteringDomains, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_FILTERED_GROUPS,
     "openUsdPhysics:collisionFilter:filterGroups", "physics:filteredGroups",
     "filteredGroups", kFilteringDomains, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_MATERIAL_BINDING, nullptr, "material:binding:physics",
     "materialBindingPhysics", kMaterialBindingDomains, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_COLLIDERS, nullptr, "collection:colliders:includes",
     "collidersIncludes", kFilteringDomains, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_ATTACHMENT_ACTOR0,
     "openUsdPhysics:attachment:actor0", nullptr, "actor0",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_ATTACHMENT, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_ATTACHMENT_ACTOR1,
     "openUsdPhysics:attachment:actor1", nullptr, "actor1",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_ATTACHMENT, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_ARTICULATION,
     "openUsdPhysics:fixedTendon:articulation", nullptr, "articulation",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_ARTICULATION,
     "openUsdPhysics:spatialTendon:articulation", nullptr, "spatialTendonArticulation",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_TENDON_ROOT_JOINT,
     "openUsdPhysics:fixedTendon:rootJoint", nullptr, "tendonRootJoint",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_TENDON_JOINTS, "openUsdPhysics:fixedTendon:joints",
     nullptr, "tendonJoints", OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_TENDON_ROOT_ATTACHMENT,
     "openUsdPhysics:spatialTendon:rootAttachment", nullptr, "tendonRootAttachment",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_ATTACHMENT_TENDON,
     "openUsdPhysics:tendonAttachment:tendon", nullptr, "tendonAttachmentTendon",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_ATTACHMENT_PARENT,
     "openUsdPhysics:tendonAttachment:parentAttachment", nullptr,
     "tendonAttachmentParentAttachment", OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_MIMIC_REFERENCE_JOINT,
     "openUsdPhysics:mimicJoint:referenceJoint", nullptr, "mimicReferenceJoint",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_MIMIC, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_PARTICLE_SYSTEM,
     "openUsdPhysics:particleSet:particleSystem", nullptr, "particleSystem",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_PARTICLE_SYSTEM,
     "openUsdPhysics:particleCloth:particleSystem", nullptr, "clothParticleSystem",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_DEFORMABLE_MATERIAL,
     "openUsdPhysics:particleSet:material", nullptr, "particleSetMaterial",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_DEFORMABLE_MATERIAL,
     "openUsdPhysics:particleCloth:material", nullptr, "particleClothMaterial",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_DEFORMABLE_MATERIAL,
     "openUsdPhysics:surfaceDeformable:material", nullptr, "surfaceDeformableMaterial",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_DEFORMABLE_MATERIAL,
     "openUsdPhysics:volumeDeformable:material", nullptr, "volumeDeformableMaterial",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_SIMULATION_OWNER,
     "openUsdPhysics:particleSystem:simulationOwner", nullptr, "particleSystemSimulationOwner",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_SIMULATION_OWNER,
     "openUsdPhysics:surfaceDeformable:simulationOwner", nullptr, "surfaceDeformableSimulationOwner",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, true},
    {OPENUSD_PHYSICS_EXTRACT_KEY_REL_SIMULATION_OWNER,
     "openUsdPhysics:volumeDeformable:simulationOwner", nullptr, "volumeDeformableSimulationOwner",
     OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE, true},
};

constexpr size_t kCanonicalEntryCount = sizeof(kCanonicalEntries) / sizeof(kCanonicalEntries[0]);

// Multiple apply drive and limit properties carry an instance name that the canonical table
// cannot spell. They are matched by prefix and leaf instead, and the authored name is kept so
// a consumer can tell the instances apart.
struct MultiApplyEntry
{
    const char* prefix;
    const char* leaf;
    uint32_t key;
};

const MultiApplyEntry kMultiApplyEntries[] = {
    {"drive:", "stiffness", OPENUSD_PHYSICS_EXTRACT_KEY_DRIVE_STIFFNESS},
    {"drive:", "damping", OPENUSD_PHYSICS_EXTRACT_KEY_DRIVE_DAMPING},
    {"drive:", "maxForce", OPENUSD_PHYSICS_EXTRACT_KEY_DRIVE_MAX_FORCE},
    {"drive:", "targetPosition", OPENUSD_PHYSICS_EXTRACT_KEY_DRIVE_TARGET_POSITION},
    {"drive:", "targetVelocity", OPENUSD_PHYSICS_EXTRACT_KEY_DRIVE_TARGET_VELOCITY},
    {"drive:", "type", OPENUSD_PHYSICS_EXTRACT_KEY_DRIVE_TYPE},
    {"limit:", "low", OPENUSD_PHYSICS_EXTRACT_KEY_LIMIT_LOW},
    {"limit:", "high", OPENUSD_PHYSICS_EXTRACT_KEY_LIMIT_HIGH},
};

constexpr size_t kMultiApplyEntryCount =
    sizeof(kMultiApplyEntries) / sizeof(kMultiApplyEntries[0]);

// ---------------------------------------------------------------------------------------
// Schema recognition.
// ---------------------------------------------------------------------------------------

struct SchemaMapping
{
    const char* schema;
    uint32_t objectType;
};

// Concrete prim types that declare a physics object.
const SchemaMapping kTypeMappings[] = {
    {"PhysicsScene", OPENUSD_PHYSICS_EXTRACT_OBJECT_SCENE},
    {"PhysicsCollisionGroup", OPENUSD_PHYSICS_EXTRACT_OBJECT_COLLISION_GROUP},
    {"PhysicsJoint", OPENUSD_PHYSICS_EXTRACT_OBJECT_JOINT},
    {"PhysicsFixedJoint", OPENUSD_PHYSICS_EXTRACT_OBJECT_JOINT},
    {"PhysicsRevoluteJoint", OPENUSD_PHYSICS_EXTRACT_OBJECT_JOINT},
    {"PhysicsPrismaticJoint", OPENUSD_PHYSICS_EXTRACT_OBJECT_JOINT},
    {"PhysicsSphericalJoint", OPENUSD_PHYSICS_EXTRACT_OBJECT_JOINT},
    {"PhysicsDistanceJoint", OPENUSD_PHYSICS_EXTRACT_OBJECT_JOINT},
    {"OpenUsdPhysicsVehicleTireFrictionTable",
     OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE_FRICTION_TABLE},
    {"OpenUsdPhysicsFixedTendon", OPENUSD_PHYSICS_EXTRACT_OBJECT_FIXED_TENDON},
    {"OpenUsdPhysicsSpatialTendon", OPENUSD_PHYSICS_EXTRACT_OBJECT_SPATIAL_TENDON},
    {"OpenUsdPhysicsAttachment", OPENUSD_PHYSICS_EXTRACT_OBJECT_ATTACHMENT},
};

// Applied API schemas that declare a physics object. Multiple-apply instance suffixes are
// stripped before the lookup.
const SchemaMapping kApiMappings[] = {
    {"PhysicsRigidBodyAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_RIGID_BODY},
    {"PhysicsMassAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_RIGID_BODY},
    {"OpenUsdPhysicsRigidBodySettingsAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_RIGID_BODY},
    {"PhysicsCollisionAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_COLLIDER},
    {"PhysicsMeshCollisionAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_COLLIDER},
    {"OpenUsdPhysicsCollisionSettingsAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_COLLIDER},
    {"PhysicsMaterialAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_MATERIAL},
    {"OpenUsdPhysicsMaterialSettingsAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_MATERIAL},
    {"PhysicsArticulationRootAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_ARTICULATION_ROOT},
    {"OpenUsdPhysicsArticulationSettingsAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_ARTICULATION_ROOT},
    {"PhysicsFilteredPairsAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_FILTERED_PAIRS},
    {"PhysicsDriveAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_JOINT},
    {"PhysicsLimitAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_JOINT},
    {"OpenUsdPhysicsSceneAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_SCENE},
    {"OpenUsdPhysicsSimulationMetadataAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_SIMULATION_METADATA},
    {"OpenUsdPhysicsCharacterControllerAPI",
     OPENUSD_PHYSICS_EXTRACT_OBJECT_CHARACTER_CONTROLLER},
    {"OpenUsdPhysicsVehicleAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE},
    {"OpenUsdPhysicsVehicleEngineAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE},
    {"OpenUsdPhysicsVehicleGearsAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE},
    {"OpenUsdPhysicsVehicleAutoGearBoxAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE},
    {"OpenUsdPhysicsVehicleClutchAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE},
    {"OpenUsdPhysicsVehicleDifferentialAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE},
    {"OpenUsdPhysicsVehicleBrakesAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE},
    {"OpenUsdPhysicsVehicleSteeringAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE},
    {"OpenUsdPhysicsVehicleWheelAttachmentAPI",
     OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE_WHEEL_ATTACHMENT},
    {"OpenUsdPhysicsVehicleWheelAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE_WHEEL_ATTACHMENT},
    {"OpenUsdPhysicsVehicleSuspensionAPI",
     OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE_WHEEL_ATTACHMENT},
    {"OpenUsdPhysicsVehicleTireAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE_WHEEL_ATTACHMENT},
    {"OpenUsdPhysicsTendonAttachmentAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_TENDON_ATTACHMENT},
    {"OpenUsdPhysicsMimicJointAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_MIMIC_JOINT},
    {"OpenUsdPhysicsParticleSystemAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_PARTICLE_SYSTEM},
    {"OpenUsdPhysicsParticleAnisotropyAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_PARTICLE_SYSTEM},
    {"OpenUsdPhysicsParticleSmoothingAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_PARTICLE_SYSTEM},
    {"OpenUsdPhysicsParticleIsosurfaceAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_PARTICLE_SYSTEM},
    {"OpenUsdPhysicsParticleSetAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_PARTICLE_SET},
    {"OpenUsdPhysicsParticleClothAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_PARTICLE_CLOTH},
    {"OpenUsdPhysicsDiffuseParticlesAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_DIFFUSE_PARTICLES},
    {"OpenUsdPhysicsPbdMaterialAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_PBD_MATERIAL},
    {"OpenUsdPhysicsSurfaceDeformableAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_SURFACE_DEFORMABLE},
    {"OpenUsdPhysicsSurfaceDeformableMaterialAPI",
     OPENUSD_PHYSICS_EXTRACT_OBJECT_SURFACE_DEFORMABLE_MATERIAL},
    {"OpenUsdPhysicsVolumeDeformableAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_VOLUME_DEFORMABLE},
    {"OpenUsdPhysicsVolumeDeformableMaterialAPI",
     OPENUSD_PHYSICS_EXTRACT_OBJECT_VOLUME_DEFORMABLE_MATERIAL},
    {"OpenUsdPhysicsAutoAttachmentAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_ATTACHMENT},
    {"OpenUsdPhysicsCollisionFilterSettingsAPI",
     OPENUSD_PHYSICS_EXTRACT_OBJECT_COLLISION_FILTER},
    {"OpenUsdPhysicsCookedDataAPI", OPENUSD_PHYSICS_EXTRACT_OBJECT_COOKED_DATA},
};

// openUsdPhysics property group to owning object type. Drives assignment of properties
// that carry no canonical key so every authored opinion lands on a stable object.
const SchemaMapping kGroupMappings[] = {
    {"scene", OPENUSD_PHYSICS_EXTRACT_OBJECT_SCENE},
    {"body", OPENUSD_PHYSICS_EXTRACT_OBJECT_RIGID_BODY},
    {"collision", OPENUSD_PHYSICS_EXTRACT_OBJECT_COLLIDER},
    {"material", OPENUSD_PHYSICS_EXTRACT_OBJECT_MATERIAL},
    {"articulation", OPENUSD_PHYSICS_EXTRACT_OBJECT_ARTICULATION_ROOT},
    {"simulation", OPENUSD_PHYSICS_EXTRACT_OBJECT_SIMULATION_METADATA},
    {"controller", OPENUSD_PHYSICS_EXTRACT_OBJECT_CHARACTER_CONTROLLER},
    {"vehicle", OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE},
    {"engine", OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE},
    {"gears", OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE},
    {"autoGearBox", OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE},
    {"clutch", OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE},
    {"differential", OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE},
    {"brakes", OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE},
    {"steering", OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE},
    {"wheelAttachment", OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE_WHEEL_ATTACHMENT},
    {"wheel", OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE_WHEEL_ATTACHMENT},
    {"suspension", OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE_WHEEL_ATTACHMENT},
    {"tire", OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE_WHEEL_ATTACHMENT},
    {"frictionTable", OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE_FRICTION_TABLE},
    {"fixedTendon", OPENUSD_PHYSICS_EXTRACT_OBJECT_FIXED_TENDON},
    {"spatialTendon", OPENUSD_PHYSICS_EXTRACT_OBJECT_SPATIAL_TENDON},
    {"tendonAttachment", OPENUSD_PHYSICS_EXTRACT_OBJECT_TENDON_ATTACHMENT},
    {"mimicJoint", OPENUSD_PHYSICS_EXTRACT_OBJECT_MIMIC_JOINT},
    {"particleSystem", OPENUSD_PHYSICS_EXTRACT_OBJECT_PARTICLE_SYSTEM},
    {"particleAnisotropy", OPENUSD_PHYSICS_EXTRACT_OBJECT_PARTICLE_SYSTEM},
    {"particleSmoothing", OPENUSD_PHYSICS_EXTRACT_OBJECT_PARTICLE_SYSTEM},
    {"isosurface", OPENUSD_PHYSICS_EXTRACT_OBJECT_PARTICLE_SYSTEM},
    {"particleSet", OPENUSD_PHYSICS_EXTRACT_OBJECT_PARTICLE_SET},
    {"particleCloth", OPENUSD_PHYSICS_EXTRACT_OBJECT_PARTICLE_CLOTH},
    {"diffuseParticles", OPENUSD_PHYSICS_EXTRACT_OBJECT_DIFFUSE_PARTICLES},
    {"pbdMaterial", OPENUSD_PHYSICS_EXTRACT_OBJECT_PBD_MATERIAL},
    {"surfaceDeformable", OPENUSD_PHYSICS_EXTRACT_OBJECT_SURFACE_DEFORMABLE},
    {"surfaceDeformableMaterial",
     OPENUSD_PHYSICS_EXTRACT_OBJECT_SURFACE_DEFORMABLE_MATERIAL},
    {"volumeDeformable", OPENUSD_PHYSICS_EXTRACT_OBJECT_VOLUME_DEFORMABLE},
    {"volumeDeformableMaterial", OPENUSD_PHYSICS_EXTRACT_OBJECT_VOLUME_DEFORMABLE_MATERIAL},
    {"attachment", OPENUSD_PHYSICS_EXTRACT_OBJECT_ATTACHMENT},
    {"autoAttachment", OPENUSD_PHYSICS_EXTRACT_OBJECT_ATTACHMENT},
    {"collisionFilter", OPENUSD_PHYSICS_EXTRACT_OBJECT_COLLISION_FILTER},
    {"cookedData", OPENUSD_PHYSICS_EXTRACT_OBJECT_COOKED_DATA},
};

uint32_t DomainForObjectType(uint32_t objectType) noexcept
{
    switch (objectType)
    {
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_SCENE:
        return OPENUSD_PHYSICS_EXTRACT_DOMAIN_SCENE;
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_RIGID_BODY:
        return OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY;
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_COLLIDER:
        return OPENUSD_PHYSICS_EXTRACT_DOMAIN_COLLISION;
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_MATERIAL:
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_PBD_MATERIAL:
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_SURFACE_DEFORMABLE_MATERIAL:
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_VOLUME_DEFORMABLE_MATERIAL:
        return OPENUSD_PHYSICS_EXTRACT_DOMAIN_MATERIAL;
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_JOINT:
        return OPENUSD_PHYSICS_EXTRACT_DOMAIN_JOINT;
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_ARTICULATION_ROOT:
        return OPENUSD_PHYSICS_EXTRACT_DOMAIN_ARTICULATION;
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_COLLISION_GROUP:
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_FILTERED_PAIRS:
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_COLLISION_FILTER:
        return OPENUSD_PHYSICS_EXTRACT_DOMAIN_FILTERING;
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_CHARACTER_CONTROLLER:
        return OPENUSD_PHYSICS_EXTRACT_DOMAIN_CONTROLLER;
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE:
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE_WHEEL_ATTACHMENT:
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_VEHICLE_FRICTION_TABLE:
        return OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE;
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_FIXED_TENDON:
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_SPATIAL_TENDON:
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_TENDON_ATTACHMENT:
        return OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON;
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_MIMIC_JOINT:
        return OPENUSD_PHYSICS_EXTRACT_DOMAIN_MIMIC;
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_PARTICLE_SYSTEM:
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_PARTICLE_SET:
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_PARTICLE_CLOTH:
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_DIFFUSE_PARTICLES:
        return OPENUSD_PHYSICS_EXTRACT_DOMAIN_PARTICLE;
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_SURFACE_DEFORMABLE:
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_VOLUME_DEFORMABLE:
        return OPENUSD_PHYSICS_EXTRACT_DOMAIN_DEFORMABLE;
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_ATTACHMENT:
        return OPENUSD_PHYSICS_EXTRACT_DOMAIN_ATTACHMENT;
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_COOKED_DATA:
        return OPENUSD_PHYSICS_EXTRACT_DOMAIN_COOKED_DATA;
    case OPENUSD_PHYSICS_EXTRACT_OBJECT_SIMULATION_METADATA:
        return OPENUSD_PHYSICS_EXTRACT_DOMAIN_SIMULATION_METADATA;
    default:
        return OPENUSD_PHYSICS_EXTRACT_DOMAIN_NONE;
    }
}

// Domains the retained simulation build page can consume today. Everything else is
// extracted, reported once per object and left for the domain specific work items.
constexpr uint32_t kSimulatedDomains =
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_SCENE | OPENUSD_PHYSICS_EXTRACT_DOMAIN_RIGID_BODY |
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_COLLISION | OPENUSD_PHYSICS_EXTRACT_DOMAIN_MATERIAL |
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_JOINT | OPENUSD_PHYSICS_EXTRACT_DOMAIN_ARTICULATION |
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_TENDON | OPENUSD_PHYSICS_EXTRACT_DOMAIN_MIMIC |
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_VEHICLE |
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_CONTROLLER |
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_FILTERING |
    OPENUSD_PHYSICS_EXTRACT_DOMAIN_SIMULATION_METADATA;

std::string_view LastNamespaceComponent(std::string_view name) noexcept
{
    const size_t separator = name.rfind(':');
    return separator == std::string_view::npos ? name : name.substr(separator + 1);
}

bool StartsWith(std::string_view value, std::string_view prefix) noexcept
{
    return value.size() >= prefix.size() && value.compare(0, prefix.size(), prefix) == 0;
}

// Multiple apply schema names carry an instance suffix after a colon.
std::string_view SchemaFamily(std::string_view name) noexcept
{
    const size_t separator = name.find(':');
    return separator == std::string_view::npos ? name : name.substr(0, separator);
}

uint32_t ObjectTypeForType(std::string_view typeName) noexcept
{
    for (const SchemaMapping& mapping : kTypeMappings)
    {
        if (typeName == mapping.schema)
        {
            return mapping.objectType;
        }
    }
    return OPENUSD_PHYSICS_EXTRACT_OBJECT_UNKNOWN;
}

uint32_t ObjectTypeForApi(std::string_view schemaName) noexcept
{
    const std::string_view family = SchemaFamily(schemaName);
    for (const SchemaMapping& mapping : kApiMappings)
    {
        if (family == mapping.schema)
        {
            return mapping.objectType;
        }
    }
    return OPENUSD_PHYSICS_EXTRACT_OBJECT_UNKNOWN;
}

uint32_t ObjectTypeForGroup(std::string_view group) noexcept
{
    for (const SchemaMapping& mapping : kGroupMappings)
    {
        if (group == mapping.schema)
        {
            return mapping.objectType;
        }
    }
    return OPENUSD_PHYSICS_EXTRACT_OBJECT_UNKNOWN;
}

// True for every property name that carries a physics opinion in one of the three
// recognized namespaces.
bool IsPhysicsPropertyName(std::string_view name) noexcept
{
    return StartsWith(name, "openUsdPhysics:") || StartsWith(name, "physics:") ||
        StartsWith(name, "physx") || StartsWith(name, "drive:") || StartsWith(name, "limit:") ||
        StartsWith(name, "material:binding:physics") ||
        StartsWith(name, "collection:colliders");
}

// ---------------------------------------------------------------------------------------
// Value conversion.
// ---------------------------------------------------------------------------------------

template <typename TArray>
void AppendRealArray(const VtValue& value, PropertyData& property, uint32_t valueType)
{
    const TArray& array = value.UncheckedGet<TArray>();
    property.valueType = valueType;
    property.numbers.reserve(array.size());
    for (const auto& element : array)
    {
        property.numbers.push_back(static_cast<double>(element));
    }
}

template <typename TArray, size_t TComponents>
void AppendVectorArray(const VtValue& value, PropertyData& property, uint32_t valueType)
{
    const TArray& array = value.UncheckedGet<TArray>();
    property.valueType = valueType;
    property.numbers.reserve(array.size() * TComponents);
    for (const auto& element : array)
    {
        for (size_t component = 0; component < TComponents; ++component)
        {
            property.numbers.push_back(static_cast<double>(element[component]));
        }
    }
}

template <typename TArray>
void AppendTextArray(const VtValue& value, PropertyData& property)
{
    const TArray& array = value.UncheckedGet<TArray>();
    property.valueType = OPENUSD_PHYSICS_EXTRACT_VALUE_TEXT_ARRAY;
    property.texts.reserve(array.size());
    for (const auto& element : array)
    {
        property.texts.emplace_back(TfStringify(element));
    }
}

bool ConvertValue(const VtValue& value, PropertyData& property)
{
    if (value.IsEmpty())
    {
        property.valueType = OPENUSD_PHYSICS_EXTRACT_VALUE_NONE;
        return true;
    }
    if (value.IsHolding<bool>())
    {
        property.valueType = OPENUSD_PHYSICS_EXTRACT_VALUE_BOOL;
        property.scalar = value.UncheckedGet<bool>() ? 1.0 : 0.0;
        return true;
    }
    if (value.IsHolding<int>())
    {
        property.valueType = OPENUSD_PHYSICS_EXTRACT_VALUE_INTEGER;
        property.scalar = static_cast<double>(value.UncheckedGet<int>());
        return true;
    }
    if (value.IsHolding<unsigned int>())
    {
        property.valueType = OPENUSD_PHYSICS_EXTRACT_VALUE_INTEGER;
        property.scalar = static_cast<double>(value.UncheckedGet<unsigned int>());
        return true;
    }
    if (value.IsHolding<int64_t>())
    {
        property.valueType = OPENUSD_PHYSICS_EXTRACT_VALUE_INTEGER;
        property.scalar = static_cast<double>(value.UncheckedGet<int64_t>());
        return true;
    }
    if (value.IsHolding<uint64_t>())
    {
        property.valueType = OPENUSD_PHYSICS_EXTRACT_VALUE_INTEGER;
        property.scalar = static_cast<double>(value.UncheckedGet<uint64_t>());
        return true;
    }
    if (value.IsHolding<float>())
    {
        property.valueType = OPENUSD_PHYSICS_EXTRACT_VALUE_REAL;
        property.scalar = static_cast<double>(value.UncheckedGet<float>());
        return true;
    }
    if (value.IsHolding<double>())
    {
        property.valueType = OPENUSD_PHYSICS_EXTRACT_VALUE_REAL;
        property.scalar = value.UncheckedGet<double>();
        return true;
    }
    if (value.IsHolding<GfHalf>())
    {
        property.valueType = OPENUSD_PHYSICS_EXTRACT_VALUE_REAL;
        property.scalar = static_cast<double>(static_cast<float>(value.UncheckedGet<GfHalf>()));
        return true;
    }
    if (value.IsHolding<GfVec2f>() || value.IsHolding<GfVec2d>() || value.IsHolding<GfVec2i>())
    {
        property.valueType = OPENUSD_PHYSICS_EXTRACT_VALUE_VEC2;
        if (value.IsHolding<GfVec2f>())
        {
            const GfVec2f vector = value.UncheckedGet<GfVec2f>();
            property.numbers = {static_cast<double>(vector[0]), static_cast<double>(vector[1])};
        }
        else if (value.IsHolding<GfVec2d>())
        {
            const GfVec2d vector = value.UncheckedGet<GfVec2d>();
            property.numbers = {vector[0], vector[1]};
        }
        else
        {
            const GfVec2i vector = value.UncheckedGet<GfVec2i>();
            property.numbers = {static_cast<double>(vector[0]), static_cast<double>(vector[1])};
        }
        return true;
    }
    if (value.IsHolding<GfVec3f>() || value.IsHolding<GfVec3d>() || value.IsHolding<GfVec3i>())
    {
        property.valueType = OPENUSD_PHYSICS_EXTRACT_VALUE_VEC3;
        if (value.IsHolding<GfVec3f>())
        {
            const GfVec3f vector = value.UncheckedGet<GfVec3f>();
            property.numbers = {
                static_cast<double>(vector[0]),
                static_cast<double>(vector[1]),
                static_cast<double>(vector[2])};
        }
        else if (value.IsHolding<GfVec3d>())
        {
            const GfVec3d vector = value.UncheckedGet<GfVec3d>();
            property.numbers = {vector[0], vector[1], vector[2]};
        }
        else
        {
            const GfVec3i vector = value.UncheckedGet<GfVec3i>();
            property.numbers = {
                static_cast<double>(vector[0]),
                static_cast<double>(vector[1]),
                static_cast<double>(vector[2])};
        }
        return true;
    }
    if (value.IsHolding<GfVec4f>() || value.IsHolding<GfVec4d>())
    {
        property.valueType = OPENUSD_PHYSICS_EXTRACT_VALUE_VEC4;
        if (value.IsHolding<GfVec4f>())
        {
            const GfVec4f vector = value.UncheckedGet<GfVec4f>();
            property.numbers = {
                static_cast<double>(vector[0]),
                static_cast<double>(vector[1]),
                static_cast<double>(vector[2]),
                static_cast<double>(vector[3])};
        }
        else
        {
            const GfVec4d vector = value.UncheckedGet<GfVec4d>();
            property.numbers = {vector[0], vector[1], vector[2], vector[3]};
        }
        return true;
    }
    if (value.IsHolding<GfQuatf>() || value.IsHolding<GfQuatd>())
    {
        property.valueType = OPENUSD_PHYSICS_EXTRACT_VALUE_QUAT;
        if (value.IsHolding<GfQuatf>())
        {
            const GfQuatf quaternion = value.UncheckedGet<GfQuatf>();
            const GfVec3f imaginary = quaternion.GetImaginary();
            property.numbers = {
                static_cast<double>(quaternion.GetReal()),
                static_cast<double>(imaginary[0]),
                static_cast<double>(imaginary[1]),
                static_cast<double>(imaginary[2])};
        }
        else
        {
            const GfQuatd quaternion = value.UncheckedGet<GfQuatd>();
            const GfVec3d imaginary = quaternion.GetImaginary();
            property.numbers = {
                quaternion.GetReal(), imaginary[0], imaginary[1], imaginary[2]};
        }
        return true;
    }
    if (value.IsHolding<GfMatrix4d>())
    {
        property.valueType = OPENUSD_PHYSICS_EXTRACT_VALUE_MATRIX4;
        const GfMatrix4d matrix = value.UncheckedGet<GfMatrix4d>();
        property.numbers.reserve(16);
        for (int row = 0; row < 4; ++row)
        {
            for (int column = 0; column < 4; ++column)
            {
                property.numbers.push_back(matrix[row][column]);
            }
        }
        return true;
    }
    if (value.IsHolding<TfToken>())
    {
        property.valueType = OPENUSD_PHYSICS_EXTRACT_VALUE_TEXT;
        property.texts.emplace_back(value.UncheckedGet<TfToken>().GetString());
        return true;
    }
    if (value.IsHolding<std::string>())
    {
        property.valueType = OPENUSD_PHYSICS_EXTRACT_VALUE_TEXT;
        property.texts.emplace_back(value.UncheckedGet<std::string>());
        return true;
    }
    if (value.IsHolding<SdfAssetPath>())
    {
        property.valueType = OPENUSD_PHYSICS_EXTRACT_VALUE_TEXT;
        property.texts.emplace_back(value.UncheckedGet<SdfAssetPath>().GetAssetPath());
        return true;
    }
    if (value.IsHolding<VtDoubleArray>())
    {
        AppendRealArray<VtDoubleArray>(
            value, property, OPENUSD_PHYSICS_EXTRACT_VALUE_REAL_ARRAY);
        return true;
    }
    if (value.IsHolding<VtFloatArray>())
    {
        AppendRealArray<VtFloatArray>(
            value, property, OPENUSD_PHYSICS_EXTRACT_VALUE_REAL_ARRAY);
        return true;
    }
    if (value.IsHolding<VtIntArray>())
    {
        AppendRealArray<VtIntArray>(
            value, property, OPENUSD_PHYSICS_EXTRACT_VALUE_INTEGER_ARRAY);
        return true;
    }
    if (value.IsHolding<VtInt64Array>())
    {
        AppendRealArray<VtInt64Array>(
            value, property, OPENUSD_PHYSICS_EXTRACT_VALUE_INTEGER_ARRAY);
        return true;
    }
    if (value.IsHolding<VtUIntArray>())
    {
        AppendRealArray<VtUIntArray>(
            value, property, OPENUSD_PHYSICS_EXTRACT_VALUE_INTEGER_ARRAY);
        return true;
    }
    if (value.IsHolding<VtBoolArray>())
    {
        AppendRealArray<VtBoolArray>(
            value, property, OPENUSD_PHYSICS_EXTRACT_VALUE_INTEGER_ARRAY);
        return true;
    }
    if (value.IsHolding<VtVec2fArray>())
    {
        AppendVectorArray<VtVec2fArray, 2>(
            value, property, OPENUSD_PHYSICS_EXTRACT_VALUE_VEC2_ARRAY);
        return true;
    }
    if (value.IsHolding<VtVec3fArray>())
    {
        AppendVectorArray<VtVec3fArray, 3>(
            value, property, OPENUSD_PHYSICS_EXTRACT_VALUE_VEC3_ARRAY);
        return true;
    }
    if (value.IsHolding<VtVec3dArray>())
    {
        AppendVectorArray<VtVec3dArray, 3>(
            value, property, OPENUSD_PHYSICS_EXTRACT_VALUE_VEC3_ARRAY);
        return true;
    }
    if (value.IsHolding<VtTokenArray>())
    {
        AppendTextArray<VtTokenArray>(value, property);
        return true;
    }
    if (value.IsHolding<VtStringArray>())
    {
        AppendTextArray<VtStringArray>(value, property);
        return true;
    }
    property.valueType = OPENUSD_PHYSICS_EXTRACT_VALUE_NONE;
    return false;
}

// ---------------------------------------------------------------------------------------
// Extraction.
// ---------------------------------------------------------------------------------------

struct ExtractLimits
{
    uint32_t objects = OPENUSD_PHYSICS_EXTRACT_MAX_OBJECTS;
    uint32_t properties = OPENUSD_PHYSICS_EXTRACT_MAX_PROPERTIES;
    uint32_t relationships = OPENUSD_PHYSICS_EXTRACT_MAX_RELATIONSHIPS;
    uint32_t targets = OPENUSD_PHYSICS_EXTRACT_MAX_TARGETS;
    uint32_t numbers = OPENUSD_PHYSICS_EXTRACT_MAX_NUMBERS;
    uint32_t texts = OPENUSD_PHYSICS_EXTRACT_MAX_TEXTS;
    uint32_t points = OPENUSD_PHYSICS_EXTRACT_MAX_POINTS;
    uint32_t indices = OPENUSD_PHYSICS_EXTRACT_MAX_INDICES;
    uint32_t diagnostics = OPENUSD_PHYSICS_EXTRACT_MAX_DIAGNOSTICS;
    uint32_t stringBytes = OPENUSD_PHYSICS_EXTRACT_MAX_STRING_BYTES;
};

uint32_t ClampLimit(uint32_t requested, uint32_t hardMaximum) noexcept
{
    if (requested == 0 || requested > hardMaximum)
    {
        return hardMaximum;
    }
    return requested;
}

class Extractor
{
public:
    Extractor(UsdStageRefPtr stage, const openusd_physics_extract_options& options)
        : stage_(std::move(stage))
        , options_(options)
    {
        limits_.objects = ClampLimit(options.max_objects, OPENUSD_PHYSICS_EXTRACT_MAX_OBJECTS);
        limits_.properties =
            ClampLimit(options.max_properties, OPENUSD_PHYSICS_EXTRACT_MAX_PROPERTIES);
        limits_.relationships =
            ClampLimit(options.max_relationships, OPENUSD_PHYSICS_EXTRACT_MAX_RELATIONSHIPS);
        limits_.targets = ClampLimit(options.max_targets, OPENUSD_PHYSICS_EXTRACT_MAX_TARGETS);
        limits_.numbers = ClampLimit(options.max_numbers, OPENUSD_PHYSICS_EXTRACT_MAX_NUMBERS);
        limits_.texts = ClampLimit(options.max_texts, OPENUSD_PHYSICS_EXTRACT_MAX_TEXTS);
        limits_.points = ClampLimit(options.max_points, OPENUSD_PHYSICS_EXTRACT_MAX_POINTS);
        limits_.indices = ClampLimit(options.max_indices, OPENUSD_PHYSICS_EXTRACT_MAX_INDICES);
        limits_.diagnostics =
            ClampLimit(options.max_diagnostics, OPENUSD_PHYSICS_EXTRACT_MAX_DIAGNOSTICS);
        limits_.stringBytes =
            ClampLimit(options.max_string_bytes, OPENUSD_PHYSICS_EXTRACT_MAX_STRING_BYTES);
        strings_.SetMaxBytes(limits_.stringBytes);
    }

    // The only method that touches the stage. Everything after it runs on collected data.
    void Traverse();

    std::vector<unsigned char> Serialize();

private:
    struct BodyFrame
    {
        SdfPath path;
        int32_t index;
    };

    struct ObjectFrame
    {
        SdfPath path;
        uint64_t id;
    };

    void ReadStageMetadata();
    void VisitPrim(const UsdPrim& prim);
    void ResolveReferences();
    void AddDiagnostic(
        uint32_t severity,
        uint32_t category,
        uint32_t code,
        int32_t objectIndex,
        uint32_t key,
        std::string message);
    void CollectProperties(
        const UsdPrim& prim,
        const std::vector<std::string>& authored,
        std::map<std::string, std::vector<std::string>>& foreignByLeaf,
        std::unordered_set<std::string>& consumed,
        ObjectData& object,
        int32_t objectIndex);
    void CollectMultiApply(
        const UsdPrim& prim,
        const std::vector<std::string>& authored,
        std::unordered_set<std::string>& consumed,
        ObjectData& object,
        int32_t objectIndex);
    void CollectUnmapped(
        const UsdPrim& prim,
        const std::vector<std::string>& authored,
        const std::unordered_set<std::string>& consumed,
        const std::vector<int32_t>& primObjects);
    void ReadProperty(
        const UsdPrim& prim,
        const std::string& name,
        bool relationship,
        uint32_t key,
        uint32_t source,
        uint32_t flags,
        ObjectData& object,
        int32_t objectIndex,
        bool hidden);
    void CollectGeometry(const UsdPrim& prim, ObjectData& object, int32_t objectIndex);
    void ApplySpaceTransform(const UsdPrim& prim, ObjectData& object);

    UsdStageRefPtr stage_;
    openusd_physics_extract_options options_{};
    ExtractLimits limits_{};
    UsdTimeCode time_ = UsdTimeCode::Default();

    double metersPerUnit_ = 0.01;
    double kilogramsPerUnit_ = 1.0;
    double timeCodesPerSecond_ = 24.0;
    double startTimeCode_ = 0.0;
    double endTimeCode_ = 0.0;
    uint32_t upAxis_ = OPENUSD_PHYSICS_EXTRACT_UP_AXIS_Y;
    GfQuatd upRotation_ = GfQuatd(1.0, 0.0, 0.0, 0.0);
    uint32_t pageFlags_ = OPENUSD_PHYSICS_EXTRACT_PAGE_FLAG_NONE;
    uint32_t truncationFlags_ = 0;
    int32_t defaultSceneIndex_ = -1;

    StringTable strings_;
    std::vector<ObjectData> objects_;
    std::vector<DiagnosticData> diagnostics_;
    std::vector<BodyFrame> bodyStack_;
    std::vector<ObjectFrame> objectStack_;
    std::unordered_map<std::string, int32_t> objectByPath_;
    std::unordered_map<std::string, int32_t> primaryByPath_;
    std::vector<int32_t> sceneIndices_;
    uint64_t propertyCount_ = 0;
    uint64_t relationshipCount_ = 0;
    uint64_t targetCount_ = 0;
    uint64_t numberCount_ = 0;
    uint64_t textCount_ = 0;
    uint64_t pointCount_ = 0;
    uint64_t indexCount_ = 0;
    UsdGeomXformCache xformCache_;
};

void Extractor::AddDiagnostic(
    uint32_t severity,
    uint32_t category,
    uint32_t code,
    int32_t objectIndex,
    uint32_t key,
    std::string message)
{
    if (diagnostics_.size() >= static_cast<size_t>(limits_.diagnostics))
    {
        truncationFlags_ |= 1u << 9;
        pageFlags_ |= OPENUSD_PHYSICS_EXTRACT_PAGE_FLAG_TRUNCATED;
        return;
    }
    if (message.size() >= OPENUSD_PHYSICS_EXTRACT_MESSAGE_BYTES)
    {
        message.resize(OPENUSD_PHYSICS_EXTRACT_MESSAGE_BYTES - 1);
    }
    DiagnosticData diagnostic;
    diagnostic.severity = severity;
    diagnostic.category = category;
    diagnostic.code = code;
    diagnostic.objectIndex = objectIndex;
    diagnostic.key = key;
    diagnostic.message = std::move(message);
    if (objectIndex >= 0 && static_cast<size_t>(objectIndex) < objects_.size())
    {
        diagnostic.objectId = objects_[static_cast<size_t>(objectIndex)].id;
        objects_[static_cast<size_t>(objectIndex)].diagnosticCount += 1u;
        if (severity == OPENUSD_PHYSICS_EXTRACT_SEVERITY_ERROR)
        {
            ObjectData& object = objects_[static_cast<size_t>(objectIndex)];
            object.flags &= ~static_cast<uint32_t>(OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_ENABLED);
            object.flags |= OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_DISABLED_BY_DIAGNOSTIC;
            pageFlags_ |= OPENUSD_PHYSICS_EXTRACT_PAGE_FLAG_HAS_DISABLED_OBJECTS;
        }
    }
    diagnostics_.push_back(std::move(diagnostic));
}

void Extractor::ReadStageMetadata()
{
    metersPerUnit_ = UsdGeomGetStageMetersPerUnit(stage_);
    if (!UsdGeomStageHasAuthoredMetersPerUnit(stage_))
    {
        pageFlags_ |= OPENUSD_PHYSICS_EXTRACT_PAGE_FLAG_METERS_FALLBACK;
        AddDiagnostic(
            OPENUSD_PHYSICS_EXTRACT_SEVERITY_INFORMATION,
            OPENUSD_PHYSICS_EXTRACT_CATEGORY_UNITS,
            OPENUSD_PHYSICS_EXTRACT_CODE_METERS_PER_UNIT_FALLBACK,
            -1,
            OPENUSD_PHYSICS_EXTRACT_KEY_UNMAPPED,
            "metersPerUnit is not authored; the standard fallback was used.");
    }
    kilogramsPerUnit_ = UsdPhysicsGetStageKilogramsPerUnit(stage_);
    if (!UsdPhysicsStageHasAuthoredKilogramsPerUnit(stage_))
    {
        pageFlags_ |= OPENUSD_PHYSICS_EXTRACT_PAGE_FLAG_KILOGRAMS_FALLBACK;
        AddDiagnostic(
            OPENUSD_PHYSICS_EXTRACT_SEVERITY_INFORMATION,
            OPENUSD_PHYSICS_EXTRACT_CATEGORY_UNITS,
            OPENUSD_PHYSICS_EXTRACT_CODE_KILOGRAMS_PER_UNIT_FALLBACK,
            -1,
            OPENUSD_PHYSICS_EXTRACT_KEY_UNMAPPED,
            "kilogramsPerUnit is not authored; the standard fallback of 1.0 was used.");
    }
    if (!std::isfinite(metersPerUnit_) || metersPerUnit_ <= 0.0 ||
        !std::isfinite(kilogramsPerUnit_) || kilogramsPerUnit_ <= 0.0)
    {
        AddDiagnostic(
            OPENUSD_PHYSICS_EXTRACT_SEVERITY_ERROR,
            OPENUSD_PHYSICS_EXTRACT_CATEGORY_UNITS,
            OPENUSD_PHYSICS_EXTRACT_CODE_NON_FINITE_UNITS,
            -1,
            OPENUSD_PHYSICS_EXTRACT_KEY_UNMAPPED,
            "Stage units are not positive finite numbers; standard fallbacks were used.");
        metersPerUnit_ = 0.01;
        kilogramsPerUnit_ = 1.0;
    }

    const TfToken upAxis = UsdGeomGetStageUpAxis(stage_);
    if (upAxis == UsdGeomTokens->x)
    {
        upAxis_ = OPENUSD_PHYSICS_EXTRACT_UP_AXIS_X;
        upRotation_ = GfQuatd(0.7071067811865476, GfVec3d(0.0, 0.0, 0.7071067811865476));
    }
    else if (upAxis == UsdGeomTokens->z)
    {
        upAxis_ = OPENUSD_PHYSICS_EXTRACT_UP_AXIS_Z;
        upRotation_ = GfQuatd(0.7071067811865476, GfVec3d(-0.7071067811865476, 0.0, 0.0));
    }
    else
    {
        upAxis_ = OPENUSD_PHYSICS_EXTRACT_UP_AXIS_Y;
        upRotation_ = GfQuatd(1.0, GfVec3d(0.0, 0.0, 0.0));
    }
    if (upAxis_ != OPENUSD_PHYSICS_EXTRACT_UP_AXIS_Y)
    {
        pageFlags_ |= OPENUSD_PHYSICS_EXTRACT_PAGE_FLAG_UP_AXIS_CONVERTED;
        AddDiagnostic(
            OPENUSD_PHYSICS_EXTRACT_SEVERITY_INFORMATION,
            OPENUSD_PHYSICS_EXTRACT_CATEGORY_UNITS,
            OPENUSD_PHYSICS_EXTRACT_CODE_UP_AXIS_CONVERTED,
            -1,
            OPENUSD_PHYSICS_EXTRACT_KEY_UNMAPPED,
            "Stage up axis was converted to the simulation Y up basis.");
    }

    timeCodesPerSecond_ = stage_->GetTimeCodesPerSecond();
    if (!stage_->GetRootLayer()->HasTimeCodesPerSecond())
    {
        pageFlags_ |= OPENUSD_PHYSICS_EXTRACT_PAGE_FLAG_TIME_CODES_FALLBACK;
        AddDiagnostic(
            OPENUSD_PHYSICS_EXTRACT_SEVERITY_INFORMATION,
            OPENUSD_PHYSICS_EXTRACT_CATEGORY_UNITS,
            OPENUSD_PHYSICS_EXTRACT_CODE_TIME_CODES_PER_SECOND_FALLBACK,
            -1,
            OPENUSD_PHYSICS_EXTRACT_KEY_UNMAPPED,
            "timeCodesPerSecond is not authored; the stage default was used.");
    }
    if (stage_->HasAuthoredTimeCodeRange())
    {
        startTimeCode_ = stage_->GetStartTimeCode();
        endTimeCode_ = stage_->GetEndTimeCode();
    }
    else
    {
        startTimeCode_ = 0.0;
        endTimeCode_ = 0.0;
        pageFlags_ |= OPENUSD_PHYSICS_EXTRACT_PAGE_FLAG_TIME_RANGE_FALLBACK;
        AddDiagnostic(
            OPENUSD_PHYSICS_EXTRACT_SEVERITY_INFORMATION,
            OPENUSD_PHYSICS_EXTRACT_CATEGORY_UNITS,
            OPENUSD_PHYSICS_EXTRACT_CODE_TIME_RANGE_FALLBACK,
            -1,
            OPENUSD_PHYSICS_EXTRACT_KEY_UNMAPPED,
            "No authored start or end time code was found.");
    }
    time_ = std::isfinite(options_.time_code) ? UsdTimeCode(options_.time_code)
                                             : UsdTimeCode::Default();
}

void Extractor::ApplySpaceTransform(const UsdPrim& prim, ObjectData& object)
{
    const GfMatrix4d local = xformCache_.GetLocalToWorldTransform(prim);
    const GfVec3d translation = local.ExtractTranslation();
    GfMatrix3d basis = local.ExtractRotationMatrix();
    GfVec3d scale(
        basis.GetRow(0).GetLength(), basis.GetRow(1).GetLength(), basis.GetRow(2).GetLength());
    if (local.GetDeterminant() < 0.0)
    {
        scale[0] = -scale[0];
    }
    for (size_t row = 0; row < 3; ++row)
    {
        const double length = scale[static_cast<int>(row)];
        if (std::abs(length) > 1e-12)
        {
            basis.SetRow(static_cast<int>(row), basis.GetRow(static_cast<int>(row)) / length);
        }
    }
    const GfQuatd rotation = basis.ExtractRotation().GetQuat().GetNormalized();

    // Positions move into simulation space: change of basis first, then unit scaling.
    const GfVec3d rotated = upRotation_.Transform(translation) * metersPerUnit_;
    const GfQuatd composed = (upRotation_ * rotation).GetNormalized();

    object.position[0] = rotated[0];
    object.position[1] = rotated[1];
    object.position[2] = rotated[2];
    object.rotation[0] = composed.GetReal();
    object.rotation[1] = composed.GetImaginary()[0];
    object.rotation[2] = composed.GetImaginary()[1];
    object.rotation[3] = composed.GetImaginary()[2];
    object.scale[0] = scale[0];
    object.scale[1] = scale[1];
    object.scale[2] = scale[2];
}

void Extractor::CollectGeometry(const UsdPrim& prim, ObjectData& object, int32_t objectIndex)
{
    const TfToken typeName = prim.GetTypeName();
    object.geometryAxis = 1u;
    if (typeName == UsdGeomTokens->Sphere)
    {
        object.geometry = OPENUSD_PHYSICS_EXTRACT_GEOMETRY_SPHERE;
        double radius = 1.0;
        UsdGeomSphere(prim).GetRadiusAttr().Get(&radius, time_);
        object.extent[0] = radius * metersPerUnit_;
    }
    else if (typeName == UsdGeomTokens->Cube)
    {
        object.geometry = OPENUSD_PHYSICS_EXTRACT_GEOMETRY_BOX;
        double size = 2.0;
        UsdGeomCube(prim).GetSizeAttr().Get(&size, time_);
        const double half = 0.5 * size * metersPerUnit_;
        object.extent[0] = half;
        object.extent[1] = half;
        object.extent[2] = half;
    }
    else if (typeName == UsdGeomTokens->Capsule || typeName == UsdGeomTokens->Cylinder ||
        typeName == UsdGeomTokens->Cone)
    {
        double radius = 1.0;
        double height = 2.0;
        TfToken axis = UsdGeomTokens->z;
        if (typeName == UsdGeomTokens->Capsule)
        {
            object.geometry = OPENUSD_PHYSICS_EXTRACT_GEOMETRY_CAPSULE;
            const UsdGeomCapsule capsule(prim);
            capsule.GetRadiusAttr().Get(&radius, time_);
            capsule.GetHeightAttr().Get(&height, time_);
            capsule.GetAxisAttr().Get(&axis, time_);
        }
        else if (typeName == UsdGeomTokens->Cylinder)
        {
            object.geometry = OPENUSD_PHYSICS_EXTRACT_GEOMETRY_CYLINDER;
            const UsdGeomCylinder cylinder(prim);
            cylinder.GetRadiusAttr().Get(&radius, time_);
            cylinder.GetHeightAttr().Get(&height, time_);
            cylinder.GetAxisAttr().Get(&axis, time_);
        }
        else
        {
            object.geometry = OPENUSD_PHYSICS_EXTRACT_GEOMETRY_CONE;
            const UsdGeomCone cone(prim);
            cone.GetRadiusAttr().Get(&radius, time_);
            cone.GetHeightAttr().Get(&height, time_);
            cone.GetAxisAttr().Get(&axis, time_);
        }
        object.extent[0] = radius * metersPerUnit_;
        object.extent[1] = 0.5 * height * metersPerUnit_;
        object.geometryAxis = axis == UsdGeomTokens->x ? 0u : (axis == UsdGeomTokens->y ? 1u : 2u);
    }
    else if (typeName == UsdGeomTokens->Plane)
    {
        object.geometry = OPENUSD_PHYSICS_EXTRACT_GEOMETRY_PLANE;
        TfToken axis = UsdGeomTokens->z;
        UsdGeomPlane(prim).GetAxisAttr().Get(&axis, time_);
        object.geometryAxis = axis == UsdGeomTokens->x ? 0u : (axis == UsdGeomTokens->y ? 1u : 2u);
    }
    else if (typeName == UsdGeomTokens->Mesh)
    {
        object.geometry = OPENUSD_PHYSICS_EXTRACT_GEOMETRY_MESH;
        TfToken approximation;
        const UsdAttribute approximationAttribute =
            prim.GetAttribute(UsdPhysicsTokens->physicsApproximation);
        if (approximationAttribute && approximationAttribute.Get(&approximation, time_))
        {
            if (approximation == UsdPhysicsTokens->convexHull ||
                approximation == UsdPhysicsTokens->convexDecomposition ||
                approximation == UsdPhysicsTokens->boundingCube ||
                approximation == UsdPhysicsTokens->boundingSphere)
            {
                object.geometry = OPENUSD_PHYSICS_EXTRACT_GEOMETRY_CONVEX_MESH;
            }
        }
        if ((options_.flags & OPENUSD_PHYSICS_EXTRACT_OPTION_INCLUDE_MESH_DATA) != 0)
        {
            VtVec3fArray points;
            VtIntArray faceCounts;
            VtIntArray faceIndices;
            const UsdGeomMesh mesh(prim);
            mesh.GetPointsAttr().Get(&points, time_);
            mesh.GetFaceVertexCountsAttr().Get(&faceCounts, time_);
            mesh.GetFaceVertexIndicesAttr().Get(&faceIndices, time_);
            if (pointCount_ + points.size() > limits_.points)
            {
                truncationFlags_ |= 1u << 7;
                pageFlags_ |= OPENUSD_PHYSICS_EXTRACT_PAGE_FLAG_TRUNCATED;
                AddDiagnostic(
                    OPENUSD_PHYSICS_EXTRACT_SEVERITY_WARNING,
                    OPENUSD_PHYSICS_EXTRACT_CATEGORY_CAPACITY,
                    OPENUSD_PHYSICS_EXTRACT_CODE_CAPACITY_EXCEEDED,
                    objectIndex,
                    OPENUSD_PHYSICS_EXTRACT_KEY_UNMAPPED,
                    "Collider point capacity was exceeded; mesh data was dropped.");
                return;
            }
            object.points.reserve(points.size() * 3);
            for (const GfVec3f& point : points)
            {
                object.points.push_back(point[0]);
                object.points.push_back(point[1]);
                object.points.push_back(point[2]);
            }
            pointCount_ += points.size();

            size_t cursor = 0;
            bool degenerate = false;
            for (const int count : faceCounts)
            {
                if (count < 3 || cursor + static_cast<size_t>(count) > faceIndices.size())
                {
                    degenerate = true;
                    break;
                }
                for (int corner = 1; corner + 1 < count; ++corner)
                {
                    const int a = faceIndices[cursor];
                    const int b = faceIndices[cursor + static_cast<size_t>(corner)];
                    const int c = faceIndices[cursor + static_cast<size_t>(corner) + 1];
                    if (a < 0 || b < 0 || c < 0 || static_cast<size_t>(a) >= points.size() ||
                        static_cast<size_t>(b) >= points.size() ||
                        static_cast<size_t>(c) >= points.size())
                    {
                        degenerate = true;
                        break;
                    }
                    if (indexCount_ + object.indices.size() + 3 > limits_.indices)
                    {
                        truncationFlags_ |= 1u << 8;
                        pageFlags_ |= OPENUSD_PHYSICS_EXTRACT_PAGE_FLAG_TRUNCATED;
                        degenerate = true;
                        break;
                    }
                    object.indices.push_back(static_cast<uint32_t>(a));
                    object.indices.push_back(static_cast<uint32_t>(b));
                    object.indices.push_back(static_cast<uint32_t>(c));
                }
                if (degenerate)
                {
                    break;
                }
                cursor += static_cast<size_t>(count);
            }
            indexCount_ += object.indices.size();
            if (degenerate)
            {
                AddDiagnostic(
                    OPENUSD_PHYSICS_EXTRACT_SEVERITY_WARNING,
                    OPENUSD_PHYSICS_EXTRACT_CATEGORY_GEOMETRY,
                    OPENUSD_PHYSICS_EXTRACT_CODE_GEOMETRY_DEGENERATE,
                    objectIndex,
                    OPENUSD_PHYSICS_EXTRACT_KEY_UNMAPPED,
                    "Mesh topology is degenerate or out of range; triangulation stopped.");
            }
        }
    }
    else if (typeName == UsdGeomTokens->Points)
    {
        object.geometry = OPENUSD_PHYSICS_EXTRACT_GEOMETRY_POINTS;
        // A point cloud carries no topology, so only the points are collected.
        // They are what a particle body is simulated from.
        if ((options_.flags & OPENUSD_PHYSICS_EXTRACT_OPTION_INCLUDE_MESH_DATA) != 0)
        {
            VtVec3fArray points;
            UsdGeomPoints(prim).GetPointsAttr().Get(&points, time_);
            if (pointCount_ + points.size() > limits_.points)
            {
                truncationFlags_ |= 1u << 7;
                pageFlags_ |= OPENUSD_PHYSICS_EXTRACT_PAGE_FLAG_TRUNCATED;
                AddDiagnostic(
                    OPENUSD_PHYSICS_EXTRACT_SEVERITY_WARNING,
                    OPENUSD_PHYSICS_EXTRACT_CATEGORY_CAPACITY,
                    OPENUSD_PHYSICS_EXTRACT_CODE_CAPACITY_EXCEEDED,
                    objectIndex,
                    OPENUSD_PHYSICS_EXTRACT_KEY_UNMAPPED,
                    "Collider point capacity was exceeded; point data was dropped.");
                return;
            }
            object.points.reserve(points.size() * 3);
            for (const GfVec3f& point : points)
            {
                object.points.push_back(point[0]);
                object.points.push_back(point[1]);
                object.points.push_back(point[2]);
            }
            pointCount_ += points.size();
        }
    }
    else if (typeName == UsdGeomTokens->TetMesh)
    {
        object.geometry = OPENUSD_PHYSICS_EXTRACT_GEOMETRY_TET_MESH;
    }
    else
    {
        object.geometry = OPENUSD_PHYSICS_EXTRACT_GEOMETRY_NONE;
        AddDiagnostic(
            OPENUSD_PHYSICS_EXTRACT_SEVERITY_WARNING,
            OPENUSD_PHYSICS_EXTRACT_CATEGORY_GEOMETRY,
            OPENUSD_PHYSICS_EXTRACT_CODE_GEOMETRY_UNSUPPORTED,
            objectIndex,
            OPENUSD_PHYSICS_EXTRACT_KEY_UNMAPPED,
            "Collider prim type has no supported collision geometry.");
    }
}

void Extractor::Traverse()
{
    gTraversalCount.fetch_add(1, std::memory_order_relaxed);
    ReadStageMetadata();
    xformCache_.SetTime(time_);

    uint64_t visited = 0;
    // Exactly one composed stage traversal. Instance proxies are visited so that every
    // instance receives its own stable identity.
    const UsdPrimRange range = stage_->Traverse(
        UsdTraverseInstanceProxies(UsdPrimIsActive && UsdPrimIsDefined && !UsdPrimIsAbstract));
    for (const UsdPrim& prim : range)
    {
        ++visited;
        VisitPrim(prim);
    }
    gVisitedPrimCount.store(visited, std::memory_order_relaxed);
    ResolveReferences();
}

double FindScalar(const ObjectData& object, uint32_t key, double fallback) noexcept
{
    for (const PropertyData& property : object.properties)
    {
        if (property.key == key)
        {
            return property.scalar;
        }
    }
    return fallback;
}

bool HasKey(const ObjectData& object, uint32_t key) noexcept
{
    for (const PropertyData& property : object.properties)
    {
        if (property.key == key)
        {
            return true;
        }
    }
    return false;
}

void Extractor::ReadProperty(
    const UsdPrim& prim,
    const std::string& name,
    bool relationship,
    uint32_t key,
    uint32_t source,
    uint32_t flags,
    ObjectData& object,
    int32_t objectIndex,
    bool hidden)
{
    const TfToken token(name);
    if (relationship)
    {
        const UsdRelationship target = prim.GetRelationship(token);
        if (!target)
        {
            return;
        }
        SdfPathVector paths;
        target.GetTargets(&paths);
        RelationshipData data;
        data.key = key;
        data.name = name;
        data.flags = flags;
        data.targets.reserve(paths.size());
        for (const SdfPath& path : paths)
        {
            data.targets.push_back(path.GetString());
        }
        if (targetCount_ + data.targets.size() > limits_.targets ||
            relationshipCount_ + 1 > limits_.relationships)
        {
            truncationFlags_ |= (1u << 3) | (1u << 4);
            pageFlags_ |= OPENUSD_PHYSICS_EXTRACT_PAGE_FLAG_TRUNCATED;
            AddDiagnostic(
                OPENUSD_PHYSICS_EXTRACT_SEVERITY_WARNING,
                OPENUSD_PHYSICS_EXTRACT_CATEGORY_CAPACITY,
                OPENUSD_PHYSICS_EXTRACT_CODE_CAPACITY_EXCEEDED,
                objectIndex,
                key,
                "Relationship capacity was exceeded; the relationship was dropped.");
            return;
        }
        relationshipCount_ += 1;
        targetCount_ += data.targets.size();
        if (key == OPENUSD_PHYSICS_EXTRACT_KEY_REL_SIMULATION_OWNER)
        {
            object.simulationOwnerCount = static_cast<uint32_t>(data.targets.size());
            if (!data.targets.empty())
            {
                object.simulationOwnerPath = data.targets.front();
            }
        }
        if (hidden)
        {
            object.hiddenRelationships.push_back(std::move(data));
        }
        else
        {
            object.relationships.push_back(std::move(data));
        }
        return;
    }

    const UsdAttribute attribute = prim.GetAttribute(token);
    if (!attribute)
    {
        return;
    }
    PropertyData data;
    data.key = key;
    data.name = name;
    data.source = source;
    data.flags = flags;
    if (attribute.GetNumTimeSamples() > 0)
    {
        data.flags |= OPENUSD_PHYSICS_EXTRACT_PROPERTY_FLAG_TIME_SAMPLED;
    }
    if (attribute.GetVariability() == SdfVariabilityUniform)
    {
        data.flags |= OPENUSD_PHYSICS_EXTRACT_PROPERTY_FLAG_UNIFORM;
    }
    VtValue value;
    if (!attribute.Get(&value, time_) || value.IsEmpty())
    {
        return;
    }
    if (!ConvertValue(value, data))
    {
        data.flags |= OPENUSD_PHYSICS_EXTRACT_PROPERTY_FLAG_INVALID;
        AddDiagnostic(
            OPENUSD_PHYSICS_EXTRACT_SEVERITY_WARNING,
            OPENUSD_PHYSICS_EXTRACT_CATEGORY_SCHEMA,
            OPENUSD_PHYSICS_EXTRACT_CODE_PROPERTY_TYPE_UNSUPPORTED,
            objectIndex,
            key,
            "Authored value type is not representable in the extraction page: " + name);
        return;
    }
    bool finite = std::isfinite(data.scalar);
    for (const double component : data.numbers)
    {
        finite = finite && std::isfinite(component);
    }
    if (!finite)
    {
        data.flags |= OPENUSD_PHYSICS_EXTRACT_PROPERTY_FLAG_INVALID;
        AddDiagnostic(
            OPENUSD_PHYSICS_EXTRACT_SEVERITY_WARNING,
            OPENUSD_PHYSICS_EXTRACT_CATEGORY_SCHEMA,
            OPENUSD_PHYSICS_EXTRACT_CODE_PROPERTY_VALUE_INVALID,
            objectIndex,
            key,
            "Authored value is not a finite number and will not be simulated: " + name);
    }
    if (propertyCount_ + 1 > limits_.properties ||
        numberCount_ + data.numbers.size() > limits_.numbers ||
        textCount_ + data.texts.size() > limits_.texts)
    {
        truncationFlags_ |= (1u << 2) | (1u << 5) | (1u << 6);
        pageFlags_ |= OPENUSD_PHYSICS_EXTRACT_PAGE_FLAG_TRUNCATED;
        AddDiagnostic(
            OPENUSD_PHYSICS_EXTRACT_SEVERITY_WARNING,
            OPENUSD_PHYSICS_EXTRACT_CATEGORY_CAPACITY,
            OPENUSD_PHYSICS_EXTRACT_CODE_CAPACITY_EXCEEDED,
            objectIndex,
            key,
            "Property capacity was exceeded; the property was dropped.");
        return;
    }
    propertyCount_ += 1;
    numberCount_ += data.numbers.size();
    textCount_ += data.texts.size();
    if (hidden)
    {
        object.hiddenProperties.push_back(std::move(data));
    }
    else
    {
        object.properties.push_back(std::move(data));
    }
}

void Extractor::CollectProperties(
    const UsdPrim& prim,
    const std::vector<std::string>& authored,
    std::map<std::string, std::vector<std::string>>& foreignByLeaf,
    std::unordered_set<std::string>& consumed,
    ObjectData& object,
    int32_t objectIndex)
{
    const std::unordered_set<std::string> authoredSet(authored.begin(), authored.end());
    for (size_t entryIndex = 0; entryIndex < kCanonicalEntryCount; ++entryIndex)
    {
        const CanonicalEntry& entry = kCanonicalEntries[entryIndex];
        if ((entry.domain & object.domains) == 0)
        {
            continue;
        }

        const bool hasProject = entry.project != nullptr &&
            authoredSet.find(entry.project) != authoredSet.end() &&
            consumed.find(entry.project) == consumed.end();
        const bool hasStandard = entry.standard != nullptr &&
            authoredSet.find(entry.standard) != authoredSet.end() &&
            consumed.find(entry.standard) == consumed.end();

        std::vector<std::string> foreign;
        const auto foreignIterator = foreignByLeaf.find(entry.leaf);
        if (foreignIterator != foreignByLeaf.end())
        {
            for (const std::string& candidate : foreignIterator->second)
            {
                if (consumed.find(candidate) == consumed.end())
                {
                    foreign.push_back(candidate);
                }
            }
        }

        uint32_t source = OPENUSD_PHYSICS_EXTRACT_SOURCE_FALLBACK;
        std::string selected;
        if (hasProject)
        {
            source = OPENUSD_PHYSICS_EXTRACT_SOURCE_PROJECT;
            selected = entry.project;
        }
        else if (!foreign.empty())
        {
            source = OPENUSD_PHYSICS_EXTRACT_SOURCE_FOREIGN;
            selected = foreign.front();
        }
        else if (hasStandard)
        {
            source = OPENUSD_PHYSICS_EXTRACT_SOURCE_STANDARD;
            selected = entry.standard;
        }
        else
        {
            continue;
        }

        uint32_t flags = OPENUSD_PHYSICS_EXTRACT_PROPERTY_FLAG_NONE;
        const size_t opinions =
            (hasProject ? 1u : 0u) + (foreign.empty() ? 0u : 1u) + (hasStandard ? 1u : 0u);
        if (opinions > 1)
        {
            flags |= OPENUSD_PHYSICS_EXTRACT_PROPERTY_FLAG_SHADOWS_WEAKER;
        }
        if (foreign.size() > 1)
        {
            flags |= OPENUSD_PHYSICS_EXTRACT_PROPERTY_FLAG_AMBIGUOUS_FOREIGN;
            AddDiagnostic(
                OPENUSD_PHYSICS_EXTRACT_SEVERITY_WARNING,
                OPENUSD_PHYSICS_EXTRACT_CATEGORY_PRECEDENCE,
                OPENUSD_PHYSICS_EXTRACT_CODE_FOREIGN_OPINION_AMBIGUOUS,
                objectIndex,
                entry.key,
                "Several foreign opinions match one canonical property; the first was used: " +
                    foreign.front());
        }

        if (hasProject)
        {
            consumed.insert(entry.project);
            object.flags |= OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_PROJECT_OPINIONS;
        }
        for (const std::string& candidate : foreign)
        {
            consumed.insert(candidate);
        }
        if (!foreign.empty())
        {
            object.flags |= OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_FOREIGN_OPINIONS;
            pageFlags_ |= OPENUSD_PHYSICS_EXTRACT_PAGE_FLAG_HAS_FOREIGN_OPINIONS;
            if (source == OPENUSD_PHYSICS_EXTRACT_SOURCE_FOREIGN)
            {
                AddDiagnostic(
                    OPENUSD_PHYSICS_EXTRACT_SEVERITY_INFORMATION,
                    OPENUSD_PHYSICS_EXTRACT_CATEGORY_PRECEDENCE,
                    OPENUSD_PHYSICS_EXTRACT_CODE_FOREIGN_OPINION_USED,
                    objectIndex,
                    entry.key,
                    "A foreign opinion supplied a canonical property: " + selected);
            }
        }
        if (hasStandard)
        {
            consumed.insert(entry.standard);
            object.flags |= OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_STANDARD_OPINIONS;
        }

        ReadProperty(
            prim, selected, entry.relationship, entry.key, source, flags, object, objectIndex,
            false);
    }

    CollectMultiApply(prim, authored, consumed, object, objectIndex);
}

void Extractor::CollectMultiApply(
    const UsdPrim& prim,
    const std::vector<std::string>& authored,
    std::unordered_set<std::string>& consumed,
    ObjectData& object,
    int32_t objectIndex)
{
    if ((object.domains & OPENUSD_PHYSICS_EXTRACT_DOMAIN_JOINT) == 0)
    {
        return;
    }

    // The authored names are already sorted, so one instance never overtakes another.
    for (const std::string& name : authored)
    {
        if (consumed.find(name) != consumed.end())
        {
            continue;
        }
        for (size_t entryIndex = 0; entryIndex < kMultiApplyEntryCount; ++entryIndex)
        {
            const MultiApplyEntry& entry = kMultiApplyEntries[entryIndex];
            if (!StartsWith(name, entry.prefix))
            {
                continue;
            }
            const size_t start = std::strlen(entry.prefix);
            const size_t end = name.find(':', start);
            if (end == std::string::npos || end == start)
            {
                continue;
            }
            const std::string expected =
                name.substr(0, end + 1) + "physics:" + entry.leaf;
            if (name != expected)
            {
                continue;
            }
            consumed.insert(name);
            ReadProperty(
                prim,
                name,
                false,
                entry.key,
                OPENUSD_PHYSICS_EXTRACT_SOURCE_STANDARD,
                OPENUSD_PHYSICS_EXTRACT_PROPERTY_FLAG_NONE,
                object,
                objectIndex,
                false);
            object.flags |= OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_STANDARD_OPINIONS;
            break;
        }
    }
}

void Extractor::CollectUnmapped(
    const UsdPrim& prim,
    const std::vector<std::string>& authored,
    const std::unordered_set<std::string>& consumed,
    const std::vector<int32_t>& primObjects)
{
    if (primObjects.empty())
    {
        return;
    }
    const bool visible = (options_.flags & OPENUSD_PHYSICS_EXTRACT_OPTION_INCLUDE_UNMAPPED) != 0;
    for (const std::string& name : authored)
    {
        if (consumed.find(name) != consumed.end())
        {
            continue;
        }
        int32_t objectIndex = primObjects.front();
        if (StartsWith(name, "openUsdPhysics:"))
        {
            const size_t start = std::strlen("openUsdPhysics:");
            const size_t end = name.find(':', start);
            if (end != std::string::npos)
            {
                const uint32_t objectType =
                    ObjectTypeForGroup(std::string_view(name).substr(start, end - start));
                for (const int32_t candidate : primObjects)
                {
                    if (objects_[static_cast<size_t>(candidate)].objectType == objectType)
                    {
                        objectIndex = candidate;
                        break;
                    }
                }
            }
        }
        ObjectData& object = objects_[static_cast<size_t>(objectIndex)];
        const bool relationship = static_cast<bool>(prim.GetRelationship(TfToken(name)));
        if (StartsWith(name, "physx"))
        {
            object.flags |= OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_FOREIGN_OPINIONS;
            pageFlags_ |= OPENUSD_PHYSICS_EXTRACT_PAGE_FLAG_HAS_FOREIGN_OPINIONS;
        }
        ReadProperty(
            prim,
            name,
            relationship,
            OPENUSD_PHYSICS_EXTRACT_KEY_UNMAPPED,
            StartsWith(name, "openUsdPhysics:")
                ? OPENUSD_PHYSICS_EXTRACT_SOURCE_PROJECT
                : (StartsWith(name, "physx") ? OPENUSD_PHYSICS_EXTRACT_SOURCE_FOREIGN
                                             : OPENUSD_PHYSICS_EXTRACT_SOURCE_STANDARD),
            OPENUSD_PHYSICS_EXTRACT_PROPERTY_FLAG_UNMAPPED,
            object,
            objectIndex,
            !visible);
    }
}

void Extractor::VisitPrim(const UsdPrim& prim)
{
    const SdfPath primPath = prim.GetPath();
    const std::string primPathText = primPath.GetString();

    while (!objectStack_.empty() && !primPath.HasPrefix(objectStack_.back().path))
    {
        objectStack_.pop_back();
    }
    while (!bodyStack_.empty() && !primPath.HasPrefix(bodyStack_.back().path))
    {
        bodyStack_.pop_back();
    }

    std::vector<uint32_t> types;
    const auto addType = [&types](uint32_t objectType) {
        if (objectType == OPENUSD_PHYSICS_EXTRACT_OBJECT_UNKNOWN)
        {
            return;
        }
        if (std::find(types.begin(), types.end(), objectType) == types.end())
        {
            types.push_back(objectType);
        }
    };

    addType(ObjectTypeForType(prim.GetTypeName().GetString()));
    for (const TfToken& schema : prim.GetAppliedSchemas())
    {
        addType(ObjectTypeForApi(schema.GetString()));
    }
    // Codeless project schemas are not always registered, so the composed apiSchemas list is
    // read directly as well. Detection therefore never depends on plugin registration.
    SdfTokenListOp appliedListOp;
    if (prim.GetMetadata(UsdTokens->apiSchemas, &appliedListOp))
    {
        TfTokenVector applied;
        appliedListOp.ApplyOperations(&applied);
        for (const TfToken& schema : applied)
        {
            addType(ObjectTypeForApi(schema.GetString()));
        }
    }

    std::vector<std::string> authored;
    for (const UsdProperty& property : prim.GetAuthoredProperties())
    {
        const std::string name = property.GetName().GetString();
        if (IsPhysicsPropertyName(name))
        {
            authored.push_back(name);
        }
    }
    std::sort(authored.begin(), authored.end());

    // Authored project opinions declare an object even when no API schema is applied.
    for (const std::string& name : authored)
    {
        if (!StartsWith(name, "openUsdPhysics:"))
        {
            continue;
        }
        const size_t start = std::strlen("openUsdPhysics:");
        const size_t end = name.find(':', start);
        if (end != std::string::npos)
        {
            addType(ObjectTypeForGroup(std::string_view(name).substr(start, end - start)));
        }
    }

    if (types.empty())
    {
        return;
    }
    std::sort(types.begin(), types.end());

    uint32_t sharedFlags = OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_ENABLED;
    if (prim.IsA<UsdGeomImageable>())
    {
        const UsdGeomImageable imageable(prim);
        if (imageable.ComputeVisibility(time_) == UsdGeomTokens->invisible)
        {
            sharedFlags |= OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_INVISIBLE;
        }
        if (imageable.ComputePurpose() == UsdGeomTokens->guide)
        {
            sharedFlags |= OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_GUIDE_PURPOSE;
        }
    }
    if ((sharedFlags & OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_INVISIBLE) != 0 &&
        (options_.flags & OPENUSD_PHYSICS_EXTRACT_OPTION_SKIP_INVISIBLE) != 0)
    {
        return;
    }
    if ((sharedFlags & OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_GUIDE_PURPOSE) != 0 &&
        (options_.flags & OPENUSD_PHYSICS_EXTRACT_OPTION_SKIP_GUIDE) != 0)
    {
        return;
    }

    bool animated = false;
    if (prim.IsA<UsdGeomXformable>())
    {
        animated = UsdGeomXformable(prim).TransformMightBeTimeVarying();
    }
    if (animated)
    {
        sharedFlags |= OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_ANIMATED |
            OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_TIME_SAMPLED_TRANSFORM;
    }
    if (prim.IsInstanceProxy())
    {
        sharedFlags |= OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_INSTANCE_PROXY;
        pageFlags_ |= OPENUSD_PHYSICS_EXTRACT_PAGE_FLAG_HAS_INSTANCES;
    }
    if (prim.IsInPrototype())
    {
        sharedFlags |= OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_IN_PROTOTYPE;
    }

    std::map<std::string, std::vector<std::string>> foreignByLeaf;
    for (const std::string& name : authored)
    {
        if (StartsWith(name, "physx"))
        {
            foreignByLeaf[std::string(LastNamespaceComponent(name))].push_back(name);
        }
    }

    // Consumption is scoped to one object so shared opinions such as the simulation owner or the
    // material binding reach every object on the prim. The prim wide set only decides which
    // authored names are still unmapped.
    std::unordered_set<std::string> seen;
    std::vector<int32_t> primObjects;
    primObjects.reserve(types.size());

    // A rigid body and a collider may live on the same prim. Object types are sorted, and the
    // rigid body type sorts first, so the body index is known before the collider is created.
    int32_t primBodyIndex = -1;

    for (const uint32_t objectType : types)
    {
        if (objects_.size() >= static_cast<size_t>(limits_.objects))
        {
            truncationFlags_ |= 1u << 1;
            pageFlags_ |= OPENUSD_PHYSICS_EXTRACT_PAGE_FLAG_TRUNCATED;
            AddDiagnostic(
                OPENUSD_PHYSICS_EXTRACT_SEVERITY_WARNING,
                OPENUSD_PHYSICS_EXTRACT_CATEGORY_CAPACITY,
                OPENUSD_PHYSICS_EXTRACT_CODE_CAPACITY_EXCEEDED,
                -1,
                OPENUSD_PHYSICS_EXTRACT_KEY_UNMAPPED,
                "Object capacity was exceeded; remaining prims were not extracted.");
            return;
        }

        const int32_t objectIndex = static_cast<int32_t>(objects_.size());
        ObjectData created;
        created.path = primPathText;
        created.name = prim.GetName().GetString();
        created.typeName = prim.GetTypeName().GetString();
        created.objectType = objectType;
        created.domains = DomainForObjectType(objectType);
        created.flags = sharedFlags;
        created.id = HashIdentity(primPathText + "|" + std::to_string(objectType));
        if (!objectStack_.empty())
        {
            created.parentId = objectStack_.back().id;
        }
        if (primBodyIndex >= 0)
        {
            created.parentBodyIndex = primBodyIndex;
        }
        else if (!bodyStack_.empty())
        {
            created.parentBodyIndex = bodyStack_.back().index;
        }
        if (prim.IsInstanceProxy())
        {
            const UsdPrim prototype = prim.GetPrimInPrototype();
            if (prototype)
            {
                created.prototypeId = HashIdentity(
                    prototype.GetPath().GetString() + "|" + std::to_string(objectType));
            }
        }
        objects_.push_back(std::move(created));
        primObjects.push_back(objectIndex);
        objectByPath_.emplace(primPathText + "|" + std::to_string(objectType), objectIndex);

        ObjectData& object = objects_[static_cast<size_t>(objectIndex)];
        ApplySpaceTransform(prim, object);
        std::unordered_set<std::string> consumed;
        CollectProperties(prim, authored, foreignByLeaf, consumed, object, objectIndex);
        seen.insert(consumed.begin(), consumed.end());

        if ((object.domains & kSimulatedDomains) == 0)
        {
            object.flags |= OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_UNSUPPORTED_DOMAIN;
            AddDiagnostic(
                OPENUSD_PHYSICS_EXTRACT_SEVERITY_INFORMATION,
                OPENUSD_PHYSICS_EXTRACT_CATEGORY_CAPABILITY,
                OPENUSD_PHYSICS_EXTRACT_CODE_DOMAIN_NOT_SIMULATED,
                objectIndex,
                OPENUSD_PHYSICS_EXTRACT_KEY_UNMAPPED,
                "This domain is extracted but not simulated yet: " + object.path);
        }

        if (object.simulationOwnerCount > 1)
        {
            object.flags |= OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_CONTRADICTORY_OWNERSHIP;
            AddDiagnostic(
                OPENUSD_PHYSICS_EXTRACT_SEVERITY_ERROR,
                OPENUSD_PHYSICS_EXTRACT_CATEGORY_OWNERSHIP,
                OPENUSD_PHYSICS_EXTRACT_CODE_MULTIPLE_SIMULATION_OWNERS,
                objectIndex,
                OPENUSD_PHYSICS_EXTRACT_KEY_REL_SIMULATION_OWNER,
                "More than one simulation owner was authored: " + object.path);
        }

        switch (objectType)
        {
            case OPENUSD_PHYSICS_EXTRACT_OBJECT_SCENE:
            {
                if (defaultSceneIndex_ < 0)
                {
                    defaultSceneIndex_ = objectIndex;
                    object.flags |= OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_DEFAULT_SCENE;
                }
                sceneIndices_.push_back(objectIndex);
                break;
            }
            case OPENUSD_PHYSICS_EXTRACT_OBJECT_RIGID_BODY:
            {
                const bool enabled = FindScalar(
                    object, OPENUSD_PHYSICS_EXTRACT_KEY_BODY_ENABLED, 1.0) != 0.0;
                const bool kinematic = FindScalar(
                    object, OPENUSD_PHYSICS_EXTRACT_KEY_BODY_KINEMATIC, 0.0) != 0.0;
                if (!enabled)
                {
                    object.flags |= OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_STATIC;
                }
                else if (kinematic)
                {
                    object.flags |= OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_KINEMATIC;
                }
                else if (animated)
                {
                    // A time sampled transform contradicts dynamic simulation. Kinematic is
                    // the deterministic resolution so the authored animation still plays.
                    object.flags |= OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_KINEMATIC;
                    AddDiagnostic(
                        OPENUSD_PHYSICS_EXTRACT_SEVERITY_WARNING,
                        OPENUSD_PHYSICS_EXTRACT_CATEGORY_OWNERSHIP,
                        OPENUSD_PHYSICS_EXTRACT_CODE_ANIMATED_DYNAMIC_BODY,
                        objectIndex,
                        OPENUSD_PHYSICS_EXTRACT_KEY_BODY_KINEMATIC,
                        "A dynamic body has an animated transform; it became kinematic.");
                }
                else
                {
                    object.flags |= OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_DYNAMIC;
                }
                if (HasKey(object, OPENUSD_PHYSICS_EXTRACT_KEY_MASS_MASS))
                {
                    object.flags |= OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_MASS_AUTHORED;
                }
                primBodyIndex = objectIndex;
                if (!bodyStack_.empty())
                {
                    object.flags |= OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_NESTED_BODY;
                    AddDiagnostic(
                        OPENUSD_PHYSICS_EXTRACT_SEVERITY_ERROR,
                        OPENUSD_PHYSICS_EXTRACT_CATEGORY_OWNERSHIP,
                        OPENUSD_PHYSICS_EXTRACT_CODE_NESTED_RIGID_BODY,
                        objectIndex,
                        OPENUSD_PHYSICS_EXTRACT_KEY_BODY_ENABLED,
                        "A rigid body is nested inside another rigid body: " + object.path);
                }
                break;
            }
            case OPENUSD_PHYSICS_EXTRACT_OBJECT_COLLIDER:
            {
                CollectGeometry(prim, object, objectIndex);
                if (object.parentBodyIndex < 0)
                {
                    object.flags |= OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_STATIC;
                    AddDiagnostic(
                        OPENUSD_PHYSICS_EXTRACT_SEVERITY_INFORMATION,
                        OPENUSD_PHYSICS_EXTRACT_CATEGORY_OWNERSHIP,
                        OPENUSD_PHYSICS_EXTRACT_CODE_ORPHANED_COLLIDER,
                        objectIndex,
                        OPENUSD_PHYSICS_EXTRACT_KEY_COLLISION_ENABLED,
                        "A collider has no rigid body ancestor; it is static geometry.");
                }
                break;
            }
            case OPENUSD_PHYSICS_EXTRACT_OBJECT_PARTICLE_SET:
            case OPENUSD_PHYSICS_EXTRACT_OBJECT_PARTICLE_CLOTH:
            case OPENUSD_PHYSICS_EXTRACT_OBJECT_SURFACE_DEFORMABLE:
            case OPENUSD_PHYSICS_EXTRACT_OBJECT_VOLUME_DEFORMABLE:
            {
                // A particle body and a deformable are solved on the points of
                // the geometry they are applied to, so the same geometry
                // collection a collider uses carries their rest configuration.
                // Nothing here decides whether the object can be simulated: it
                // only makes the authored points and topology available to a
                // composer that may or may not have a device.
                CollectGeometry(prim, object, objectIndex);
                break;
            }
            default:
                break;
        }
    }

    CollectUnmapped(prim, authored, seen, primObjects);

    for (const int32_t objectIndex : primObjects)
    {
        if (objects_[static_cast<size_t>(objectIndex)].objectType ==
            OPENUSD_PHYSICS_EXTRACT_OBJECT_RIGID_BODY)
        {
            bodyStack_.push_back(BodyFrame{primPath, objectIndex});
            break;
        }
    }
    objectStack_.push_back(
        ObjectFrame{primPath, objects_[static_cast<size_t>(primObjects.front())].id});
}

void Extractor::ResolveReferences()
{
    // Nothing in this method reads the stage. Only collected data is used.
    for (size_t index = 0; index < objects_.size(); ++index)
    {
        primaryByPath_.emplace(objects_[index].path, static_cast<int32_t>(index));
    }

    std::unordered_map<std::string, int32_t> sceneByPath;
    for (const int32_t sceneIndex : sceneIndices_)
    {
        sceneByPath.emplace(objects_[static_cast<size_t>(sceneIndex)].path, sceneIndex);
    }

    for (size_t index = 0; index < objects_.size(); ++index)
    {
        ObjectData& object = objects_[index];
        if (!object.simulationOwnerPath.empty())
        {
            const auto owner = sceneByPath.find(object.simulationOwnerPath);
            if (owner != sceneByPath.end())
            {
                object.sceneIndex = owner->second;
            }
            else
            {
                object.sceneIndex = defaultSceneIndex_;
                AddDiagnostic(
                    OPENUSD_PHYSICS_EXTRACT_SEVERITY_WARNING,
                    OPENUSD_PHYSICS_EXTRACT_CATEGORY_OWNERSHIP,
                    OPENUSD_PHYSICS_EXTRACT_CODE_UNKNOWN_SIMULATION_OWNER,
                    static_cast<int32_t>(index),
                    OPENUSD_PHYSICS_EXTRACT_KEY_REL_SIMULATION_OWNER,
                    "The authored simulation owner is not a physics scene: " +
                        object.simulationOwnerPath);
            }
        }
        else if (object.objectType != OPENUSD_PHYSICS_EXTRACT_OBJECT_SCENE)
        {
            object.sceneIndex = defaultSceneIndex_;
        }

        for (RelationshipData& relationship : object.relationships)
        {
            relationship.targetIndices.reserve(relationship.targets.size());
            for (const std::string& target : relationship.targets)
            {
                const auto resolved = primaryByPath_.find(target);
                relationship.targetIndices.push_back(
                    resolved == primaryByPath_.end() ? -1 : resolved->second);
            }
        }
        for (RelationshipData& relationship : object.hiddenRelationships)
        {
            relationship.targetIndices.assign(relationship.targets.size(), -1);
        }
    }

    for (size_t index = 0; index < objects_.size(); ++index)
    {
        ObjectData& object = objects_[index];
        if (object.objectType == OPENUSD_PHYSICS_EXTRACT_OBJECT_COLLIDER &&
            object.parentBodyIndex >= 0)
        {
            const ObjectData& body = objects_[static_cast<size_t>(object.parentBodyIndex)];
            if (!object.simulationOwnerPath.empty() && !body.simulationOwnerPath.empty() &&
                object.simulationOwnerPath != body.simulationOwnerPath)
            {
                object.flags |= OPENUSD_PHYSICS_EXTRACT_OBJECT_FLAG_CONTRADICTORY_OWNERSHIP;
                AddDiagnostic(
                    OPENUSD_PHYSICS_EXTRACT_SEVERITY_ERROR,
                    OPENUSD_PHYSICS_EXTRACT_CATEGORY_OWNERSHIP,
                    OPENUSD_PHYSICS_EXTRACT_CODE_CONTRADICTORY_OWNERSHIP,
                    static_cast<int32_t>(index),
                    OPENUSD_PHYSICS_EXTRACT_KEY_REL_SIMULATION_OWNER,
                    "A collider and its rigid body claim different simulation owners.");
            }
        }
        if (object.objectType == OPENUSD_PHYSICS_EXTRACT_OBJECT_JOINT)
        {
            for (const RelationshipData& relationship : object.relationships)
            {
                if (relationship.key != OPENUSD_PHYSICS_EXTRACT_KEY_REL_BODY0 &&
                    relationship.key != OPENUSD_PHYSICS_EXTRACT_KEY_REL_BODY1)
                {
                    continue;
                }
                for (size_t target = 0; target < relationship.targetIndices.size(); ++target)
                {
                    if (relationship.targetIndices[target] < 0)
                    {
                        AddDiagnostic(
                            OPENUSD_PHYSICS_EXTRACT_SEVERITY_WARNING,
                            OPENUSD_PHYSICS_EXTRACT_CATEGORY_OWNERSHIP,
                            OPENUSD_PHYSICS_EXTRACT_CODE_JOINT_BODY_UNRESOLVED,
                            static_cast<int32_t>(index),
                            relationship.key,
                            "A joint body target does not resolve to an extracted object: " +
                                relationship.targets[target]);
                    }
                }
            }
        }
    }

    // Diagnostics are grouped per object so that readers see a stable order regardless of
    // the pass that produced them. Traversal order already made the input deterministic.
    std::stable_sort(
        diagnostics_.begin(),
        diagnostics_.end(),
        [](const DiagnosticData& left, const DiagnosticData& right) {
            return left.objectIndex < right.objectIndex;
        });
}

std::vector<unsigned char> Extractor::Serialize()
{
    Fingerprint fingerprint;
    fingerprint.AddText("openusd-physics-extract");
    fingerprint.AddUInt(OPENUSD_PHYSICS_EXTRACT_ABI_VERSION);
    fingerprint.AddReal(metersPerUnit_);
    fingerprint.AddReal(kilogramsPerUnit_);
    fingerprint.AddReal(timeCodesPerSecond_);
    fingerprint.AddReal(startTimeCode_);
    fingerprint.AddReal(endTimeCode_);
    fingerprint.AddUInt(upAxis_);

    ByteWriter objectWriter;
    ByteWriter propertyWriter;
    ByteWriter relationshipWriter;
    ByteWriter targetWriter;
    ByteWriter numberWriter;
    ByteWriter textWriter;
    ByteWriter pointWriter;
    ByteWriter indexWriter;
    ByteWriter diagnosticWriter;

    uint32_t propertyCursor = 0;
    uint32_t relationshipCursor = 0;
    uint32_t targetCursor = 0;
    uint32_t numberCursor = 0;
    uint32_t textCursor = 0;
    uint32_t pointCursor = 0;
    uint32_t indexCursor = 0;

    const auto fingerprintProperty = [&fingerprint](const PropertyData& property) {
        fingerprint.AddUInt(property.key);
        fingerprint.AddText(property.name);
        fingerprint.AddUInt(property.source);
        fingerprint.AddUInt(property.valueType);
        fingerprint.AddUInt(property.flags);
        fingerprint.AddReal(property.scalar);
        for (const double number : property.numbers)
        {
            fingerprint.AddReal(number);
        }
        for (const std::string& text : property.texts)
        {
            fingerprint.AddText(text);
        }
    };
    const auto fingerprintRelationship = [&fingerprint](const RelationshipData& relationship) {
        fingerprint.AddUInt(relationship.key);
        fingerprint.AddText(relationship.name);
        for (const std::string& target : relationship.targets)
        {
            fingerprint.AddText(target);
        }
    };

    const auto writeProperty = [&](const PropertyData& property) {
        uint32_t valueIndex = 0;
        uint32_t valueCount = 0;
        if (!property.texts.empty())
        {
            valueIndex = textCursor;
            valueCount = static_cast<uint32_t>(property.texts.size());
            for (const std::string& text : property.texts)
            {
                textWriter.PutU32(strings_.Add(text));
                textWriter.PutU32(static_cast<uint32_t>(text.size()));
            }
            textCursor += valueCount;
        }
        else if (!property.numbers.empty())
        {
            valueIndex = numberCursor;
            valueCount = static_cast<uint32_t>(property.numbers.size());
            for (const double number : property.numbers)
            {
                numberWriter.PutF64(number);
            }
            numberCursor += valueCount;
        }
        propertyWriter.PutU32(property.key);
        propertyWriter.PutU32(strings_.Add(property.name));
        propertyWriter.PutU32(property.valueType);
        propertyWriter.PutU32(property.flags);
        propertyWriter.PutU32(property.source);
        propertyWriter.PutU32(valueIndex);
        propertyWriter.PutU32(valueCount);
        propertyWriter.PutU32(0);
        propertyWriter.PutF64(property.scalar);
        propertyCursor += 1;
    };

    for (const ObjectData& object : objects_)
    {
        fingerprint.AddUInt(object.id);
        fingerprint.AddText(object.path);
        fingerprint.AddText(object.typeName);
        fingerprint.AddUInt(object.objectType);
        fingerprint.AddUInt(object.domains);
        fingerprint.AddUInt(object.flags);
        fingerprint.AddUInt(object.prototypeId);
        fingerprint.AddUInt(object.geometry);
        fingerprint.AddUInt(object.geometryAxis);
        for (size_t axis = 0; axis < 3; ++axis)
        {
            fingerprint.AddReal(object.position[axis]);
            fingerprint.AddReal(object.scale[axis]);
            fingerprint.AddReal(object.extent[axis]);
        }
        for (size_t component = 0; component < 4; ++component)
        {
            fingerprint.AddReal(object.rotation[component]);
        }
        for (const PropertyData& property : object.properties)
        {
            fingerprintProperty(property);
        }
        // Properties that are not published still describe authored physics intent, so they
        // take part in the fingerprint. Two extractions of one stage therefore agree even
        // when the caller asks for different levels of detail.
        for (const PropertyData& property : object.hiddenProperties)
        {
            fingerprintProperty(property);
        }
        for (const RelationshipData& relationship : object.relationships)
        {
            fingerprintRelationship(relationship);
        }
        for (const RelationshipData& relationship : object.hiddenRelationships)
        {
            fingerprintRelationship(relationship);
        }
        for (const float coordinate : object.points)
        {
            fingerprint.AddReal(static_cast<double>(coordinate));
        }
        for (const uint32_t index : object.indices)
        {
            fingerprint.AddUInt(index);
        }

        const uint32_t propertyStart = propertyCursor;
        for (const PropertyData& property : object.properties)
        {
            writeProperty(property);
        }
        const uint32_t propertyRecords = propertyCursor - propertyStart;

        const uint32_t relationshipStart = relationshipCursor;
        for (const RelationshipData& relationship : object.relationships)
        {
            const uint32_t targetStart = targetCursor;
            for (size_t target = 0; target < relationship.targets.size(); ++target)
            {
                targetWriter.PutU64(HashIdentity(relationship.targets[target]));
                targetWriter.PutU32(strings_.Add(relationship.targets[target]));
                targetWriter.PutI32(
                    target < relationship.targetIndices.size()
                        ? relationship.targetIndices[target]
                        : -1);
            }
            targetCursor += static_cast<uint32_t>(relationship.targets.size());
            relationshipWriter.PutU32(relationship.key);
            relationshipWriter.PutU32(strings_.Add(relationship.name));
            relationshipWriter.PutU32(targetStart);
            relationshipWriter.PutU32(targetCursor - targetStart);
            relationshipWriter.PutU32(relationship.flags);
            relationshipWriter.PutU32(0);
            relationshipCursor += 1;
        }
        const uint32_t relationshipRecords = relationshipCursor - relationshipStart;

        const uint32_t pointStart = pointCursor;
        for (size_t coordinate = 0; coordinate + 2 < object.points.size(); coordinate += 3)
        {
            pointWriter.PutF32(object.points[coordinate]);
            pointWriter.PutF32(object.points[coordinate + 1]);
            pointWriter.PutF32(object.points[coordinate + 2]);
            pointCursor += 1;
        }
        const uint32_t indexStart = indexCursor;
        for (const uint32_t index : object.indices)
        {
            indexWriter.PutU32(index);
            indexCursor += 1;
        }

        objectWriter.PutU64(object.id);
        objectWriter.PutU64(object.parentId);
        objectWriter.PutU64(object.prototypeId);
        objectWriter.PutU32(strings_.Add(object.path));
        objectWriter.PutU32(strings_.Add(object.name));
        objectWriter.PutU32(strings_.Add(object.typeName));
        objectWriter.PutU32(object.objectType);
        objectWriter.PutU32(object.domains);
        objectWriter.PutU32(object.flags);
        objectWriter.PutU32(object.geometry);
        objectWriter.PutI32(object.sceneIndex);
        objectWriter.PutI32(object.parentBodyIndex);
        objectWriter.PutU32(propertyStart);
        objectWriter.PutU32(propertyRecords);
        objectWriter.PutU32(relationshipStart);
        objectWriter.PutU32(relationshipRecords);
        objectWriter.PutU32(pointStart);
        objectWriter.PutU32(pointCursor - pointStart);
        objectWriter.PutU32(indexStart);
        objectWriter.PutU32(indexCursor - indexStart);
        objectWriter.PutU32(object.diagnosticCount);
        for (size_t axis = 0; axis < 3; ++axis)
        {
            objectWriter.PutF64(object.position[axis]);
        }
        for (size_t component = 0; component < 4; ++component)
        {
            objectWriter.PutF64(object.rotation[component]);
        }
        for (size_t axis = 0; axis < 3; ++axis)
        {
            objectWriter.PutF64(object.scale[axis]);
        }
        for (size_t axis = 0; axis < 3; ++axis)
        {
            objectWriter.PutF64(object.extent[axis]);
        }
        objectWriter.PutU32(object.geometryAxis);
        objectWriter.PutU32(0);
    }

    for (const DiagnosticData& diagnostic : diagnostics_)
    {
        diagnosticWriter.PutU32(diagnostic.severity);
        diagnosticWriter.PutU32(diagnostic.category);
        diagnosticWriter.PutU32(diagnostic.code);
        diagnosticWriter.PutI32(diagnostic.objectIndex);
        diagnosticWriter.PutU32(strings_.Add(diagnostic.message));
        diagnosticWriter.PutU32(diagnostic.key);
        diagnosticWriter.PutU64(diagnostic.objectId);
    }

    if (strings_.Truncated())
    {
        truncationFlags_ |= 1u << 0;
        pageFlags_ |= OPENUSD_PHYSICS_EXTRACT_PAGE_FLAG_TRUNCATED;
    }

    struct Section
    {
        const std::vector<unsigned char>* bytes;
        uint32_t count;
        uint32_t offset;
    };

    const std::vector<unsigned char>& stringBytes = strings_.Bytes();
    Section sections[10] = {
        {&stringBytes, static_cast<uint32_t>(stringBytes.size()), 0},
        {&objectWriter.Bytes(), static_cast<uint32_t>(objects_.size()), 0},
        {&propertyWriter.Bytes(), propertyCursor, 0},
        {&relationshipWriter.Bytes(), relationshipCursor, 0},
        {&targetWriter.Bytes(), targetCursor, 0},
        {&numberWriter.Bytes(), numberCursor, 0},
        {&textWriter.Bytes(), textCursor, 0},
        {&pointWriter.Bytes(), pointCursor, 0},
        {&indexWriter.Bytes(), indexCursor, 0},
        {&diagnosticWriter.Bytes(), static_cast<uint32_t>(diagnostics_.size()), 0}};

    uint64_t cursor = OPENUSD_PHYSICS_EXTRACT_HEADER_BYTES;
    for (Section& section : sections)
    {
        if (section.count == 0 || section.bytes->empty())
        {
            section.offset = 0;
            section.count = 0;
            continue;
        }
        cursor = (cursor + (kAlignment - 1)) & ~static_cast<uint64_t>(kAlignment - 1);
        if (cursor > OPENUSD_PHYSICS_EXTRACT_PAGE_MAX_BYTES)
        {
            throw std::runtime_error("The physics extraction page exceeded its maximum size.");
        }
        section.offset = static_cast<uint32_t>(cursor);
        cursor += section.bytes->size();
        if (cursor > OPENUSD_PHYSICS_EXTRACT_PAGE_MAX_BYTES)
        {
            throw std::runtime_error("The physics extraction page exceeded its maximum size.");
        }
    }
    cursor = (cursor + (kAlignment - 1)) & ~static_cast<uint64_t>(kAlignment - 1);

    fingerprint.AddUInt(pageFlags_ & ~static_cast<uint32_t>(
        OPENUSD_PHYSICS_EXTRACT_PAGE_FLAG_TRUNCATED));

    std::vector<unsigned char> page(static_cast<size_t>(cursor), 0);
    ByteWriter header;
    header.PutU64(OPENUSD_PHYSICS_EXTRACT_PAGE_MAGIC);
    header.PutU32(OPENUSD_PHYSICS_EXTRACT_ABI_VERSION);
    header.PutU32(OPENUSD_PHYSICS_EXTRACT_HEADER_BYTES);
    header.PutU64(cursor);
    header.PutU64(fingerprint.Low());
    header.PutU64(fingerprint.High());
    header.PutF64(metersPerUnit_);
    header.PutF64(kilogramsPerUnit_);
    header.PutF64(timeCodesPerSecond_);
    header.PutF64(startTimeCode_);
    header.PutF64(endTimeCode_);
    header.PutF64(time_.IsDefault() ? startTimeCode_ : time_.GetValue());
    header.PutU32(upAxis_);
    header.PutU32(pageFlags_);
    header.PutI32(defaultSceneIndex_);
    header.PutU32(truncationFlags_);
    for (const Section& section : sections)
    {
        header.PutU32(section.offset);
        header.PutU32(section.count);
    }
    for (size_t reserved = 0; reserved < 4; ++reserved)
    {
        header.PutU64(0);
    }
    if (header.Size() != OPENUSD_PHYSICS_EXTRACT_HEADER_BYTES)
    {
        throw std::runtime_error("The physics extraction header layout is inconsistent.");
    }
    std::memcpy(page.data(), header.Bytes().data(), header.Bytes().size());
    for (const Section& section : sections)
    {
        if (section.offset == 0)
        {
            continue;
        }
        std::memcpy(page.data() + section.offset, section.bytes->data(), section.bytes->size());
    }
    return page;
}

}

struct openusd_physics_extraction
{
    std::vector<unsigned char> data;
};

openusd_status openusd_physics_extract_stage(
    const openusd_stage* stage,
    const openusd_physics_extract_options* options,
    openusd_physics_extraction** extraction,
    openusd_physics_extract_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(error);
        if (extraction != nullptr)
        {
            *extraction = nullptr;
        }
        if (view != nullptr)
        {
            const uint32_t structSize = view->struct_size;
            const uint32_t version = view->version;
            view->data = nullptr;
            view->byte_size = 0;
            if (structSize != static_cast<uint32_t>(sizeof(openusd_physics_extract_view)) ||
                version != OPENUSD_PHYSICS_EXTRACT_VIEW_VERSION)
            {
                WriteError(error, "A physics extraction view of version 1 is required.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
        }
        if (stage == nullptr || !stage->value || extraction == nullptr || view == nullptr)
        {
            WriteError(error, "A valid stage, extraction output, and extraction view are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        openusd_physics_extract_options resolved{};
        resolved.struct_size = static_cast<uint32_t>(sizeof(openusd_physics_extract_options));
        resolved.version = OPENUSD_PHYSICS_EXTRACT_OPTIONS_VERSION;
        resolved.time_code = std::numeric_limits<double>::quiet_NaN();
        if (options != nullptr)
        {
            if (options->struct_size !=
                    static_cast<uint32_t>(sizeof(openusd_physics_extract_options)) ||
                options->version != OPENUSD_PHYSICS_EXTRACT_OPTIONS_VERSION)
            {
                WriteError(error, "Physics extraction options of version 1 are required.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            resolved = *options;
        }

        Extractor extractor(stage->value, resolved);
        extractor.Traverse();
        auto result = std::make_unique<openusd_physics_extraction>();
        result->data = extractor.Serialize();
        view->data = result->data.data();
        view->byte_size = result->data.size();
        *extraction = result.release();
        return OPENUSD_STATUS_OK;
    });
}

void openusd_physics_extraction_release(openusd_physics_extraction* extraction)
{
    delete extraction;
}

uint64_t openusd_physics_extract_get_traversal_count(void)
{
    return gTraversalCount.load(std::memory_order_relaxed);
}

uint64_t openusd_physics_extract_get_visited_prim_count(void)
{
    return gVisitedPrimCount.load(std::memory_order_relaxed);
}
