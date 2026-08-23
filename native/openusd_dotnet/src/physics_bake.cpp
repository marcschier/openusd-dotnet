// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

#include "pxr/base/gf/range3f.h"
#include "pxr/base/tf/fileUtils.h"
#include "pxr/base/tf/stringUtils.h"
#include "pxr/usd/usd/editContext.h"
#include "pxr/usd/usdGeom/pointBased.h"

#include <map>
#include <mutex>

/*
 * Batched physics preview and bake authoring.
 *
 * One call authors an entire chunk from a single pointer-free page, so neither the
 * preview path nor the bake path ever performs one managed/native transition per
 * simulated element. The page carries transforms and point samples together with the
 * prim-path string section they address; nothing in it is retained after the call.
 *
 * Transactions are explicit: begin captures a complete anonymous backup of the
 * destination layer, rollback restores it, and commit optionally saves only that layer.
 * The stage root layer is never reloaded and the caller's edit target is always restored
 * because every mutation runs inside a scoped UsdEditContext.
 */

namespace
{

static_assert(sizeof(openusd_physics_bake_page_header) == 72);
static_assert(sizeof(openusd_physics_bake_record) == 56);
static_assert(sizeof(openusd_physics_bake_result_header) == 32);
static_assert(sizeof(openusd_physics_bake_result_record) == 16);

// PermissionToSave is captured when the layer is opened, so an asset that became read
// only afterwards would still report as saveable. Preflight must not promise a save it
// cannot perform, so the backing file is probed directly as well.
bool IsAssetWritable(const SdfLayerHandle& layer)
{
    const std::string realPath = layer->GetRealPath();
    if (realPath.empty())
    {
        return true;
    }
    if (!TfPathExists(realPath))
    {
        return true;
    }
    return TfIsWritable(realPath);
}

const TfToken& SimulationIdentityToken()
{
    static const TfToken token("openUsdPhysics:simulation:identity");
    return token;
}

const TfToken& SimulationIndexToken()
{
    static const TfToken token("openUsdPhysics:simulation:identityIndex");
    return token;
}

const TfToken& SimulationRevisionToken()
{
    static const TfToken token("openUsdPhysics:simulation:sourceRevision");
    return token;
}

const TfToken& SimulationSleepingToken()
{
    static const TfToken token("openUsdPhysics:simulation:sleeping");
    return token;
}

const TfToken& PhysicsVelocityToken()
{
    static const TfToken token("physics:velocity");
    return token;
}

const TfToken& PhysicsAngularVelocityToken()
{
    static const TfToken token("physics:angularVelocity");
    return token;
}

const TfToken& PhysicsKinematicToken()
{
    static const TfToken token("physics:kinematicEnabled");
    return token;
}

const TfToken& TransformOpToken()
{
    static const TfToken token("xformOp:transform");
    return token;
}

const TfToken& ResetXformStackToken()
{
    static const TfToken token("!resetXformStack!");
    return token;
}

// Hard ceilings applied before any allocation or loop. A page is untrusted input: the
// element counts are 32 bit, so products such as point_count * 3 must be evaluated in 64
// bit and clamped, otherwise a wrapped product would satisfy a tiny float_count check and
// the authoring loop would then read far past the page.
constexpr uint64_t MaxPointsPerRecord = 64ull * 1024ull * 1024ull;
constexpr uint64_t MaxFacesPerRecord = 64ull * 1024ull * 1024ull;

// Every open bake transaction, keyed by an opaque token that never leaves this module.
// The backup must be held by a ref pointer: an anonymous layer that nothing else
// references would otherwise be destroyed before rollback could use it.
struct BakeTransaction
{
    SdfLayerRefPtr backup;
    // The resolved layer identifier, never the caller supplied string: two callers may
    // name the same layer differently and both must contend for the same reservation.
    std::string destination;
};

std::mutex& TransactionMutex()
{
    static std::mutex mutex;
    return mutex;
}

std::map<uint64_t, BakeTransaction>& Transactions()
{
    static std::map<uint64_t, BakeTransaction> transactions;
    return transactions;
}

uint64_t NextTransactionToken()
{
    static uint64_t next = 1;
    return next++;
}

// The registry doubles as the destination reservation table. Two overlapping
// transactions on one layer would each hold a backup taken at a different moment, so the
// second rollback could resurrect content the first one already committed.
bool IsDestinationReserved(const std::string& destination)
{
    for (const auto& entry : Transactions())
    {
        if (entry.second.destination == destination)
        {
            return true;
        }
    }
    return false;
}

// A validated, bounds-checked view over one authoring page.
struct PageView
{
    const openusd_physics_bake_page_header* header = nullptr;
    const openusd_physics_bake_record* records = nullptr;
    const char* strings = nullptr;
    const double* doubles = nullptr;
    const float* floats = nullptr;
    const int32_t* ints = nullptr;
    size_t stringCount = 0;
    size_t doubleCount = 0;
    size_t floatCount = 0;
    size_t intCount = 0;
};

bool SectionFits(uint32_t offset, uint64_t bytes, size_t pageSize, size_t alignment)
{
    if (bytes == 0)
    {
        return true;
    }
    if (offset % alignment != 0)
    {
        return false;
    }
    return static_cast<uint64_t>(offset) + bytes <= static_cast<uint64_t>(pageSize);
}

bool TryReadPage(const uint8_t* page, size_t pageSize, PageView* view, std::string* error)
{
    if (page == nullptr || pageSize < sizeof(openusd_physics_bake_page_header))
    {
        *error = "The authoring page is smaller than its header.";
        return false;
    }

    const auto* header = reinterpret_cast<const openusd_physics_bake_page_header*>(page);
    if (header->struct_size != sizeof(openusd_physics_bake_page_header) ||
        header->magic != OPENUSD_PHYSICS_BAKE_PAGE_MAGIC ||
        header->version != OPENUSD_PHYSICS_BAKE_PAGE_VERSION)
    {
        *error = "The authoring page header is not a supported physics bake page.";
        return false;
    }

    const uint64_t recordBytes =
        static_cast<uint64_t>(header->record_count) * sizeof(openusd_physics_bake_record);
    if (!SectionFits(header->record_offset, recordBytes, pageSize, alignof(openusd_physics_bake_record)) ||
        !SectionFits(header->string_offset, header->string_size, pageSize, 1) ||
        !SectionFits(header->double_offset,
            static_cast<uint64_t>(header->double_count) * sizeof(double), pageSize, alignof(double)) ||
        !SectionFits(header->float_offset,
            static_cast<uint64_t>(header->float_count) * sizeof(float), pageSize, alignof(float)) ||
        !SectionFits(header->int_offset,
            static_cast<uint64_t>(header->int_count) * sizeof(int32_t), pageSize, alignof(int32_t)))
    {
        *error = "An authoring page section does not fit inside the supplied page.";
        return false;
    }

    view->header = header;
    view->records = header->record_count == 0
        ? nullptr
        : reinterpret_cast<const openusd_physics_bake_record*>(page + header->record_offset);
    view->strings = header->string_size == 0
        ? nullptr
        : reinterpret_cast<const char*>(page + header->string_offset);
    view->doubles = header->double_count == 0
        ? nullptr
        : reinterpret_cast<const double*>(page + header->double_offset);
    view->floats = header->float_count == 0
        ? nullptr
        : reinterpret_cast<const float*>(page + header->float_offset);
    view->ints = header->int_count == 0
        ? nullptr
        : reinterpret_cast<const int32_t*>(page + header->int_offset);
    view->stringCount = header->string_size;
    view->doubleCount = header->double_count;
    view->floatCount = header->float_count;
    view->intCount = header->int_count;
    return true;
}

bool RecordFits(const PageView& view, const openusd_physics_bake_record& record)
{
    const uint64_t pathEnd =
        static_cast<uint64_t>(record.path_offset) + record.path_length;
    const uint64_t doubleEnd =
        static_cast<uint64_t>(record.double_offset) + record.double_count;
    const uint64_t floatEnd =
        static_cast<uint64_t>(record.float_offset) + record.float_count;
    const uint64_t intEnd = static_cast<uint64_t>(record.int_offset) + record.int_count;
    return record.path_length != 0 &&
        pathEnd <= view.stringCount &&
        doubleEnd <= view.doubleCount &&
        floatEnd <= view.floatCount &&
        intEnd <= view.intCount &&
        record.face_count <= record.int_count;
}

GfMatrix4d ReadMatrix(const double* values)
{
    GfMatrix4d matrix;
    for (int row = 0; row < 4; ++row)
    {
        for (int column = 0; column < 4; ++column)
        {
            matrix[row][column] = values[(row * 4) + column];
        }
    }
    return matrix;
}

struct RecordOutcome
{
    uint32_t status = OPENUSD_PHYSICS_BAKE_STATUS_APPLIED;
    uint32_t detail = 0;
};

// Resolves the world transform a prim will hold once the whole batch has been authored.
// A prim that is itself baked contributes its own target pose, and a prim between two
// baked ancestors follows the baked ancestor, so a child never composes against its
// parent's pre-bake pose. Because the answer depends only on the batch content and the
// stage state outside the batch, splitting a batch into more chunks cannot change the
// composed result.
struct TargetWorldResolver
{
    UsdGeomXformCache* cache = nullptr;
    const std::map<SdfPath, GfMatrix4d>* targets = nullptr;
    std::map<SdfPath, GfMatrix4d> memo;

