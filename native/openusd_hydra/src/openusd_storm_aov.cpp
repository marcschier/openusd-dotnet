// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_storm_aov_internal.h"

#include "pxr/base/tf/errorMark.h"
#include "pxr/imaging/garch/glApi.h"
#include "pxr/imaging/hd/renderBuffer.h"
#include "pxr/imaging/hd/tokens.h"
#include "pxr/imaging/hdSt/hgiConversions.h"
#include "pxr/imaging/hgi/texture.h"
#include "pxr/usdImaging/usdImagingGL/engine.h"

#include <algorithm>
#include <cmath>
#include <cstring>
#include <limits>
#include <vector>

PXR_NAMESPACE_USING_DIRECTIVE

struct openusd_storm_aov_owner
{
    openusd_storm_aov_owner() = default;
    openusd_storm_aov_owner(const openusd_storm_aov_owner&) = delete;
    openusd_storm_aov_owner& operator=(const openusd_storm_aov_owner&) = delete;

    openusd_storm_aov_view view{};
    std::array<openusd_storm_aov_output, OPENUSD_STORM_AOV_MAX_OUTPUTS> outputs{};
    std::vector<uint64_t> pixels;
    std::vector<openusd_storm_aov_identity> identities;
    std::vector<openusd_storm_aov_instance_context> contexts;
    std::vector<char> text;
    std::vector<uint32_t> identity_indices;
    std::shared_ptr<std::atomic_bool> owner_live;
};

namespace
{
constexpr uint64_t ControlAllowance = 16384;
constexpr uint64_t ReadbackAllowance = 4096;

struct IdentitySlot
{
    uint64_t key = 0;
    uint32_t index = OPENUSD_STORM_AOV_BACKGROUND_INDEX;
    uint32_t reserved = 0;
};

uint32_t LookupSlots(uint32_t identities) noexcept
{
    uint32_t slots = 1;
    while (slots < identities * 2)
    {
        slots *= 2;
    }
    return slots;
}

uint64_t IdentityWorkingBytes(const openusd_storm_aov_request& request) noexcept
{
    if ((request.flags & OPENUSD_STORM_AOV_REQUEST_IDENTITIES) == 0)
    {
        return 0;
    }
    return static_cast<uint64_t>(request.width) *
            static_cast<uint64_t>(request.height) * sizeof(uint32_t) +
        static_cast<uint64_t>(request.max_unique_identities) *
            sizeof(openusd_storm_aov_identity) +
        static_cast<uint64_t>(request.max_instance_contexts) *
            sizeof(openusd_storm_aov_instance_context) +
        request.max_text_bytes +
        static_cast<uint64_t>(LookupSlots(request.max_unique_identities)) *
            sizeof(IdentitySlot);
}

uint64_t Align8(uint64_t bytes) noexcept
{
    return (bytes + 7) & ~UINT64_C(7);
}

struct Format
{
    HdFormat native_format = HdFormatInvalid;
    uint32_t format = OPENUSD_STORM_AOV_FORMAT_NONE;
    uint32_t bytes = 0;
};

Format OutputFormat(uint32_t kind) noexcept
{
    switch (kind)
    {
    case OPENUSD_STORM_AOV_COLOR:
        return {HdFormatFloat16Vec4, OPENUSD_STORM_AOV_FORMAT_FLOAT16_VEC4, 8};
    case OPENUSD_STORM_AOV_DEPTH:
        return {HdFormatFloat32, OPENUSD_STORM_AOV_FORMAT_FLOAT32, 4};
    case OPENUSD_STORM_AOV_PRIM_ID:
    case OPENUSD_STORM_AOV_INSTANCE_ID:
    case OPENUSD_STORM_AOV_ELEMENT_ID:
        return {HdFormatInt32, OPENUSD_STORM_AOV_FORMAT_INT32, 4};
    case OPENUSD_STORM_AOV_NEYE:
        return {HdFormatUNorm8Vec4, OPENUSD_STORM_AOV_FORMAT_UNORM8_VEC4, 4};
    default:
        return {};
    }
}

TfToken OutputName(uint32_t kind)
{
    switch (kind)
    {
    case OPENUSD_STORM_AOV_COLOR: return HdAovTokens->color;
    case OPENUSD_STORM_AOV_DEPTH: return HdAovTokens->depth;
    case OPENUSD_STORM_AOV_PRIM_ID: return HdAovTokens->primId;
    case OPENUSD_STORM_AOV_INSTANCE_ID: return HdAovTokens->instanceId;
    case OPENUSD_STORM_AOV_ELEMENT_ID: return HdAovTokens->elementId;
    case OPENUSD_STORM_AOV_NEYE: return HdAovTokens->Neye;
    default: return TfToken();
    }
}

uint64_t RetainedScratch(const OpenUsdStormAovState& state) noexcept
{
    uint64_t bytes = 0;
    for (uint64_t value : state.scratch_high_water)
    {
        bytes += value;
    }
    return bytes;
}

uint64_t WorkingBytes(
    const OpenUsdStormAovState& state,
    const openusd_storm_aov_request& request,
    uint64_t pixel_bytes,
    uint64_t replacement_scratch) noexcept
{
    // HdSt retains Map's CPU allocation after Unmap, and the next Map allocates
    // its replacement before releasing the old allocation. Keep a conservative
    // per-kind high-water even when task-controller changes may have freed it.
    return sizeof(openusd_storm_aov_owner) + ControlAllowance + Align8(pixel_bytes) +
        RetainedScratch(state) + replacement_scratch + IdentityWorkingBytes(request);
}

class ScopedMap final
{
public:
    explicit ScopedMap(HdRenderBuffer& buffer) : _buffer(buffer)
    {
    }

