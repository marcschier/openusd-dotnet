// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef HDSILK_MDL_ADAPTER_H
#define HDSILK_MDL_ADAPTER_H

#include "openusd_mdl.h"

#include "pxr/pxr.h"

#include <string>
#include <vector>

PXR_NAMESPACE_OPEN_SCOPE

/// Why the optional MDL adapter is not usable. Every value other than Loaded
/// names a condition the caller must report against the material it was
/// resolving; none of them is a reason to shade with a default.
/// Why the optional MDL adapter is not usable. Every value other than Loaded
/// names a condition the caller must report against the material it was
/// resolving; none of them is a reason to shade with a default.
enum class HdSilkMdlAdapterState
{
    Loaded,
    /// No adapter library sits beside the module that hosts this loader, and no
    /// explicit path was configured. This is the state of every default
    /// install: the adapter ships in no base package.
    NotInstalled,
    /// The directory of the module hosting this loader could not be determined,
    /// so the only safe default location cannot be formed. Reported rather than
    /// falling back to a bare library name, because a bare name lets the
    /// platform loader search directories this process does not control.
    ModulePathUnavailable,
    /// OPENUSD_MDL_ADAPTER_PATH was set to something that is not an absolute
    /// path. A relative path is resolved against the process working directory,
    /// which is exactly the search this loader refuses to perform.
    PathNotAbsolute,
    /// A library was named by an absolute path and could not be loaded, either
    /// because it is not there or because loading it failed.
    LoadFailed,
    /// A library was loaded but does not export the project-owned C ABI, or
    /// exports a different ABI version than this build understands.
    AbiMismatch,
    /// The library loaded and matched, but refused to create an instance.
    CreateFailed
};

/// One distilled surface input, already mapped onto the hdSilk material wire
/// ids. The MDL C ABI's own ids are translated explicitly on the way in, so a
/// future divergence between the two enumerations is a compile error rather
/// than a silently mis-bound parameter.
struct HdSilkMdlDistilledScalar
{
    uint32_t parameter = 0;
    uint32_t componentCount = 0;
    float value[4] = {0.0F, 0.0F, 0.0F, 0.0F};
};

struct HdSilkMdlDistilledTexture
{
    uint32_t parameter = 0;
    uint32_t componentCount = 0;
    uint32_t outputChannel = 0;
    uint32_t wrapS = 0;
    uint32_t wrapT = 0;
    uint32_t colorSpace = 0;
    float scale[4] = {1.0F, 1.0F, 1.0F, 1.0F};
    float bias[4] = {0.0F, 0.0F, 0.0F, 0.0F};
    std::string asset;
};

/// The outcome of one distillation request. `succeeded` is true only when the
/// adapter produced at least one surface input; every other outcome carries a
/// diagnostic naming the material and the reason.
struct HdSilkMdlDistillation
{
    bool succeeded = false;
    std::string diagnostic;
    std::vector<HdSilkMdlDistilledScalar> scalars;
    std::vector<HdSilkMdlDistilledTexture> textures;
    std::vector<std::string> unsupportedParameters;
};

/// One authored MDL shader input as hdSilk read it out of the Hydra material
/// network. Only value kinds the C ABI can express are built; anything else is
/// reported by the caller instead of being converted approximately.
struct HdSilkMdlParameter
{
    std::string name;
    uint32_t kind = 0;
    uint32_t componentCount = 0;
    float value[4] = {0.0F, 0.0F, 0.0F, 0.0F};
    int32_t integerValue = 0;
    std::string text;
};

/// Process-wide accessor for the optional adapter.
///
/// The library is looked up once, lazily, and the result -- including the
/// reason it is unusable -- is cached for the life of the process.
///
/// Resolution is deliberately narrow. The default location is the *absolute*
/// sibling of the module that hosts this loader, formed from that module's own
/// path; there is no bare-library-name load, because passing a bare name to the
/// platform loader lets it search directories this process does not control,
/// which on Windows can include the process directory and, depending on the
/// safe-search setting, the current directory. An operator can override the
/// location with OPENUSD_MDL_ADAPTER_PATH, which must be absolute for the same
/// reason. That single variable is the only environment this loader reads: it
/// never enumerates the environment block and never records anything but the
/// one path it was asked to load.
class HdSilkMdlAdapter
{
public:
    /// Returns the load state, loading the library on the first call.
    static HdSilkMdlAdapterState GetState();

    /// Returns the provenance string the loaded adapter reports, or the reason
    /// no adapter is available. Never empty.
    static std::string GetDescription();

    /// Returns the absolute path this loader last attempted, or an empty string
    /// when it never formed one. Exposed for diagnostics and probes.
    static std::string GetResolvedPath();

    /// Distils one material. Safe to call from several threads; the adapter
    /// instance itself is serialized internally because the C ABI does not
    /// promise thread safety.
    static HdSilkMdlDistillation Distill(
        const std::string& materialPath,
        const std::string& moduleUri,
        const std::string& materialName,
        const std::vector<HdSilkMdlParameter>& parameters);

    /// Drops the cached load result and resolves again. Test-only: a probe
    /// needs to prove several resolution outcomes in one process.
    static void ResetForTesting();

private:
    HdSilkMdlAdapter() = delete;
};

PXR_NAMESPACE_CLOSE_SCOPE

#endif
