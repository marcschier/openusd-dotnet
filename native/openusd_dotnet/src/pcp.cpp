// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

#include "pxr/usd/pcp/layerStack.h"

#include <unordered_map>

struct openusd_pcp_prim_index_list
{
    std::vector<openusd_pcp_node_record> nodes;
    std::vector<char> data;
    std::vector<size_t> offsets;
};

namespace
{
void ResetPcpOutput(openusd_pcp_prim_index_list** list, openusd_pcp_prim_index_view* view) noexcept
{
    if (list != nullptr)
    {
        *list = nullptr;
    }
    if (view != nullptr)
    {
        const uint32_t structSize = view->struct_size;
        std::memset(view, 0, sizeof(*view));
        view->struct_size = structSize;
        view->version = OPENUSD_PCP_PRIM_INDEX_VIEW_VERSION;
    }
}

void AppendString(openusd_pcp_prim_index_list& list, const std::string& value)
{
    if (value.find('\0') != std::string::npos)
    {
        throw std::invalid_argument("Pcp strings must not contain embedded NULs.");
    }
    list.offsets.push_back(list.data.size());
    list.data.insert(list.data.end(), value.begin(), value.end());
    list.data.push_back('\0');
}

std::string PathString(const SdfPath& path)
{
    return path.IsEmpty() ? std::string() : path.GetString();
}

openusd_pcp_arc_type ConvertArcType(PcpArcType arcType)
{
    switch (arcType)
    {
    case PcpArcTypeRoot: return OPENUSD_PCP_ARC_ROOT;
    case PcpArcTypeInherit: return OPENUSD_PCP_ARC_INHERIT;
    case PcpArcTypeVariant: return OPENUSD_PCP_ARC_VARIANT;
    case PcpArcTypeRelocate: return OPENUSD_PCP_ARC_RELOCATE;
    case PcpArcTypeReference: return OPENUSD_PCP_ARC_REFERENCE;
    case PcpArcTypePayload: return OPENUSD_PCP_ARC_PAYLOAD;
    case PcpArcTypeSpecialize: return OPENUSD_PCP_ARC_SPECIALIZE;
    default: return OPENUSD_PCP_ARC_ROOT;
    }
}

void FillView(openusd_pcp_prim_index_list& list, openusd_pcp_prim_index_view* view)
{
    view->version = OPENUSD_PCP_PRIM_INDEX_VIEW_VERSION;
    view->nodes = list.nodes.empty() ? nullptr : list.nodes.data();
    view->nodes_size = list.nodes.size() * sizeof(openusd_pcp_node_record);
    view->node_count = list.nodes.size();
    view->data = list.data.empty() ? nullptr : list.data.data();
    view->data_size = list.data.size();
    view->offsets = list.offsets.empty() ? nullptr : list.offsets.data();
    view->offsets_size = list.offsets.size() * sizeof(size_t);
    view->string_count = list.offsets.size();
}
}

void openusd_pcp_prim_index_list_release(openusd_pcp_prim_index_list* list)
{
    delete list;
}

openusd_status openusd_pcp_get_prim_index(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_pcp_prim_index_list** list,
    openusd_pcp_prim_index_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        uint32_t structSize = view == nullptr ? 0 : view->struct_size;
        const uint32_t requestedVersion = view == nullptr ? 0 : view->version;
        ResetPcpOutput(list, view);
        // ABI_OUTPUT_INITIALIZATION
        if (stage == nullptr || !stage->value || !IsValidPrimPath(prim_path) ||
            list == nullptr || view == nullptr || !IsAligned(view) ||
            structSize < sizeof(openusd_pcp_prim_index_view) ||
            requestedVersion != OPENUSD_PCP_PRIM_INDEX_VIEW_VERSION)
        {
            WriteError(error, "A valid stage, prim path, output list, and Pcp view version 1 are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        std::unique_ptr<openusd_pcp_prim_index_list> result;
        openusd_status status = Guard(error, [&]() -> openusd_status
        {
            const UsdPrim prim = stage->value->GetPrimAtPath(SdfPath(prim_path));
            if (!prim)
            {
                WriteError(error, std::string("Prim was not found: ") + prim_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }

            result = std::make_unique<openusd_pcp_prim_index_list>();
            const PcpPrimIndex primIndex = prim.ComputeExpandedPrimIndex();
            std::unordered_map<void*, int32_t> nodeIndices;
            for (const PcpNodeRef& node : primIndex.GetNodeRange())
            {
                const int32_t index = static_cast<int32_t>(result->nodes.size());
                nodeIndices[node.GetUniqueIdentifier()] = index;

                openusd_pcp_node_record record{};
                const PcpNodeRef parent = node.GetParentNode();
                const auto parentIt = parent ? nodeIndices.find(parent.GetUniqueIdentifier()) : nodeIndices.end();
                record.parent_index = parentIt == nodeIndices.end() ? -1 : parentIt->second;
                record.arc_type = static_cast<int32_t>(ConvertArcType(node.GetArcType()));
                record.is_culled = node.IsCulled() ? 1 : 0;
                record.is_inert = node.IsInert() ? 1 : 0;
                record.is_due_to_ancestor = node.IsDueToAncestor() ? 1 : 0;
                record.has_specs = node.HasSpecs() ? 1 : 0;
                record.can_contribute_specs = node.CanContributeSpecs() ? 1 : 0;
                record.namespace_depth = node.GetNamespaceDepth();
                record.depth_below_introduction = node.GetDepthBelowIntroduction();
                record.sibling_index_at_origin = node.GetSiblingNumAtOrigin();
                record.string_offset = result->offsets.size();
                record.string_count = 5;
                AppendString(*result, PathString(node.GetPath()));
                AppendString(*result, PathString(node.GetIntroPath()));
                AppendString(*result, PathString(node.GetPathAtIntroduction()));
                AppendString(*result, PathString(node.GetPathAtOriginRootIntroduction()));
                const PcpLayerStackRefPtr& layerStack = node.GetLayerStack();
                AppendString(
                    *result,
                    layerStack && layerStack->GetIdentifier().rootLayer
                        ? layerStack->GetIdentifier().rootLayer->GetIdentifier()
                        : std::string());
                record.layer_offset = result->offsets.size();
                if (layerStack)
                {
                    for (const SdfLayerRefPtr& layer : layerStack->GetLayers())
                    {
                        AppendString(*result, layer ? layer->GetIdentifier() : std::string());
                    }
                }
                record.layer_count = result->offsets.size() - record.layer_offset;
                result->nodes.push_back(record);
            }

            view->error_offset = result->offsets.size();
            for (const PcpErrorBasePtr& pcpError : primIndex.GetLocalErrors())
            {
                AppendString(*result, pcpError ? pcpError->ToString() : std::string());
            }
            view->error_count = result->offsets.size() - view->error_offset;
            FillView(*result, view);
            return OPENUSD_STATUS_OK;
        });
        if (status == OPENUSD_STATUS_OK && result)
        {
            *list = result.release();
            return OPENUSD_STATUS_OK;
        }
        ResetPcpOutput(list, view);
        return status;
    });
}