    const void* Map()
    {
        _buffer.Resolve();
        _attempted = true;
        return _buffer.Map();
    }

    ~ScopedMap()
    {
        if (_attempted)
        {
            _buffer.Unmap();
        }
    }

    ScopedMap(const ScopedMap&) = delete;
    ScopedMap& operator=(const ScopedMap&) = delete;

private:
    HdRenderBuffer& _buffer;
    bool _attempted = false;
};

const openusd_storm_aov_output* FindOutput(
    const openusd_storm_aov_owner& owner, uint32_t output_count, uint32_t kind) noexcept
{
    for (uint32_t index = 0; index < output_count; ++index)
    {
        if (owner.outputs[index].kind == kind)
        {
            return &owner.outputs[index];
        }
    }
    return nullptr;
}

int32_t ReadId(const uint8_t* bytes, uint64_t pixel) noexcept
{
    int32_t value;
    std::memcpy(&value, bytes + pixel * sizeof(value), sizeof(value));
    return value;
}

uint32_t PairSlot(uint64_t key, uint32_t mask) noexcept
{
    key ^= key >> 33;
    key *= UINT64_C(0xff51afd7ed558ccd);
    key ^= key >> 33;
    return static_cast<uint32_t>(key) & mask;
}

bool CanonicalPrimPath(const SdfPath& path) noexcept
{
    return path.IsAbsolutePath() && path.IsPrimPath() &&
        !path.ContainsPrimVariantSelection();
}

void CopyPath(
    const SdfPath& path,
    openusd_storm_aov_owner& owner,
    uint32_t& offset,
    uint32_t& length)
{
    const std::string& text = path.GetString();
    length = static_cast<uint32_t>(text.size());
    offset = length == 0 ? 0 : owner.view.text_byte_count;
    if (length != 0)
    {
        std::memcpy(owner.text.data() + offset, text.data(), length);
        owner.view.text_byte_count += length;
    }
}

openusd_status BuildIdentities(
    UsdImagingGLEngine& engine,
    const openusd_storm_aov_request& request,
    openusd_storm_aov_owner& owner,
    std::string& message)
{
    if ((request.flags & OPENUSD_STORM_AOV_REQUEST_IDENTITIES) == 0)
    {
        return OPENUSD_STATUS_OK;
    }
    const auto* prim = FindOutput(owner, request.output_count, OPENUSD_STORM_AOV_PRIM_ID);
    const auto* instance =
        FindOutput(owner, request.output_count, OPENUSD_STORM_AOV_INSTANCE_ID);
    if (prim == nullptr || instance == nullptr)
    {
        message = "The admitted identity request is missing its ID output descriptors.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    if (prim->status != OPENUSD_STORM_AOV_STATUS_READY ||
        instance->status != OPENUSD_STORM_AOV_STATUS_READY)
    {
        owner.view.identity_status = OPENUSD_STORM_AOV_STATUS_ABSENT;
        return OPENUSD_STATUS_OK;
    }
    owner.identities.resize(request.max_unique_identities);
    owner.contexts.resize(request.max_instance_contexts);
    owner.text.resize(request.max_text_bytes);
    const uint64_t pixels =
        static_cast<uint64_t>(request.width) * static_cast<uint64_t>(request.height);
    owner.identity_indices.resize(
        static_cast<size_t>(pixels), OPENUSD_STORM_AOV_BACKGROUND_INDEX);
    std::vector<IdentitySlot> lookup(LookupSlots(request.max_unique_identities));
    const uint32_t mask = static_cast<uint32_t>(lookup.size() - 1);
    const auto* bytes = reinterpret_cast<const uint8_t*>(owner.pixels.data());
    for (uint64_t pixel = 0; pixel < pixels; ++pixel)
    {
        const int32_t prim_id = ReadId(bytes + prim->data_offset, pixel);
        if (prim_id == -1)
        {
            continue;
        }
        const int32_t instance_id = ReadId(bytes + instance->data_offset, pixel);
        const uint64_t key =
            (static_cast<uint64_t>(static_cast<uint32_t>(prim_id)) << 32) |
            static_cast<uint32_t>(instance_id);
        uint32_t slot = PairSlot(key, mask);
        while (lookup[slot].index != OPENUSD_STORM_AOV_BACKGROUND_INDEX &&
               lookup[slot].key != key)
        {
            slot = (slot + 1) & mask;
        }
        if (lookup[slot].index == OPENUSD_STORM_AOV_BACKGROUND_INDEX)
        {
            if (owner.view.identity_count == request.max_unique_identities)
            {
                message = "Storm unique identity limit exceeded before native ID decoding.";
                return OPENUSD_STATUS_BUFFER_TOO_SMALL;
            }
            const uint32_t index = owner.view.identity_count++;
            lookup[slot].key = key;
            lookup[slot].index = index;
            auto& identity = owner.identities[index];
            identity.struct_size = sizeof(identity);
            identity.version = OPENUSD_STORM_AOV_VERSION;
            identity.status = OPENUSD_STORM_AOV_IDENTITY_UNRESOLVED;
            identity.prim_id = prim_id;
            identity.instance_id = instance_id;
            identity.instance_index = -1;
        }
        owner.identity_indices[static_cast<size_t>(pixel)] = lookup[slot].index;
    }

    for (uint32_t index = 0; index < owner.view.identity_count; ++index)
    {
        auto& identity = owner.identities[index];
        SdfPath prim_path;
        SdfPath instancer_path;
        int instance_index = -1;
        HdInstancerContext context;
        if (!engine.DecodeIntersection(
                identity.prim_id, identity.instance_id,
                &prim_path, &instancer_path, &instance_index, &context) ||
            !CanonicalPrimPath(prim_path) ||
            (!instancer_path.IsEmpty() && !CanonicalPrimPath(instancer_path)))
        {
            continue;
        }
        bool valid_context = true;
        for (const auto& entry : context)
        {
            valid_context = valid_context &&
                CanonicalPrimPath(entry.first) && entry.second >= 0;
        }
        if (!valid_context ||
            ((!instancer_path.IsEmpty() || !context.empty()) && instance_index < 0))
        {
            continue;
        }
        if (context.size() > request.max_instance_contexts - owner.view.context_count)
        {
            message = "Storm decoded instance context exceeds the output limit.";
            return OPENUSD_STATUS_BUFFER_TOO_SMALL;
        }
        const uint64_t remaining_text =
            request.max_text_bytes - owner.view.text_byte_count;
        uint64_t text_bytes = prim_path.GetString().size();
        if (text_bytes > remaining_text ||
            instancer_path.GetString().size() > remaining_text - text_bytes)
        {
            message = "Storm decoded paths exceed the UTF-8 output limit.";
            return OPENUSD_STATUS_BUFFER_TOO_SMALL;
        }
        text_bytes += instancer_path.GetString().size();
        for (const auto& entry : context)
        {
            if (entry.first.GetString().size() > remaining_text - text_bytes)
            {
                message = "Storm decoded context paths exceed the UTF-8 output limit.";
                return OPENUSD_STATUS_BUFFER_TOO_SMALL;
            }
            text_bytes += entry.first.GetString().size();
        }
        CopyPath(prim_path, owner, identity.prim_path_offset, identity.prim_path_length);
        CopyPath(
            instancer_path, owner,
            identity.instancer_path_offset, identity.instancer_path_length);
        identity.instance_index =
            instancer_path.IsEmpty() && context.empty() ? -1 : instance_index;
        identity.context_offset = owner.view.context_count;
        identity.context_count = static_cast<uint32_t>(context.size());
        for (const auto& entry : context)
        {
            auto& destination = owner.contexts[owner.view.context_count++];
            destination.struct_size = sizeof(destination);
            destination.version = OPENUSD_STORM_AOV_VERSION;
            destination.instance_index = entry.second;
            CopyPath(entry.first, owner, destination.path_offset, destination.path_length);
        }
        identity.status = OPENUSD_STORM_AOV_IDENTITY_RESOLVED;
    }
    owner.view.identity_status = OPENUSD_STORM_AOV_STATUS_READY;
    return OPENUSD_STATUS_OK;
}
}

