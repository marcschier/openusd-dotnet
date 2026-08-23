// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"
#include "pxr/usd/sdf/copyUtils.h"

/*
 * Session overlay normalization: creates two anonymous sublayers of the session layer
 * (physics = strongest, user-edit = weaker) and transfers any pre-existing direct
 * session opinions into the user-edit layer. On removal, only the physics layer is
 * detached; user content is preserved.
 *
 * Strength order within the session layer after normalization:
 *   [0] physics overlay (anonymous) — simulation results compose here
 *   [1] user-edit layer (anonymous) — user/viewer session edits compose here
 *   (any pre-existing sublayers follow at index 2+)
 *
 * Transactional guarantee: all mutations use a backup anonymous layer that holds a
 * complete snapshot of the original session content (including sublayer paths and all
 * pseudo-root metadata). On any failure the session layer is restored from the backup
 * and the original edit target is re-set; rollback failures are reported.
 */

namespace
{

// Snapshot the full session layer into a backup anonymous layer and capture the
// current edit-target layer identifier for rollback.
struct SessionBackup
{
    SdfLayerRefPtr backup;
    SdfLayerHandle editTargetLayer;
    bool valid = false;

    static SessionBackup Capture(
        const UsdStageRefPtr& stage,
        const SdfLayerHandle& session)
    {
        SessionBackup sb;
        sb.backup = SdfLayer::CreateAnonymous("session-backup");
        if (!sb.backup)
        {
            return sb;
        }
        sb.backup->TransferContent(session);
        sb.editTargetLayer = stage->GetEditTarget().GetLayer();
        sb.valid = true;
        return sb;
    }

    // Returns false and writes to error if rollback itself fails.
    bool Restore(
        const UsdStageRefPtr& stage,
        const SdfLayerHandle& session,
        openusd_error_buffer* error) const
    {
        if (!valid || !backup || !session)
        {
            WriteError(error, "Session backup is invalid; rollback not possible.");
            return false;
        }
        session->TransferContent(backup);

        if (editTargetLayer)
        {
            TfErrorMark restoreMark;
            try
            {
                stage->SetEditTarget(UsdEditTarget(editTargetLayer));
            }
            catch (...)
            {
                WriteError(error,
                    "Session content was restored but edit target could not be re-set.");
                return false;
            }
            if (!restoreMark.IsClean())
            {
                ConsumeErrors(restoreMark);
                WriteError(error,
                    "Session content was restored but edit target restoration had errors.");
                return false;
            }
        }
        return true;
    }
};

// Returns true if the session layer has any direct content beyond sublayer topology.
// Checks: root prim specs, and all pseudo-root fields except subLayers/subLayerOffsets.
bool HasDirectContent(const SdfLayerHandle& session)
{
    if (!session->GetRootPrims().empty())
    {
        return true;
    }
    // Check pseudo-root for any fields other than sublayer management.
    SdfPrimSpecHandle pseudoRoot = session->GetPseudoRoot();
    if (pseudoRoot)
    {
        for (const TfToken& field : pseudoRoot->ListInfoKeys())
        {
            if (field != SdfFieldKeys->SubLayers &&
                field != SdfFieldKeys->SubLayerOffsets)
            {
                return true;
            }
        }
    }
    return false;
}

// Strips only sublayer paths from a layer (used after TransferContent to separate
// topology management from direct opinions).
void StripSublayerPaths(const SdfLayerHandle& layer)
{
    const SdfSubLayerProxy subs = layer->GetSubLayerPaths();
    for (int i = static_cast<int>(subs.size()) - 1; i >= 0; --i)
    {
        layer->RemoveSubLayerPath(i);
    }
}

// Copies all root prim specs and pseudo-root metadata (excluding sublayer fields)
// from source to destination with overwrite semantics using SdfCopySpec.
// Returns true on success.
bool CopyDirectContent(
    const SdfLayerHandle& source,
    const SdfLayerHandle& dest)
{
    // Copy each root prim spec using SdfCopySpec (overwrites existing specs).
    for (const SdfPrimSpecHandle& prim : source->GetRootPrims())
    {
        const SdfPath path = prim->GetPath();
        if (!SdfCopySpec(source, path, dest, path))
        {
            return false;
        }
    }

    // Copy pseudo-root metadata fields (excluding sublayer topology).
    SdfPrimSpecHandle srcPseudoRoot = source->GetPseudoRoot();
    SdfPrimSpecHandle dstPseudoRoot = dest->GetPseudoRoot();
    if (srcPseudoRoot && dstPseudoRoot)
    {
        for (const TfToken& field : srcPseudoRoot->ListInfoKeys())
        {
            if (field != SdfFieldKeys->SubLayers &&
                field != SdfFieldKeys->SubLayerOffsets)
            {
                VtValue value = srcPseudoRoot->GetInfo(field);
                dstPseudoRoot->SetInfo(field, value);
            }
        }
    }
    return true;
}

// Captures sublayer paths with their offsets for order-preserving restoration.
struct SublayerSnapshot
{
    std::vector<std::string> paths;
    std::vector<SdfLayerOffset> offsets;