    GfMatrix4d Resolve(const UsdPrim& prim)
    {
        if (!prim || prim.IsPseudoRoot())
        {
            return GfMatrix4d(1.0);
        }

        const SdfPath path = prim.GetPath();
        const auto cached = memo.find(path);
        if (cached != memo.end())
        {
            return cached->second;
        }

        GfMatrix4d world(1.0);
        const auto target = targets->find(path);
        if (target != targets->end())
        {
            world = target->second;
        }
        else
        {
            bool resetsXformStack = false;
            const GfMatrix4d local = cache->GetLocalTransformation(prim, &resetsXformStack);
            world = resetsXformStack ? local : local * Resolve(prim.GetParent());
        }

        memo[path] = world;
        return world;
    }
};

// Validates one record without mutating anything. Authoring runs only after every
// record in the page has been validated, so an atomic page never partially applies.
RecordOutcome ValidateRecord(
    const UsdStageRefPtr& stage,
    const SdfLayerHandle& layer,
    const PageView& view,
    const openusd_physics_bake_record& record,
    UsdPrim* primOut)
{
    RecordOutcome outcome;
    if (!RecordFits(view, record))
    {
        outcome.status = OPENUSD_PHYSICS_BAKE_STATUS_INVALID_RECORD;
        return outcome;
    }

    const std::string path(view.strings + record.path_offset, record.path_length);
    if (!SdfPath::IsValidPathString(path))
    {
        outcome.status = OPENUSD_PHYSICS_BAKE_STATUS_INVALID_RECORD;
        return outcome;
    }

    const SdfPath primPath(path);
    if (!primPath.IsAbsoluteRootOrPrimPath() || primPath.IsAbsoluteRootPath())
    {
        outcome.status = OPENUSD_PHYSICS_BAKE_STATUS_INVALID_RECORD;
        return outcome;
    }

    const UsdPrim prim = stage->GetPrimAtPath(primPath);
    if (!prim)
    {
        outcome.status = OPENUSD_PHYSICS_BAKE_STATUS_PATH_MISSING;
        return outcome;
    }
    if (prim.IsInstanceProxy())
    {
        outcome.status = OPENUSD_PHYSICS_BAKE_STATUS_INSTANCE_PROXY;
        return outcome;
    }
    if (prim.IsInPrototype())
    {
        outcome.status = OPENUSD_PHYSICS_BAKE_STATUS_IN_PROTOTYPE;
        return outcome;
    }

    if (record.kind == OPENUSD_PHYSICS_BAKE_KIND_TRANSFORM)
    {
        if (!UsdGeomXformable(prim))
        {
            outcome.status = OPENUSD_PHYSICS_BAKE_STATUS_NOT_XFORMABLE;
            return outcome;
        }
        const uint32_t required =
            (record.flags & OPENUSD_PHYSICS_BAKE_RECORD_VELOCITY) != 0 ? 22u : 16u;
        if (record.double_count != required)
        {
            outcome.status = OPENUSD_PHYSICS_BAKE_STATUS_SAMPLE_COUNT;
            outcome.detail = record.double_count;
            return outcome;
        }
    }
    else if (record.kind == OPENUSD_PHYSICS_BAKE_KIND_POINTS)
    {
        const UsdGeomPointBased pointBased(prim);
        if (!pointBased)
        {
            outcome.status = OPENUSD_PHYSICS_BAKE_STATUS_NOT_POINT_BASED;
            return outcome;
        }
        // Checked 64 bit arithmetic. point_count * 3 wraps in 32 bit for counts above
        // 0x55555555, which would let a page declare a huge point count while carrying
        // only a handful of floats.
        const uint64_t pointCount = record.point_count;
        const uint64_t coordinates = pointCount * 3ull;
        const uint64_t required =
            (record.flags & OPENUSD_PHYSICS_BAKE_RECORD_VELOCITY) != 0
                ? coordinates * 2ull
                : coordinates;
        if (pointCount == 0 || pointCount > MaxPointsPerRecord ||
            required > static_cast<uint64_t>(view.floatCount) ||
            static_cast<uint64_t>(record.float_count) != required)
        {
            outcome.status = OPENUSD_PHYSICS_BAKE_STATUS_SAMPLE_COUNT;
            outcome.detail = record.float_count;
            return outcome;
        }
        if ((record.flags & OPENUSD_PHYSICS_BAKE_RECORD_TOPOLOGY) == 0 && record.int_count != 0)
        {
            outcome.status = OPENUSD_PHYSICS_BAKE_STATUS_INVALID_RECORD;
            return outcome;
        }
        if ((record.flags & OPENUSD_PHYSICS_BAKE_RECORD_TOPOLOGY) != 0)
        {
            if (!UsdGeomMesh(prim))
            {
                outcome.status = OPENUSD_PHYSICS_BAKE_STATUS_NOT_POINT_BASED;
                return outcome;
            }
            if (static_cast<uint64_t>(record.face_count) > MaxFacesPerRecord)
            {
                outcome.status = OPENUSD_PHYSICS_BAKE_STATUS_SAMPLE_COUNT;
                outcome.detail = record.face_count;
                return outcome;
            }
            int64_t total = 0;
            for (uint32_t index = 0; index < record.face_count; ++index)
            {
                const int32_t count = view.ints[record.int_offset + index];
                if (count < 0)
                {
                    outcome.status = OPENUSD_PHYSICS_BAKE_STATUS_INVALID_RECORD;
                    return outcome;
                }
                total += count;
            }
            if (total != static_cast<int64_t>(record.int_count - record.face_count))
            {
                outcome.status = OPENUSD_PHYSICS_BAKE_STATUS_SAMPLE_COUNT;
                outcome.detail = record.int_count;
                return outcome;
            }
            for (uint32_t index = record.face_count; index < record.int_count; ++index)
            {
                const int32_t vertex = view.ints[record.int_offset + index];
                if (vertex < 0 || static_cast<uint64_t>(vertex) >= pointCount)
                {
                    outcome.status = OPENUSD_PHYSICS_BAKE_STATUS_SAMPLE_COUNT;
                    outcome.detail = static_cast<uint32_t>(vertex < 0 ? 0 : vertex);
                    return outcome;
                }
            }
        }
        else
        {
            // Without topology the authored point count must match the composed topology,
            // otherwise the destination would compose into an inconsistent mesh.
            VtVec3fArray existing;
            if (pointBased.GetPointsAttr().Get(&existing, UsdTimeCode::Default()) &&
                !existing.empty() && existing.size() != record.point_count)
            {
                outcome.status = OPENUSD_PHYSICS_BAKE_STATUS_SAMPLE_COUNT;
                outcome.detail = static_cast<uint32_t>(existing.size());
                return outcome;
            }
        }
    }
    else
    {
        outcome.status = OPENUSD_PHYSICS_BAKE_STATUS_UNSUPPORTED_KIND;
        return outcome;
    }

    const uint32_t pageFlags = view.header->flags;
    if ((pageFlags & OPENUSD_PHYSICS_BAKE_PAGE_TIME_SAMPLE) != 0 &&
        (pageFlags &
            (OPENUSD_PHYSICS_BAKE_PAGE_REJECT_EXISTING_SAMPLE |
             OPENUSD_PHYSICS_BAKE_PAGE_SKIP_EXISTING_SAMPLE)) != 0)
    {
        const TfToken name = record.kind == OPENUSD_PHYSICS_BAKE_KIND_TRANSFORM
            ? TransformOpToken()
            : UsdGeomTokens->points;
        if (layer->QueryTimeSample(primPath.AppendProperty(name), view.header->time_code))
        {
            outcome.status = (pageFlags & OPENUSD_PHYSICS_BAKE_PAGE_SKIP_EXISTING_SAMPLE) != 0
                ? OPENUSD_PHYSICS_BAKE_STATUS_SKIPPED
                : OPENUSD_PHYSICS_BAKE_STATUS_EXISTING_SAMPLE;
            return outcome;
        }
    }

    *primOut = prim;
    return outcome;
}

// Authors one already validated record. Returns the number of authored attributes, or a
// negative value when authoring failed.
int AuthorRecord(
    const PageView& view,
    const openusd_physics_bake_record& record,
    const UsdPrim& prim,
    const UsdTimeCode& time,
    const GfMatrix4d& parentInverse)
{
    const uint32_t pageFlags = view.header->flags;
    int authored = 0;

    if (record.kind == OPENUSD_PHYSICS_BAKE_KIND_TRANSFORM)
    {
        UsdGeomXformable xformable(prim);
        GfMatrix4d matrix = ReadMatrix(view.doubles + record.double_offset);
        const bool resetStack = (pageFlags & OPENUSD_PHYSICS_BAKE_PAGE_RESET_XFORM_STACK) != 0;
        if (!resetStack)
        {
            matrix = matrix * parentInverse;
        }

        UsdAttribute transformAttribute = prim.CreateAttribute(
            TransformOpToken(), SdfValueTypeNames->Matrix4d, false, SdfVariabilityVarying);
        if (!transformAttribute || !transformAttribute.Set(matrix, time))
        {
            return -1;
        }
        ++authored;

        VtTokenArray order;
        if (resetStack)
        {
            order.push_back(ResetXformStackToken());
        }
        order.push_back(TransformOpToken());
        if (!xformable.CreateXformOpOrderAttr().Set(order, UsdTimeCode::Default()))
        {
            return -1;
        }
        ++authored;

        if ((record.flags & OPENUSD_PHYSICS_BAKE_RECORD_VELOCITY) != 0)
        {
            const double* velocity = view.doubles + record.double_offset + 16;
            const GfVec3f linear(
                static_cast<float>(velocity[0]),
                static_cast<float>(velocity[1]),
                static_cast<float>(velocity[2]));
            const GfVec3f angular(
                static_cast<float>(velocity[3]),
                static_cast<float>(velocity[4]),
                static_cast<float>(velocity[5]));
            UsdAttribute linearAttribute = prim.CreateAttribute(
                PhysicsVelocityToken(), SdfValueTypeNames->Vector3f, false, SdfVariabilityVarying);
            UsdAttribute angularAttribute = prim.CreateAttribute(
                PhysicsAngularVelocityToken(),
                SdfValueTypeNames->Vector3f,
                false,
                SdfVariabilityVarying);
            if (!linearAttribute || !linearAttribute.Set(linear, time) ||
                !angularAttribute || !angularAttribute.Set(angular, time))
            {
                return -1;
            }
            authored += 2;
        }

        if ((record.flags & OPENUSD_PHYSICS_BAKE_RECORD_KINEMATIC) != 0)
        {
            UsdAttribute kinematic = prim.CreateAttribute(
                PhysicsKinematicToken(), SdfValueTypeNames->Bool, false, SdfVariabilityVarying);
            if (!kinematic || !kinematic.Set(true, UsdTimeCode::Default()))
            {
                return -1;
            }
            ++authored;
        }
    }
    else
    {
        const float* points = view.floats + record.float_offset;
        VtVec3fArray pointArray(record.point_count);
        GfRange3f bounds;
        for (uint32_t index = 0; index < record.point_count; ++index)
        {
            const GfVec3f point(
                points[(index * 3) + 0], points[(index * 3) + 1], points[(index * 3) + 2]);
            pointArray[index] = point;
            bounds.UnionWith(point);
        }

        UsdGeomPointBased pointBased(prim);
        if (!pointBased.CreatePointsAttr().Set(pointArray, time))
        {
            return -1;
        }
        ++authored;

        if ((record.flags & OPENUSD_PHYSICS_BAKE_RECORD_VELOCITY) != 0)
        {
            const float* velocities = points + (record.point_count * 3);
            VtVec3fArray velocityArray(record.point_count);
            for (uint32_t index = 0; index < record.point_count; ++index)
            {
                velocityArray[index] = GfVec3f(
                    velocities[(index * 3) + 0],
                    velocities[(index * 3) + 1],
                    velocities[(index * 3) + 2]);
            }
            if (!pointBased.CreateVelocitiesAttr().Set(velocityArray, time))
            {
                return -1;
            }
            ++authored;
        }

        if ((record.flags & OPENUSD_PHYSICS_BAKE_RECORD_TOPOLOGY) != 0)
        {
            UsdGeomMesh mesh(prim);
            VtIntArray counts(record.face_count);
            for (uint32_t index = 0; index < record.face_count; ++index)
            {
                counts[index] = view.ints[record.int_offset + index];
            }
            const uint32_t indexCount = record.int_count - record.face_count;
            VtIntArray indices(indexCount);
            for (uint32_t index = 0; index < indexCount; ++index)
            {
                indices[index] = view.ints[record.int_offset + record.face_count + index];
            }
            if (!mesh.CreateFaceVertexCountsAttr().Set(counts, time) ||
                !mesh.CreateFaceVertexIndicesAttr().Set(indices, time))
            {
                return -1;
            }
            authored += 2;
        }

        if ((pageFlags & OPENUSD_PHYSICS_BAKE_PAGE_EXTENT) != 0)
        {
            VtVec3fArray extent(2);
            extent[0] = bounds.GetMin();
            extent[1] = bounds.GetMax();
            if (!pointBased.CreateExtentAttr().Set(extent, time))
            {
                return -1;
            }
            ++authored;
        }
    }

    if ((pageFlags & OPENUSD_PHYSICS_BAKE_PAGE_SIMULATION_METADATA) != 0)
    {
        // Project-owned state that standard USD cannot express is authored in the
        // openUsdPhysics simulation namespace instead of being silently dropped.
        UsdAttribute identity = prim.CreateAttribute(
            SimulationIdentityToken(), SdfValueTypeNames->Int64, true, SdfVariabilityUniform);
        UsdAttribute kind = prim.CreateAttribute(
            SimulationIndexToken(), SdfValueTypeNames->Int64, true, SdfVariabilityUniform);
        UsdAttribute revision = prim.CreateAttribute(
            SimulationRevisionToken(), SdfValueTypeNames->String, true, SdfVariabilityUniform);
        const std::string revisionText = TfStringPrintf("%u", view.header->revision);
        if (!identity || !identity.Set(static_cast<int64_t>(record.id), UsdTimeCode::Default()) ||
            !kind || !kind.Set(static_cast<int64_t>(record.kind), UsdTimeCode::Default()) ||
            !revision || !revision.Set(revisionText, UsdTimeCode::Default()))
        {
            return -1;
        }
        authored += 3;

        if (record.kind == OPENUSD_PHYSICS_BAKE_KIND_TRANSFORM)
        {
            UsdAttribute sleeping = prim.CreateAttribute(
                SimulationSleepingToken(), SdfValueTypeNames->Bool, true, SdfVariabilityVarying);
            const bool asleep = (record.flags & OPENUSD_PHYSICS_BAKE_RECORD_SLEEPING) != 0;
            if (!sleeping || !sleeping.Set(asleep, time))
            {
                return -1;
            }
            ++authored;
        }
    }

    return authored;
}

} // namespace