openusd_status openusd_storm_aov_detail::ValidateRequest(
    const openusd_storm_aov_request& request,
    const OpenUsdStormAovState& state,
    std::string& message)
{
    if (request.struct_size != sizeof(request) ||
        request.version != OPENUSD_STORM_AOV_VERSION ||
        request.output_count == 0 ||
        request.output_count > OPENUSD_STORM_AOV_MAX_OUTPUTS ||
        (request.flags & ~OPENUSD_STORM_AOV_REQUEST_IDENTITIES) != 0 ||
        request.width <= 0 || request.height <= 0 ||
        request.width > static_cast<int32_t>(OPENUSD_STORM_AOV_MAX_DIMENSION) ||
        request.height > static_cast<int32_t>(OPENUSD_STORM_AOV_MAX_DIMENSION) ||
        request.max_pixels == 0 ||
        request.max_pixels > OPENUSD_STORM_AOV_MAX_PIXELS ||
        request.max_working_bytes == 0 ||
        request.max_working_bytes > OPENUSD_STORM_AOV_MAX_WORKING_BYTES ||
        request.max_unique_identities > OPENUSD_STORM_AOV_MAX_IDENTITIES ||
        request.max_instance_contexts > OPENUSD_STORM_AOV_MAX_CONTEXTS ||
        request.max_text_bytes > OPENUSD_STORM_AOV_MAX_TEXT_BYTES ||
        !std::isfinite(request.time_code) ||
        (request.revision_flags &
            ~(OPENUSD_STORM_RENDER_HAS_SCENE_REVISION |
              OPENUSD_STORM_RENDER_USE_SCENE_LIGHTS)) != 0)
    {
        message = "The Storm AOV request, version, or limits are invalid.";
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if ((request.flags & OPENUSD_STORM_AOV_REQUEST_IDENTITIES) == 0 &&
        (request.max_unique_identities != 0 ||
         request.max_instance_contexts != 0 || request.max_text_bytes != 0))
    {
        message = "Identity limits require an explicit Storm identity-table request.";
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (state.owner_live->load(std::memory_order_acquire))
    {
        message = "Release the previous Storm AOV owner before capturing again.";
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (state.capture_id == std::numeric_limits<uint64_t>::max())
    {
        message = "The Storm AOV capture sequence is exhausted.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    const uint64_t pixels =
        static_cast<uint64_t>(request.width) * static_cast<uint64_t>(request.height);
    if (pixels > request.max_pixels)
    {
        message = "Storm AOV pixels exceed the request limit before rendering.";
        return OPENUSD_STATUS_BUFFER_TOO_SMALL;
    }
    uint32_t seen = 0;
    uint64_t bytes = 0;
    uint64_t scratch = 0;
    for (uint32_t index = 0; index < OPENUSD_STORM_AOV_MAX_OUTPUTS; ++index)
    {
        const uint32_t kind = request.output_kinds[index];
        if (index >= request.output_count)
        {
            if (kind != 0)
            {
                message = "Unused Storm AOV request slots must be zero.";
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            continue;
        }
        if (kind < OPENUSD_STORM_AOV_COLOR || kind > OPENUSD_STORM_AOV_NORMAL ||
            (seen & (1u << kind)) != 0)
        {
            message = "Storm AOV output kinds must be known and unique.";
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        seen |= 1u << kind;
        const uint64_t output_bytes = pixels * OutputFormat(kind).bytes;
        bytes = Align8(bytes) + output_bytes;
        if (output_bytes != 0)
        {
            scratch += output_bytes + ReadbackAllowance;
        }
    }
    constexpr uint32_t identity_outputs =
        (1u << OPENUSD_STORM_AOV_PRIM_ID) | (1u << OPENUSD_STORM_AOV_INSTANCE_ID);
    if ((request.flags & OPENUSD_STORM_AOV_REQUEST_IDENTITIES) != 0 &&
        (seen & identity_outputs) != identity_outputs)
    {
        message = "Identity tables require explicitly requested primId and instanceId outputs.";
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (WorkingBytes(state, request, bytes, scratch) > request.max_working_bytes)
    {
        message = "Storm AOV copy and retained/replacement scratch exceed the byte limit.";
        return OPENUSD_STATUS_BUFFER_TOO_SMALL;
    }
    return OPENUSD_STATUS_OK;
}

TfTokenVector openusd_storm_aov_detail::OutputNames(
    const openusd_storm_aov_request& request)
{
    TfTokenVector names;
    names.reserve(OPENUSD_STORM_AOV_MAX_OUTPUTS);
    names.push_back(HdAovTokens->color);
    for (uint32_t index = 0; index < request.output_count; ++index)
    {
        const uint32_t kind = request.output_kinds[index];
        if (kind != OPENUSD_STORM_AOV_COLOR && kind != OPENUSD_STORM_AOV_NORMAL)
        {
            names.push_back(OutputName(kind));
        }
    }
    return names;
}

openusd_status openusd_storm_aov_detail::CopyCompleted(
    UsdImagingGLEngine& engine,
    OpenUsdStormAovState& state,
    const openusd_storm_aov_request& request,
    const openusd_render_camera& applied_camera,
    openusd_storm_aov_owner** owner,
    std::string& message)
{
    if (!engine.IsConverged() || engine.GetRendererHgiDisplayName() != "OpenGL")
    {
        message = "AOV copying requires a completed OpenGL Storm render.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    TfErrorMark mark;
    std::array<openusd_storm_aov_output, OPENUSD_STORM_AOV_MAX_OUTPUTS> descriptors{};
    std::array<HdRenderBuffer*, OPENUSD_STORM_AOV_MAX_OUTPUTS> buffers{};
    uint64_t pixel_bytes = 0;
    uint64_t scratch_bytes = 0;
    for (uint32_t index = 0; index < request.output_count; ++index)
    {
        auto& descriptor = descriptors[index];
        const uint32_t kind = request.output_kinds[index];
        const Format format = OutputFormat(kind);
        descriptor.struct_size = sizeof(descriptor);
        descriptor.version = OPENUSD_STORM_AOV_VERSION;
        descriptor.kind = kind;
        if (format.bytes == 0)
        {
            descriptor.status = OPENUSD_STORM_AOV_STATUS_UNSUPPORTED;
            continue;
        }
        HdRenderBuffer* buffer = engine.GetAovRenderBuffer(OutputName(kind));
        if (buffer == nullptr)
        {
            descriptor.status = OPENUSD_STORM_AOV_STATUS_ABSENT;
            continue;
        }
        if (buffer->GetWidth() != static_cast<uint32_t>(request.width) ||
            buffer->GetHeight() != static_cast<uint32_t>(request.height) ||
            buffer->GetDepth() != 1 ||
            buffer->GetFormat() != format.native_format || buffer->IsMapped())
        {
            message = "Storm AOV descriptor mismatch refused before map or allocation.";
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        const VtValue resource = buffer->GetResource(false);
        if (!resource.IsHolding<HgiTextureHandle>() ||
            !resource.UncheckedGet<HgiTextureHandle>())
        {
            message = "Storm AOV has no resolved texture resource.";
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        const HgiTextureDesc& texture =
            resource.UncheckedGet<HgiTextureHandle>()->GetDescriptor();
        if (texture.dimensions != GfVec3i(request.width, request.height, 1) ||
            texture.format != HdStHgiConversions::GetHgiFormat(format.native_format) ||
            texture.type != HgiTextureType2D || texture.layerCount != 1 ||
            texture.mipLevels != 1 || texture.sampleCount != HgiSampleCount1)
        {
            message = "Storm AOV backing texture exceeds the admitted descriptor.";
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        buffers[index] = buffer;
        descriptor.status = OPENUSD_STORM_AOV_STATUS_READY;
        descriptor.format = format.format;
        descriptor.width = static_cast<uint32_t>(request.width);
        descriptor.height = static_cast<uint32_t>(request.height);
        descriptor.row_stride_bytes = descriptor.width * format.bytes;
        descriptor.origin = OPENUSD_STORM_AOV_ORIGIN_TOP_LEFT;
        descriptor.flags = buffer->IsMultiSampled()
            ? OPENUSD_STORM_AOV_OUTPUT_RESOLVED_MULTISAMPLE : 0;
        descriptor.depth_convention = kind == OPENUSD_STORM_AOV_DEPTH
            ? OPENUSD_STORM_AOV_DEPTH_OPENGL_WINDOW : OPENUSD_STORM_AOV_DEPTH_NONE;
        descriptor.data_offset = Align8(pixel_bytes);
        descriptor.data_bytes =
            static_cast<uint64_t>(descriptor.row_stride_bytes) * descriptor.height;
        pixel_bytes = descriptor.data_offset + descriptor.data_bytes;
        scratch_bytes += descriptor.data_bytes + ReadbackAllowance;
    }
    const uint64_t working_bytes = WorkingBytes(state, request, pixel_bytes, scratch_bytes);
    if (working_bytes > request.max_working_bytes)
    {
        message = "Actual Storm AOV descriptors exceed the working byte limit.";
        return OPENUSD_STATUS_BUFFER_TOO_SMALL;
    }
    if (!mark.IsClean())
    {
        mark.Clear();
        message = "Storm reported an AOV descriptor error before allocation.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }

    auto result = std::make_unique<openusd_storm_aov_owner>();
    result->outputs = descriptors;
    result->pixels.resize(static_cast<size_t>(Align8(pixel_bytes) / sizeof(uint64_t)));
    glFinish();
    for (uint32_t index = 0; index < request.output_count; ++index)
    {
        HdRenderBuffer* buffer = buffers[index];
        if (buffer == nullptr)
        {
            continue;
        }
        const auto& descriptor = descriptors[index];
        state.scratch_high_water[descriptor.kind] = std::max(
            state.scratch_high_water[descriptor.kind],
            descriptor.data_bytes + ReadbackAllowance);
        {
            ScopedMap map(*buffer);
            const void* data = map.Map();
            if (data == nullptr)
            {
                message = "Storm AOV mapping failed; no owner was published.";
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            auto* destination = reinterpret_cast<uint8_t*>(result->pixels.data()) +
                descriptor.data_offset;
            const auto* source = static_cast<const uint8_t*>(data);
            for (uint32_t row = 0; row < descriptor.height; ++row)
            {
                std::memcpy(
                    destination + static_cast<size_t>(row) * descriptor.row_stride_bytes,
                    source + static_cast<size_t>(descriptor.height - row - 1) *
                        descriptor.row_stride_bytes,
                    descriptor.row_stride_bytes);
            }
        }
        if (buffer->IsMapped() || !mark.IsClean())
        {
            mark.Clear();
            message = "Storm AOV map/unmap did not complete cleanly.";
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
    }
    const openusd_status identities = BuildIdentities(engine, request, *result, message);
    if (identities != OPENUSD_STATUS_OK)
    {
        mark.Clear();
        return identities;
    }
    if (!mark.IsClean())
    {
        mark.Clear();
        message = "Storm reported an integer identity decode error.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    auto& view = result->view;
    view.struct_size = sizeof(view);
    view.version = OPENUSD_STORM_AOV_VERSION;
    view.output_count = request.output_count;
    view.width = static_cast<uint32_t>(request.width);
    view.height = static_cast<uint32_t>(request.height);
    view.revision_flags = request.revision_flags;
    view.capture_id = ++state.capture_id;
    view.state_revision = request.state_revision;
    view.scene_revision = request.scene_revision;
    view.time_code = request.time_code;
    view.owned_bytes =
        sizeof(openusd_storm_aov_owner) +
        result->pixels.capacity() * sizeof(uint64_t) +
        result->identities.capacity() * sizeof(openusd_storm_aov_identity) +
        result->contexts.capacity() * sizeof(openusd_storm_aov_instance_context) +
        result->text.capacity() +
        result->identity_indices.capacity() * sizeof(uint32_t);
    view.admitted_working_bytes = working_bytes;
    view.retained_scratch_upper_bound_bytes = RetainedScratch(state);
    view.outputs = result->outputs.data();
    view.pixel_data = result->pixels.empty() ? nullptr : result->pixels.data();
    view.pixel_bytes = result->pixels.size() * sizeof(uint64_t);
    view.identities = view.identity_count == 0 ? nullptr : result->identities.data();
    view.contexts = view.context_count == 0 ? nullptr : result->contexts.data();
    view.text = view.text_byte_count == 0 ? nullptr : result->text.data();
    view.identity_indices = result->identity_indices.empty()
        ? nullptr : result->identity_indices.data();
    view.identity_index_count = result->identity_indices.size();
    view.applied_camera = applied_camera;
    result->owner_live = state.owner_live;
    result->owner_live->store(true, std::memory_order_release);
    *owner = result.release();
    return OPENUSD_STATUS_OK;
}

void openusd_storm_aov_detail::GetView(
    const openusd_storm_aov_owner& owner, openusd_storm_aov_view& view) noexcept
{
    view = owner.view;
}

void openusd_storm_aov_release(openusd_storm_aov_owner* owner) noexcept
{
    if (owner == nullptr)
    {
        return;
    }
    const auto live = std::move(owner->owner_live);
    delete owner;
    if (live)
    {
        live->store(false, std::memory_order_release);
    }
}
