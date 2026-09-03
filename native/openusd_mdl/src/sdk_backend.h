// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_MDL_SDK_BACKEND_H
#define OPENUSD_MDL_SDK_BACKEND_H

#include <cstdint>
#include <map>
#include <string>
#include <vector>

namespace openusd_mdl
{
/// One parameter value the MDL SDK resolved out of a compiled module. It is a
/// plain value, deliberately: no MDL SDK type appears in this header, so the
/// SDK backend can be compiled out entirely and nothing else in the adapter has
/// to change shape.
struct SdkParameterValue
{
    /// OPENUSD_MDL_VALUE_*.
    uint32_t kind = 0;
    uint32_t componentCount = 0;
    float value[4] = {0.0F, 0.0F, 0.0F, 0.0F};
    int32_t integerValue = 0;
    /// The resolved resource path for a texture, or the string value.
    std::string text;
    /// OPENUSD_MDL_ORIGIN_*.
    uint32_t origin = 1u;
};

/// What one material resolution produced.
struct SdkMaterialResolution
{
    /// OPENUSD_MDL_STATUS_*.
    uint32_t status = 0;
    std::string diagnostic;
    /// Parameter name to resolved value, for every parameter whose default this
    /// backend could reduce to a value.
    std::map<std::string, SdkParameterValue> defaults;
    /// Parameter names the material declares whose defaults are expressions this
    /// backend does not evaluate -- a call it does not fold, a layered BSDF, or
    /// a resource it could not resolve. Reported by name so a caller can say
    /// which input it dropped.
    std::vector<std::string> unresolved;
};

/// The MDL SDK is loaded at run time from a user-supplied location and is never
/// linked, so this whole class is a no-op unless the adapter was built with an
/// SDK and the operator supplied one.
///
/// Thread safety: one process-wide instance guarded by its own mutex. Every
/// public method takes that lock, because neuray's transactions are not safe to
/// interleave and the adapter is called from hdSilk's parallel material sync.
class SdkBackend
{
public:
    static SdkBackend& Instance();

    /// True when this build has an SDK backend compiled in at all.
    static bool IsCompiledIn();

    /// Points the backend at the directories it may resolve modules from, and
    /// invalidates every cached module when `generation` differs from the last
    /// accepted one. Every path must already be absolute; the caller validates
    /// that before this is reached.
    ///
    /// Returns an OPENUSD_MDL_STATUS_* code. SDK_UNAVAILABLE is not an error
    /// here: it means the adapter will simply keep to its authored-value path.
    uint32_t Configure(
        const std::vector<std::string>& searchPaths,
        uint64_t generation,
        std::string* diagnostic);

    /// Compiles the module and reduces the named material's parameter defaults.
    SdkMaterialResolution ResolveMaterial(
        const std::string& moduleUri,
        const std::string& materialName);

    /// A short provenance string naming the loaded SDK, or why there is none.
    std::string Describe();

    /// True when a runtime has been loaded and started.
    bool IsAvailable();

private:
    SdkBackend() = default;
    SdkBackend(const SdkBackend&) = delete;
    SdkBackend& operator=(const SdkBackend&) = delete;
};
}

#endif