openusd_status openusd_stage_physics_bake_describe_layer(
    const openusd_stage* stage,
    const char* layer_identifier,
    uint64_t* flags,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(flags);
        if (stage == nullptr || !stage->value || flags == nullptr ||
            layer_identifier == nullptr || layer_identifier[0] == '\0')
        {
            WriteError(error, "A valid stage, layer identifier, and output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const std::string identifier(layer_identifier);
            uint64_t result = 0;
            if (stage->value->IsLayerMuted(identifier))
            {
                result |= OPENUSD_PHYSICS_BAKE_LAYER_MUTED;
            }

            SdfLayerHandle layer = FindLayerInStack(stage, identifier);
            if (layer)
            {
                result |= OPENUSD_PHYSICS_BAKE_LAYER_LOCAL;
            }
            else
            {
                layer = SdfLayer::Find(identifier);
            }
            if (!layer)
            {
                *flags = result;
                return OPENUSD_STATUS_OK;
            }

            result |= OPENUSD_PHYSICS_BAKE_LAYER_FOUND;
            if (layer->IsAnonymous())
            {
                result |= OPENUSD_PHYSICS_BAKE_LAYER_ANONYMOUS;
            }
            else if (!layer->GetRealPath().empty())
            {
                result |= OPENUSD_PHYSICS_BAKE_LAYER_FILE_BACKED;
            }
            if (layer->PermissionToEdit())
            {
                result |= OPENUSD_PHYSICS_BAKE_LAYER_EDITABLE;
            }
            if (layer->PermissionToSave() && IsAssetWritable(layer))
            {
                result |= OPENUSD_PHYSICS_BAKE_LAYER_SAVEABLE;
            }
            if (layer == stage->value->GetRootLayer())
            {
                result |= OPENUSD_PHYSICS_BAKE_LAYER_ROOT;
            }
            if (layer == stage->value->GetSessionLayer())
            {
                result |= OPENUSD_PHYSICS_BAKE_LAYER_SESSION;
            }
            if (layer->IsDirty())
            {
                result |= OPENUSD_PHYSICS_BAKE_LAYER_DIRTY;
            }
            *flags = result;
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_physics_bake_begin(
    const openusd_stage* stage,
    const char* layer_identifier,
    uint64_t* transaction,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(transaction);
        if (stage == nullptr || !stage->value || transaction == nullptr ||
            layer_identifier == nullptr || layer_identifier[0] == '\0')
        {
            WriteError(error, "A valid stage, layer identifier, and output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const std::string identifier(layer_identifier);
            const SdfLayerHandle layer = FindLayerInStack(stage, identifier);
            if (!layer)
            {
                WriteError(error, "The destination layer is not in the stage local layer stack.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            const std::string destination = layer->GetIdentifier();

            {
                std::lock_guard<std::mutex> lock(TransactionMutex());
                if (IsDestinationReserved(destination))
                {
                    WriteError(error,
                        "A bake transaction is already open for this destination layer.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
                }
            }

            SdfLayerRefPtr backup = SdfLayer::CreateAnonymous("physics-bake-backup");
            if (!backup)
            {
                WriteError(error, "Could not create the bake rollback backup layer.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }

            TfErrorMark mark;
            backup->TransferContent(layer);
            if (!mark.IsClean())
            {
                WriteError(error, ConsumeErrors(mark));
                return OPENUSD_STATUS_NATIVE_ERROR;
            }

            std::lock_guard<std::mutex> lock(TransactionMutex());
            // Re-checked under the same lock that publishes the entry so two concurrent
            // begins cannot both observe a free destination and both reserve it.
            if (IsDestinationReserved(destination))
            {
                WriteError(error,
                    "A bake transaction is already open for this destination layer.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            const uint64_t token = NextTransactionToken();
            Transactions()[token] = BakeTransaction{ backup, destination };
            *transaction = token;
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_physics_bake_rollback(
    const openusd_stage* stage,
    const char* layer_identifier,
    uint64_t transaction,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || transaction == 0 ||
            layer_identifier == nullptr || layer_identifier[0] == '\0')
        {
            WriteError(error, "A valid stage, layer identifier, and transaction are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const std::string identifier(layer_identifier);
            const SdfLayerHandle layer = FindLayerInStack(stage, identifier);
            if (!layer)
            {
                WriteError(error, "The destination layer is no longer in the stage layer stack.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            const std::string destination = layer->GetIdentifier();

            SdfLayerRefPtr backup;
            {
                std::lock_guard<std::mutex> lock(TransactionMutex());
                const auto entry = Transactions().find(transaction);
                if (entry == Transactions().end() || entry->second.destination != destination)
                {
                    WriteError(error, "The bake transaction is not open for this layer.");
                    return OPENUSD_STATUS_NOT_FOUND;
                }
                backup = entry->second.backup;
                // The reservation is released here: even if the restore below reports
                // errors the transaction is finished and must never block a retry.
                Transactions().erase(entry);
            }

            TfErrorMark mark;
            layer->TransferContent(backup);
            if (!mark.IsClean())
            {
                WriteError(error, ConsumeErrors(mark));
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_physics_bake_commit(
    const openusd_stage* stage,
    const char* layer_identifier,
    uint64_t transaction,
    uint32_t save,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || transaction == 0 ||
            layer_identifier == nullptr || layer_identifier[0] == '\0')
        {
            WriteError(error, "A valid stage, layer identifier, and transaction are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const std::string identifier(layer_identifier);
            const SdfLayerHandle layer = FindLayerInStack(stage, identifier);
            if (!layer)
            {
                WriteError(error, "The destination layer is no longer in the stage layer stack.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            const std::string destination = layer->GetIdentifier();

            {
                std::lock_guard<std::mutex> lock(TransactionMutex());
                const auto entry = Transactions().find(transaction);
                if (entry == Transactions().end() || entry->second.destination != destination)
                {
                    WriteError(error, "The bake transaction is not open for this layer.");
                    return OPENUSD_STATUS_NOT_FOUND;
                }
            }

            if (save != 0)
            {
                TfErrorMark mark;
                // Only the destination layer is ever saved; the root layer is never
                // saved or reloaded by a bake.
                if (!layer->Save() || !mark.IsClean())
                {
                    const std::string message = ConsumeErrors(mark);
                    // The transaction deliberately stays open and the layer content is
                    // left untouched. Commit is therefore the only step that failed, and
                    // the caller's rollback still finds its backup and restores it, so
                    // the reported outcome is never "failed but not rolled back".
                    WriteError(error, message.empty()
                        ? "The destination layer could not be saved."
                        : message);
                    return OPENUSD_STATUS_NATIVE_ERROR;
                }
            }

            std::lock_guard<std::mutex> lock(TransactionMutex());
            Transactions().erase(transaction);
            return OPENUSD_STATUS_OK;
        });

    });
}

void openusd_stage_physics_bake_release(uint64_t transaction)
{
    if (transaction == 0)
    {
        return;
    }
    try
    {
        std::lock_guard<std::mutex> lock(TransactionMutex());
        Transactions().erase(transaction);
    }
    catch (...)
    {
        // Release must never throw across the ABI boundary.
    }
}

openusd_status openusd_stage_physics_bake_clear_layer(
    const openusd_stage* stage,
    const char* layer_identifier,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value ||
            layer_identifier == nullptr || layer_identifier[0] == '\0')
        {
            WriteError(error, "A valid stage and layer identifier are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            const std::string identifier(layer_identifier);
            const SdfLayerHandle layer = FindLayerInStack(stage, identifier);
            if (!layer)
            {
                WriteError(error, "The layer is not in the stage local layer stack.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (layer == stage->value->GetRootLayer() || layer == stage->value->GetSessionLayer())
            {
                WriteError(error, "The stage root and session layers are never cleared.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            if (!layer->PermissionToEdit())
            {
                WriteError(error, "The layer does not permit editing.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }

            TfErrorMark mark;
            // Clearing drops only the opinions this layer holds; every weaker layer, the
            // session layer, and the root layer keep their authored opinions.
            layer->Clear();
            if (!mark.IsClean())
            {
                WriteError(error, ConsumeErrors(mark));
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_physics_bake_author_page(
    const openusd_stage* stage,
    const char* layer_identifier,
    const uint8_t* page,
    size_t page_size,
    uint8_t* results,
    size_t results_size,
    size_t* required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(required);
        if (stage == nullptr || !stage->value || required == nullptr || page == nullptr ||
            layer_identifier == nullptr || layer_identifier[0] == '\0')
        {
            WriteError(error, "A valid stage, layer identifier, page, and output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]() -> openusd_status
        {
            PageView view;
            std::string parseError;
            if (!TryReadPage(page, page_size, &view, &parseError))
            {
                WriteError(error, parseError);
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }

            const size_t requiredSize = sizeof(openusd_physics_bake_result_header) +
                (static_cast<size_t>(view.header->record_count) *
                    sizeof(openusd_physics_bake_result_record));
            *required = requiredSize;
            if (results == nullptr || results_size < requiredSize)
            {
                return OPENUSD_STATUS_BUFFER_TOO_SMALL;
            }

            const std::string identifier(layer_identifier);
            const SdfLayerHandle layer = FindLayerInStack(stage, identifier);
            if (!layer)
            {
                WriteError(error, "The destination layer is not in the stage local layer stack.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if ((view.header->flags & OPENUSD_PHYSICS_BAKE_PAGE_FORBID_ROOT_LAYER) != 0 &&
                (layer == stage->value->GetRootLayer() ||
                 layer == stage->value->GetSessionLayer()))
            {
                WriteError(error, "A physics preview never authors into the root or session layer.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            if (stage->value->IsLayerMuted(identifier))
            {
                WriteError(error, "The destination layer is muted.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            if (!layer->PermissionToEdit())
            {
                WriteError(error, "The destination layer does not permit editing.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }

            auto* header = reinterpret_cast<openusd_physics_bake_result_header*>(results);
            auto* records = reinterpret_cast<openusd_physics_bake_result_record*>(
                results + sizeof(openusd_physics_bake_result_header));
            header->struct_size = sizeof(openusd_physics_bake_result_header);
            header->magic = OPENUSD_PHYSICS_BAKE_RESULT_MAGIC;
            header->version = OPENUSD_PHYSICS_BAKE_RESULT_VERSION;
            header->record_count = view.header->record_count;
            header->applied_count = 0;
            header->skipped_count = 0;
            header->rejected_count = 0;
            header->authored_count = 0;

            std::vector<UsdPrim> prims(view.header->record_count);
            std::vector<GfMatrix4d> parentInverses(
                view.header->record_count, GfMatrix4d(1.0));
            const bool timeSample =
                (view.header->flags & OPENUSD_PHYSICS_BAKE_PAGE_TIME_SAMPLE) != 0;
            const UsdTimeCode time = timeSample
                ? UsdTimeCode(view.header->time_code)
                : UsdTimeCode::Default();

            bool rejected = false;
            for (uint32_t index = 0; index < view.header->record_count; ++index)
            {
                const openusd_physics_bake_record& record = view.records[index];
                UsdPrim prim;
                const RecordOutcome outcome =
                    ValidateRecord(stage->value, layer, view, record, &prim);
                records[index].id = record.id;
                records[index].status = outcome.status;
                records[index].detail = outcome.detail;
                if (outcome.status == OPENUSD_PHYSICS_BAKE_STATUS_APPLIED)
                {
                    prims[index] = prim;
                }
                else if (outcome.status == OPENUSD_PHYSICS_BAKE_STATUS_SKIPPED)
                {
                    ++header->skipped_count;
                }
                else
                {
                    ++header->rejected_count;
                    rejected = true;
                }
            }

            const bool atomic = (view.header->flags & OPENUSD_PHYSICS_BAKE_PAGE_ATOMIC) != 0;
            if ((view.header->flags & OPENUSD_PHYSICS_BAKE_PAGE_PREFLIGHT_ONLY) != 0 ||
                (atomic && rejected))
            {
                return rejected ? OPENUSD_STATUS_INVALID_ARGUMENT : OPENUSD_STATUS_OK;
            }

            if ((view.header->flags & OPENUSD_PHYSICS_BAKE_PAGE_RESET_XFORM_STACK) == 0)
            {
                // Parent poses are resolved against the transforms the batch is going to
                // produce, not against the poses that happen to be composed right now.
                // A parent and its child may therefore appear in the same page, in either
                // order, or be split across chunks, and still compose to the same world
                // transform.
                std::map<SdfPath, GfMatrix4d> targets;
                for (uint32_t index = 0; index < view.header->record_count; ++index)
                {
                    if (!prims[index] ||
                        view.records[index].kind != OPENUSD_PHYSICS_BAKE_KIND_TRANSFORM)
                    {
                        continue;
                    }
                    targets.emplace(
                        prims[index].GetPath(),
                        ReadMatrix(view.doubles + view.records[index].double_offset));
                }

                UsdGeomXformCache cache(time);
                TargetWorldResolver resolver;
                resolver.cache = &cache;
                resolver.targets = &targets;
                for (uint32_t index = 0; index < view.header->record_count; ++index)
                {
                    if (!prims[index] ||
                        view.records[index].kind != OPENUSD_PHYSICS_BAKE_KIND_TRANSFORM)
                    {
                        continue;
                    }
                    const UsdPrim parent = prims[index].GetParent();
                    if (parent && !parent.IsPseudoRoot())
                    {
                        parentInverses[index] = resolver.Resolve(parent).GetInverse();
                    }
                }
            }

            TfErrorMark mark;
            {
                // The caller's edit target is restored by this scope on every path,
                // including when authoring throws.
                UsdEditContext editContext(stage->value, UsdEditTarget(layer));
                for (uint32_t index = 0; index < view.header->record_count; ++index)
                {
                    if (!prims[index])
                    {
                        continue;
                    }
                    const int authored = AuthorRecord(
                        view, view.records[index], prims[index], time, parentInverses[index]);
                    if (authored < 0)
                    {
                        records[index].status = OPENUSD_PHYSICS_BAKE_STATUS_AUTHORING_FAILED;
                        ++header->rejected_count;
                        rejected = true;
                        if (atomic)
                        {
                            break;
                        }
                        continue;
                    }
                    header->authored_count += static_cast<uint32_t>(authored);
                    ++header->applied_count;
                }
            }

            if (!mark.IsClean())
            {
                const std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty()
                    ? "Authoring the physics page reported errors."
                    : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return rejected ? OPENUSD_STATUS_INVALID_ARGUMENT : OPENUSD_STATUS_OK;
        });

    });
}
