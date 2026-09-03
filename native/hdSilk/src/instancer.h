// Copyright (c) marcschier. Licensed under the MIT License.
//
// Architecture note: this instancer follows the structure of Pixar's
// Apache-2.0 hdEmbree instancer (pxr/imaging/plugin/hdEmbree/instancer.*).
// hdSilk does not draw instances itself; it flattens each resolved instance
// transform into its own MESH_UPSERT wire record so backend-neutral
// consumers receive plain triangle lists without an instancing ABI of their
// own. Unlike hdEmbree, hdSilk also has to publish an identity per instance,
// so it carries the authoritative instance index next to each transform.

#ifndef HDSILK_INSTANCER_H
#define HDSILK_INSTANCER_H

#include "pxr/pxr.h"
#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/tf/token.h"
#include "pxr/base/vt/value.h"
#include "pxr/imaging/hd/instancer.h"

#include "sceneState.h"

#include <cstdint>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

PXR_NAMESPACE_OPEN_SCOPE

/// One resolved instance of a prototype.
struct HdSilkInstanceSample
{
    /// World transform of the instance, with this instancer's transform and
    /// every parent instancer level already composed.
    GfMatrix4d transform{1.0};

    /// Authoritative index of the instance inside its owning instancer.
    ///
    /// For a single instancer this is the element Hydra reports through
    /// GetInstanceIndices, which is the index into the point instancer's own
    /// protoIndices/positions arrays and the value UsdImaging decodes back to
    /// a scene instance for selection and picking. It is therefore stable when
    /// a prototype covers only part of the instancer (multiple prototypes or
    /// varying proto indices) and when instances disappear through
    /// invisibleIds, neither of which the position in the resolved array is.
    ///
    /// For a nested instancer it is the mixed-radix composition
    /// parentIndex * instanceCount + index, where instanceCount is this
    /// instancer's own authoritative instance count and never a per-prototype
    /// value: a radix widened to fit one prototype's samples would give the
    /// instancer's other prototypes a different index space. An index the
    /// authoritative count cannot explain is dropped with a diagnostic rather
    /// than composed. The result keeps nested indices unique and equally
    /// stable, but it is an hdSilk encoding rather than an index USD can decode
    /// on its own -- which is exactly why `context` exists beside it.
    int64_t index = 0;

    /// The complete ordered instancing chain this sample belongs to, outermost
    /// level first and innermost last.
    ///
    /// Each entry names the instancer at that level and the instance's own
    /// index inside it, so a consumer can decode the chain back to a scene
    /// instance. `index` above cannot be decoded that way once more than one
    /// level is involved, because it is a composition rather than any single
    /// level's index; the chain is the authoritative description and the
    /// composite remains the stable key the retained tables are built on.
    std::vector<HdSilkInstancerContextEntry> context;
};

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

    /// Returns one sample per instance of prototypeId, already composed with
    /// this instancer's transform and every parent instancer level, ordered by
    /// ascending instance index. Returns an empty vector when the instancer is
    /// invisible.
    std::vector<HdSilkInstanceSample> ComputeInstanceSamples(
        SdfPath const& prototypeId);

    /// Returns this instancer's authoritative instance count: the length of its
    /// own instance primvars, which is the radix every nested index of it is
    /// composed against. It is the same value ComputeInstanceSamples uses, so a
    /// caller that has to reproduce a composed identity -- the link resolution
    /// does -- reads the radix from here rather than deriving one of its own
    /// from a per-prototype sample count.
    int64_t GetInstanceCount() const;

    /// Returns false when the instancer is invisible, in which case it publishes
    /// no instance of any prototype and every identity beneath it is absent.
    bool IsVisible() const;

private:
    void _SyncPrimvars(HdSceneDelegate* delegate, HdDirtyBits dirtyBits);

    /// Returns the number of instances this instancer publishes, which is the
    /// length of its instance primvars rather than the number resolved for any
    /// one prototype.
    static int64_t _InstanceCount(
        const std::unordered_map<TfToken, VtValue, TfToken::HashFunctor>&
            primvars);

    mutable std::mutex _mutex;
    std::unordered_map<TfToken, VtValue, TfToken::HashFunctor> _primvarMap;
    bool _visible = true;

    HdSilkInstancer(const HdSilkInstancer&) = delete;
    HdSilkInstancer& operator=(const HdSilkInstancer&) = delete;
};

PXR_NAMESPACE_CLOSE_SCOPE

#endif
