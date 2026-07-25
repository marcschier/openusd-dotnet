// Copyright (c) marcschier. Licensed under the MIT License.
//
// Architecture note: this instancer follows the structure of Pixar's
// Apache-2.0 hdEmbree instancer (pxr/imaging/plugin/hdEmbree/instancer.*).
// hdSilk does not draw instances itself; it flattens each resolved instance
// transform into its own MESH_UPSERT wire record so backend-neutral
// consumers receive plain triangle lists without an instancing ABI of their
// own.

#ifndef HDSILK_INSTANCER_H
#define HDSILK_INSTANCER_H

#include "pxr/pxr.h"
#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/tf/token.h"
#include "pxr/base/vt/value.h"
#include "pxr/imaging/hd/instancer.h"

#include <mutex>
#include <unordered_map>

PXR_NAMESPACE_OPEN_SCOPE

/// Accumulates the instance primvars Hydra publishes for a point instancer
/// and resolves them into one world transform per instance of a prototype.
class HdSilkInstancer final : public HdInstancer
{
public:
    HdSilkInstancer(HdSceneDelegate* delegate, SdfPath const& id);
    ~HdSilkInstancer() override;

    void Sync(
        HdSceneDelegate* delegate,
        HdRenderParam* renderParam,
        HdDirtyBits* dirtyBits) override;

    /// Returns one transform per instance of prototypeId, already composed
    /// with this instancer's transform and every parent instancer level.
    /// Returns an empty array when the instancer is invisible.
    VtMatrix4dArray ComputeInstanceTransforms(SdfPath const& prototypeId);

private:
    void _SyncPrimvars(HdSceneDelegate* delegate, HdDirtyBits dirtyBits);

    mutable std::mutex _mutex;
    std::unordered_map<TfToken, VtValue, TfToken::HashFunctor> _primvarMap;
    bool _visible = true;

    HdSilkInstancer(const HdSilkInstancer&) = delete;
    HdSilkInstancer& operator=(const HdSilkInstancer&) = delete;
};

PXR_NAMESPACE_CLOSE_SCOPE

#endif