    static SublayerSnapshot Capture(const SdfLayerHandle& layer)
    {
        SublayerSnapshot snap;
        const SdfSubLayerProxy subs = layer->GetSubLayerPaths();
        snap.paths.assign(subs.begin(), subs.end());
        snap.offsets.reserve(snap.paths.size());
        for (size_t i = 0; i < snap.paths.size(); ++i)
        {
            snap.offsets.push_back(layer->GetSubLayerOffset(static_cast<int>(i)));
        }
        return snap;
    }

    // Reinserts all captured sublayers starting at `baseIndex`, restoring offsets.
    void RestoreAt(const SdfLayerHandle& layer, int baseIndex) const
    {
        for (size_t i = 0; i < paths.size(); ++i)
        {
            int idx = baseIndex + static_cast<int>(i);
            layer->InsertSubLayerPath(paths[i], idx);
            layer->SetSubLayerOffset(offsets[i], idx);
        }
    }
};

// Clears all direct content from a layer while preserving its sublayer
// topology including offsets.
void ClearDirectContent(const SdfLayerHandle& layer)
{
    SublayerSnapshot snap = SublayerSnapshot::Capture(layer);
    std::vector<SdfLayerRefPtr> retainedSublayers;
    retainedSublayers.reserve(snap.paths.size());
    for (const std::string& path : snap.paths)
    {
        const SdfLayerHandle sublayer = SdfLayer::Find(path);
        if (sublayer)
        {
            retainedSublayers.push_back(
                TfCreateRefPtrFromProtectedWeakPtr(sublayer));
        }
    }
    layer->Clear();
    snap.RestoreAt(layer, 0);
}

} // namespace

openusd_status openusd_stage_session_overlay_normalize(
    const openusd_stage* stage,
    openusd_layer** physics_layer_out,
    openusd_layer** user_layer_out,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(physics_layer_out);
        ResetAbiOutput(user_layer_out);
        if (stage == nullptr || !stage->value ||
            physics_layer_out == nullptr || user_layer_out == nullptr)
        {
            WriteError(error, "A valid stage and both layer outputs are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        *physics_layer_out = nullptr;
        *user_layer_out = nullptr;

        return Guard(error, [&]()
        {
            TfErrorMark mark;

            SdfLayerHandle session = stage->value->GetSessionLayer();
            if (!session)
            {
                WriteError(error, "The stage has no session layer.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            // 1. Full transactional backup before any mutation.
            SessionBackup backup = SessionBackup::Capture(stage->value, session);
            if (!backup.valid)
            {
                WriteError(error, "Could not create session backup for rollback.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }

            // 2. Create anonymous layers with descriptive tags for diagnostics.
            SdfLayerRefPtr physicsAnon = SdfLayer::CreateAnonymous("physics-overlay");
            SdfLayerRefPtr userAnon = SdfLayer::CreateAnonymous("user-edit");
            if (!physicsAnon || !userAnon)
            {
                WriteError(error, "Could not create anonymous overlay layers.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }

            // 3. Snapshot existing sublayer paths with offsets (order-preserving).
            SublayerSnapshot originalSublayers = SublayerSnapshot::Capture(session);

            // 4. Always transfer full session content into user-edit layer.
            //    TransferContent replaces userAnon with session's complete snapshot
            //    (prims, customLayerData, defaultPrim, documentation, timeCodes, etc.).
            //    Then strip sublayer paths from userAnon since topology is managed separately.
            userAnon->TransferContent(session);
            StripSublayerPaths(userAnon);

            // 5. Clear session to make it a pure container, then rebuild its sublayer
            //    topology: physics (strongest), user-edit, then original sublayers
            //    with their offsets restored at shifted indices (originals start at 2).
            session->Clear();

            const std::string physicsId = physicsAnon->GetIdentifier();
            const std::string userId = userAnon->GetIdentifier();

            session->InsertSubLayerPath(physicsId, 0);
            session->InsertSubLayerPath(userId, 1);
            originalSublayers.RestoreAt(session, 2);

            // 6. Check for Tf errors from any mutation above.
            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                backup.Restore(stage->value, session, error);
                WriteError(error, message.empty()
                    ? "Failed to normalize session overlay."
                    : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }

            // 7. Retain stage references for both output handles.
            if (!RetainStageReference(const_cast<openusd_stage*>(stage)))
            {
                backup.Restore(stage->value, session, error);
                WriteError(error, "Could not retain stage for physics layer.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            if (!RetainStageReference(const_cast<openusd_stage*>(stage)))
            {
                ReleaseStageReference(const_cast<openusd_stage*>(stage));
                backup.Restore(stage->value, session, error);
                WriteError(error, "Could not retain stage for user layer.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }

            // 8. Allocate output handles. On allocation failure, rollback.
            std::unique_ptr<openusd_layer> physicsHandle;
            std::unique_ptr<openusd_layer> userHandle;
            try
            {
                physicsHandle = std::make_unique<openusd_layer>();
                userHandle = std::make_unique<openusd_layer>();
            }
            catch (...)
            {
                ReleaseStageReference(const_cast<openusd_stage*>(stage));
                ReleaseStageReference(const_cast<openusd_stage*>(stage));
                backup.Restore(stage->value, session, error);
                throw;
            }

            physicsHandle->value = physicsAnon;
            physicsHandle->stage = const_cast<openusd_stage*>(stage);
            userHandle->value = userAnon;
            userHandle->stage = const_cast<openusd_stage*>(stage);

            *physics_layer_out = physicsHandle.release();
            *user_layer_out = userHandle.release();
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_session_overlay_remove(
    const openusd_stage* stage,
    const char* physics_layer_identifier,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value ||
            physics_layer_identifier == nullptr || physics_layer_identifier[0] == '\0')
        {
            WriteError(error, "A valid stage and physics layer identifier are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;

            SdfLayerHandle session = stage->value->GetSessionLayer();
            if (!session)
            {
                WriteError(error, "The stage has no session layer.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            const SdfSubLayerProxy paths = session->GetSubLayerPaths();
            int index = -1;
            for (size_t i = 0; i < paths.size(); ++i)
            {
                if (paths[i] == physics_layer_identifier)
                {
                    index = static_cast<int>(i);
                    break;
                }
            }
            if (index < 0)
            {
                WriteError(error, "The physics overlay was not found in the session layer.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            session->RemoveSubLayerPath(index);

            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                WriteError(error, message.empty()
                    ? "Could not remove the physics overlay."
                    : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_session_overlay_detect_contamination(
    const openusd_stage* stage,
    const char* physics_layer_identifier,
    const char* user_layer_identifier,
    int32_t* has_contamination,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(has_contamination);
        if (stage == nullptr || !stage->value ||
            physics_layer_identifier == nullptr || physics_layer_identifier[0] == '\0' ||
            user_layer_identifier == nullptr || user_layer_identifier[0] == '\0' ||
            has_contamination == nullptr)
        {
            WriteError(error, "A valid stage, both layer identifiers, and output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        *has_contamination = 0;

        return Guard(error, [&]()
        {
            SdfLayerHandle session = stage->value->GetSessionLayer();
            if (!session)
            {
                WriteError(error, "The stage has no session layer.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            // Contamination = session container has any direct content (root prim specs,
            // pseudo-root metadata fields beyond sublayer topology management).
            if (HasDirectContent(session))
            {
                *has_contamination = 1;
            }

            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_stage_session_overlay_migrate_contamination(
    const openusd_stage* stage,
    const char* user_layer_identifier,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value ||
            user_layer_identifier == nullptr || user_layer_identifier[0] == '\0')
        {
            WriteError(error, "A valid stage and user layer identifier are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;

            SdfLayerHandle session = stage->value->GetSessionLayer();
            if (!session)
            {
                WriteError(error, "The stage has no session layer.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            SdfLayerHandle userLayer = SdfLayer::Find(user_layer_identifier);
            if (!userLayer)
            {
                WriteError(error, "The user-edit layer was not found.");
                return OPENUSD_STATUS_NOT_FOUND;
            }

            // Nothing to migrate if session container is clean.
            if (!HasDirectContent(session))
            {
                return OPENUSD_STATUS_OK;
            }

            // Transactional backup of the session and user layers before migration.
            SessionBackup sessionBackup = SessionBackup::Capture(stage->value, session);
            if (!sessionBackup.valid)
            {
                WriteError(error, "Could not create session backup for migration rollback.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }

            SdfLayerRefPtr userBackup = SdfLayer::CreateAnonymous("user-backup");
            if (!userBackup)
            {
                WriteError(error, "Could not create user-layer backup for migration rollback.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            userBackup->TransferContent(userLayer);

            // Copy all contaminating content from session into userLayer using
            // SdfCopySpec (overwrite semantics: late session opinions override
            // existing user opinions, preserving their original strength).
            if (!CopyDirectContent(session, userLayer))
            {
                // Rollback user layer.
                userLayer->TransferContent(userBackup);
                WriteError(error, "SdfCopySpec failed during contamination migration.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }

            // Clear session's direct content but preserve sublayer topology.
            ClearDirectContent(session);

            if (!mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);

                // Rollback both layers.
                userLayer->TransferContent(userBackup);
                sessionBackup.Restore(stage->value, session, error);

                WriteError(error, message.empty()
                    ? "Could not migrate session contamination."
                    : message);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });

    });
}
