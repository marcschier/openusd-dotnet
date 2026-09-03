// Copyright (c) marcschier. Licensed under the MIT License.
//
// Dynamic loader for the optional openusd_mdl adapter. hdSilk links no MDL
// code: the adapter is resolved by name at run time, and every failure to
// resolve it is a reported state rather than a fatal condition, because the
// default install ships no adapter at all.

#include "mdlAdapter.h"

#include "openusd_hdsilk.h"

#include "pxr/base/arch/env.h"
#include "pxr/base/tf/diagnostic.h"

#include <mutex>

#if defined(_WIN32)
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#else
#include <dlfcn.h>
#include <limits.h>
#include <stdlib.h>
#include <sys/stat.h>
#endif

PXR_NAMESPACE_OPEN_SCOPE

namespace
{
#if defined(_WIN32)
using LibraryHandle = HMODULE;
constexpr const char* kAdapterLibraryName = "openusd_mdl.dll";
#elif defined(__APPLE__)
using LibraryHandle = void*;
constexpr const char* kAdapterLibraryName = "libopenusd_mdl.dylib";
#else
using LibraryHandle = void*;
constexpr const char* kAdapterLibraryName = "libopenusd_mdl.so";
#endif

constexpr const char* kAdapterPathVariable = "OPENUSD_MDL_ADAPTER_PATH";
/// Path-list of directories an SDK-backed adapter may resolve MDL modules from.
/// Every entry must be absolute; a relative one is dropped with the rest of the
/// list intact, because the alternative -- resolving it against the process
/// working directory -- is the search this runtime refuses everywhere.
constexpr const char* kModulePathVariable = "OPENUSD_MDL_MODULE_PATH";

/// Anchors the module lookup below. Its address is inside whichever module this
/// translation unit was linked into -- openusd_hdsilk in a real runtime -- which
/// is the module whose directory the adapter must sit beside.
void
_ModuleAnchor()
{
}

bool
IsAbsolutePath(const std::string& path)
{
#if defined(_WIN32)
    // A drive-qualified path or a UNC path. A drive-relative path such as
    // "C:openusd_mdl.dll" is deliberately *not* absolute: it resolves against
    // the per-drive working directory.
    if (path.size() >= 3 && (path[1] == ':') &&
        (path[2] == '\\' || path[2] == '/'))
    {
        return true;
    }
    return path.size() >= 2 && (path[0] == '\\' || path[0] == '/') &&
        (path[1] == '\\' || path[1] == '/');
#else
    return !path.empty() && path[0] == '/';
#endif
}

/// Returns the absolute directory of the module hosting this loader, or an
/// empty string when it cannot be determined.
/// Splits and validates the configured module search path. Bounded to the ABI's
/// declared maximum so a pathological value cannot make the adapter hold an
/// unbounded list; entries past the bound are dropped rather than truncating a
/// path in half.
std::vector<std::string>
ReadModuleSearchPaths()
{
    std::vector<std::string> paths;
    const std::string configured = ArchGetEnv(kModulePathVariable);
    if (configured.empty())
    {
        return paths;
    }
#if defined(_WIN32)
    const char separator = ';';
#else
    const char separator = ':';
#endif
    size_t start = 0;
    while (start <= configured.size() && paths.size() < OPENUSD_MDL_MAX_SEARCH_PATHS)
    {
        size_t end = configured.find(separator, start);
        if (end == std::string::npos)
        {
            end = configured.size();
        }
        std::string entry = configured.substr(start, end - start);
        start = end + 1;
        if (entry.empty() || entry.size() > OPENUSD_MDL_MAX_PATH_BYTES ||
            !IsAbsolutePath(entry))
        {
            continue;
        }
        paths.push_back(std::move(entry));
        if (end == configured.size())
        {
            break;
        }
    }
    return paths;
}

/// A stable identity for one search-path configuration, used as the adapter's
/// cache generation so a different configuration cannot be answered from a
/// module compiled under the previous one.
uint64_t
HashSearchPaths(const std::vector<std::string>& paths)
{
    uint64_t hash = 1469598103934665603ULL;
    for (const std::string& path : paths)
    {
        for (const char character : path)
        {
            hash ^= static_cast<uint64_t>(static_cast<unsigned char>(character));
            hash *= 1099511628211ULL;
        }
        hash ^= 0x1FULL;
        hash *= 1099511628211ULL;
    }
    return hash;
}

std::string
GetHostModuleDirectory()
{
#if defined(_WIN32)
    HMODULE module = nullptr;
    if (GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&_ModuleAnchor),
            &module) == 0 ||
        module == nullptr)
    {
        return std::string();
    }
    std::wstring path(MAX_PATH, L'\0');
    for (;;)
    {
        const DWORD written = GetModuleFileNameW(
            module, path.data(), static_cast<DWORD>(path.size()));
        if (written == 0)
        {
            return std::string();
        }
        if (written < path.size())
        {
            path.resize(written);
            break;
        }
        if (path.size() >= 32768)
        {
            return std::string();
        }
        path.resize(path.size() * 2);
    }
    const size_t separator = path.find_last_of(L"\\/");
    if (separator == std::wstring::npos)
    {
        return std::string();
    }
    path.resize(separator);
    const int size = WideCharToMultiByte(
        CP_UTF8,
        0,
        path.c_str(),
        static_cast<int>(path.size()),
        nullptr,
        0,
        nullptr,
        nullptr);
    if (size <= 0)
    {
        return std::string();
    }
    std::string utf8(static_cast<size_t>(size), '\0');
    WideCharToMultiByte(
        CP_UTF8,
        0,
        path.c_str(),
        static_cast<int>(path.size()),
        utf8.data(),
        size,
        nullptr,
        nullptr);
    return utf8;
#else
    Dl_info info{};
    if (dladdr(reinterpret_cast<void*>(&_ModuleAnchor), &info) == 0 ||
        info.dli_fname == nullptr)
    {
        return std::string();
    }
    std::string path(info.dli_fname);
    if (!IsAbsolutePath(path))
    {
        // dli_fname reports the name the module was opened with, which can be
        // relative. Resolving it is what makes the sibling path absolute.
        char resolved[PATH_MAX];
        if (realpath(path.c_str(), resolved) == nullptr)
        {
            return std::string();
        }
        path.assign(resolved);
    }
    const size_t separator = path.find_last_of('/');
    if (separator == std::string::npos)
    {
        return std::string();
    }
    path.resize(separator);
    return path;
#endif
}

std::string
JoinPath(const std::string& directory, const char* fileName)
{
#if defined(_WIN32)
    const char separator = '\\';
#else
    const char separator = '/';
#endif
    if (directory.empty())
    {
        return std::string();
    }
    std::string path(directory);
    if (path.back() != '\\' && path.back() != '/')
    {
        path.push_back(separator);
    }
    path.append(fileName);
    return path;
}

bool
PathExists(const std::string& path)
{
#if defined(_WIN32)
    const int wideLength = MultiByteToWideChar(
        CP_UTF8, 0, path.c_str(), static_cast<int>(path.size()), nullptr, 0);
    if (wideLength <= 0)
    {
        return false;
    }
    std::wstring widePath(static_cast<size_t>(wideLength), L'\0');
    MultiByteToWideChar(
        CP_UTF8,
        0,
        path.c_str(),
        static_cast<int>(path.size()),
        widePath.data(),
        wideLength);
    const DWORD attributes = GetFileAttributesW(widePath.c_str());
    return attributes != INVALID_FILE_ATTRIBUTES &&
        (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0;
#else
    struct stat status{};
    return stat(path.c_str(), &status) == 0 && S_ISREG(status.st_mode);
#endif
}

LibraryHandle
OpenLibraryAbsolute(const std::string& path)
{
#if defined(_WIN32)
    const int wideLength = MultiByteToWideChar(
        CP_UTF8, 0, path.c_str(), static_cast<int>(path.size()), nullptr, 0);
    if (wideLength <= 0)
    {
        return nullptr;
    }
    std::wstring widePath(static_cast<size_t>(wideLength), L'\0');
    MultiByteToWideChar(
        CP_UTF8,
        0,
        path.c_str(),
        static_cast<int>(path.size()),
        widePath.data(),
        wideLength);
    // LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR lets the adapter's own dependencies
    // resolve from the directory the adapter was loaded from, and
    // LOAD_LIBRARY_SEARCH_DEFAULT_DIRS keeps the system directories available.
    // Naming both replaces the legacy search entirely, so neither the process
    // directory nor the current directory participates.
    return LoadLibraryExW(
        widePath.c_str(),
        nullptr,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
#else
    return dlopen(path.c_str(), RTLD_NOW | RTLD_LOCAL);
#endif
}

void*
ResolveSymbol(LibraryHandle handle, const char* name)
{
#if defined(_WIN32)
    return reinterpret_cast<void*>(GetProcAddress(handle, name));
#else
    return dlsym(handle, name);
#endif
}

void
CloseLibrary(LibraryHandle handle)
{
#if defined(_WIN32)
    FreeLibrary(handle);
#else
    dlclose(handle);
#endif
}

/// The resolved entry points of one loaded adapter library.
struct AdapterBinding
{
    LibraryHandle handle = nullptr;
    openusd_mdl_describe_fn describe = nullptr;
    openusd_mdl_capabilities_fn capabilities = nullptr;
    openusd_mdl_adapter_create_fn create = nullptr;
    openusd_mdl_adapter_destroy_fn destroy = nullptr;
    openusd_mdl_adapter_distill_fn distill = nullptr;
    openusd_mdl_adapter_release_result_fn releaseResult = nullptr;
    openusd_mdl_adapter* instance = nullptr;
};

/// The one cached load attempt, plus the mutex that serializes calls into the
/// adapter. The C ABI states that instances are not thread safe, and hdSilk
/// syncs material Sprims in parallel, so the lock is a correctness requirement
/// rather than a convenience.
struct AdapterCache
{
    std::once_flag once;
    std::mutex lock;
    HdSilkMdlAdapterState state = HdSilkMdlAdapterState::NotInstalled;
    std::string description;
    std::string resolvedPath;
    std::vector<std::string> searchPaths;
    AdapterBinding binding;
};

AdapterCache&
GetCache()
{
    static AdapterCache cache;
    return cache;
}

std::string
DescribeState(HdSilkMdlAdapterState state)
{
    switch (state)
    {
        case HdSilkMdlAdapterState::Loaded:
            return "loaded";
        case HdSilkMdlAdapterState::NotInstalled:
            return "no openusd_mdl adapter is installed beside the hdSilk "
                   "library, and no absolute OPENUSD_MDL_ADAPTER_PATH is set; "
                   "MDL material distillation is unavailable in this build";
        case HdSilkMdlAdapterState::ModulePathUnavailable:
            return "the directory of the module hosting the hdSilk MDL loader "
                   "could not be determined, so the adapter's only safe default "
                   "location cannot be formed";
        case HdSilkMdlAdapterState::PathNotAbsolute:
            return "OPENUSD_MDL_ADAPTER_PATH is not an absolute path; a "
                   "relative path would be resolved against the process working "
                   "directory, which this loader refuses to search";
        case HdSilkMdlAdapterState::LoadFailed:
            return "the openusd_mdl adapter named by an absolute path could not "
                   "be loaded";
        case HdSilkMdlAdapterState::AbiMismatch:
            return "the openusd_mdl adapter does not implement ABI version " +
                std::to_string(OPENUSD_MDL_ABI_VERSION);
        case HdSilkMdlAdapterState::CreateFailed:
            return "the openusd_mdl adapter refused to create an instance";
    }
    return "unknown openusd_mdl adapter state";
}

void
ReleaseBinding(AdapterBinding& binding)
{
    if (binding.instance != nullptr && binding.destroy != nullptr)
    {
        binding.destroy(binding.instance);
    }
    binding.instance = nullptr;
    if (binding.handle != nullptr)
    {
        CloseLibrary(binding.handle);
    }
    binding.handle = nullptr;
}

void
LoadAdapter(AdapterCache& cache)
{
    ReleaseBinding(cache.binding);
    cache.resolvedPath.clear();
    cache.searchPaths.clear();

    // Exactly one environment variable is read, by name. The environment block
    // is never enumerated and nothing but this one path is recorded, so no
    // unrelated variable -- credential, token, or otherwise -- can reach a
    // diagnostic through this loader.
    const std::string configuredPath = ArchGetEnv(kAdapterPathVariable);
    std::string path;
    if (!configuredPath.empty())
    {
        if (!IsAbsolutePath(configuredPath))
        {
            cache.state = HdSilkMdlAdapterState::PathNotAbsolute;
            cache.description = DescribeState(cache.state);
            return;
        }
        path = configuredPath;
    }
    else
    {
        const std::string directory = GetHostModuleDirectory();
        if (directory.empty())
        {
            cache.state = HdSilkMdlAdapterState::ModulePathUnavailable;
            cache.description = DescribeState(cache.state);
            return;
        }
        path = JoinPath(directory, kAdapterLibraryName);
        if (!PathExists(path))
        {
            // Nothing beside the hdSilk library. This is the ordinary state of
            // every base package, and it is reported without attempting a load,
            // so no platform search of any other directory can occur.
            cache.state = HdSilkMdlAdapterState::NotInstalled;
            cache.description = DescribeState(cache.state);
            cache.resolvedPath = path;
            return;
        }
    }

    cache.resolvedPath = path;
    AdapterBinding binding;
    binding.handle = OpenLibraryAbsolute(path);
    if (binding.handle == nullptr)
    {
        // The path was absolute and, for the default location, the file was
        // there a moment ago. Either way the operator named a specific library
        // and did not get it, which is a different condition from "nothing
        // installed".
        cache.state = HdSilkMdlAdapterState::LoadFailed;
        cache.description = DescribeState(cache.state);
        return;
    }

    auto abiVersion = reinterpret_cast<openusd_mdl_abi_version_fn>(
        ResolveSymbol(binding.handle, "openusd_mdl_abi_version"));
    binding.describe = reinterpret_cast<openusd_mdl_describe_fn>(
        ResolveSymbol(binding.handle, "openusd_mdl_describe"));
    binding.capabilities = reinterpret_cast<openusd_mdl_capabilities_fn>(
        ResolveSymbol(binding.handle, "openusd_mdl_capabilities"));
    binding.create = reinterpret_cast<openusd_mdl_adapter_create_fn>(
        ResolveSymbol(binding.handle, "openusd_mdl_adapter_create"));
    binding.destroy = reinterpret_cast<openusd_mdl_adapter_destroy_fn>(
        ResolveSymbol(binding.handle, "openusd_mdl_adapter_destroy"));
    binding.distill = reinterpret_cast<openusd_mdl_adapter_distill_fn>(
        ResolveSymbol(binding.handle, "openusd_mdl_adapter_distill"));
    binding.releaseResult =
        reinterpret_cast<openusd_mdl_adapter_release_result_fn>(
            ResolveSymbol(binding.handle, "openusd_mdl_adapter_release_result"));

    if (abiVersion == nullptr || binding.create == nullptr ||
        binding.destroy == nullptr || binding.distill == nullptr ||
        binding.releaseResult == nullptr ||
        abiVersion() != OPENUSD_MDL_ABI_VERSION)
    {
        ReleaseBinding(binding);
        cache.state = HdSilkMdlAdapterState::AbiMismatch;
        cache.description = DescribeState(cache.state);
        return;
    }

    const std::vector<std::string> searchPaths = ReadModuleSearchPaths();
    std::vector<openusd_mdl_string> searchPathViews;
    searchPathViews.reserve(searchPaths.size());
    for (const std::string& searchPath : searchPaths)
    {
        openusd_mdl_string view{};
        view.data = searchPath.c_str();
        view.size = static_cast<uint32_t>(searchPath.size());
        searchPathViews.push_back(view);
    }
    openusd_mdl_adapter_options options{};
    options.struct_size = static_cast<uint32_t>(sizeof(options));
    options.module_search_paths =
        searchPathViews.empty() ? nullptr : searchPathViews.data();
    options.module_search_path_count =
        static_cast<uint32_t>(searchPathViews.size());
    // The generation is derived from the configured paths, so a run that
    // changes them invalidates whatever the adapter cached about modules
    // resolved under the previous set.
    options.cache_generation = HashSearchPaths(searchPaths);

    if (binding.create(&options, &binding.instance) != OPENUSD_MDL_STATUS_OK ||
        binding.instance == nullptr)
    {
        ReleaseBinding(binding);
        cache.state = HdSilkMdlAdapterState::CreateFailed;
        cache.description = DescribeState(cache.state);
        return;
    }
    cache.searchPaths = searchPaths;

    std::string description = "openusd_mdl adapter";
    if (binding.describe != nullptr)
    {
        const uint32_t required = binding.describe(nullptr, 0);
        if (required > 1)
        {
            std::string text(static_cast<size_t>(required), '\0');
            if (binding.describe(text.data(), required) == required)
            {
                text.resize(static_cast<size_t>(required) - 1);
                description = text;
            }
        }
    }

    cache.binding = binding;
    cache.state = HdSilkMdlAdapterState::Loaded;
    cache.description = description;
}

/// Maps one MDL ABI surface-input id onto the hdSilk material wire id. The
/// switch is exhaustive on purpose: an id this build does not map is refused,
/// never guessed.
bool
TryMapSurfaceInput(uint32_t surfaceInput, uint32_t* parameter)
{
    switch (surfaceInput)
    {
        case OPENUSD_MDL_SURFACE_DIFFUSE_COLOR:
            *parameter = OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR;
            return true;
        case OPENUSD_MDL_SURFACE_EMISSIVE_COLOR:
            *parameter = OPENUSD_SILK_MATERIAL_EMISSIVE_COLOR;
            return true;
        case OPENUSD_MDL_SURFACE_METALLIC:
            *parameter = OPENUSD_SILK_MATERIAL_METALLIC;
            return true;
        case OPENUSD_MDL_SURFACE_ROUGHNESS:
            *parameter = OPENUSD_SILK_MATERIAL_ROUGHNESS;
            return true;
        case OPENUSD_MDL_SURFACE_OPACITY:
            *parameter = OPENUSD_SILK_MATERIAL_OPACITY;
            return true;
        case OPENUSD_MDL_SURFACE_OPACITY_THRESHOLD:
            *parameter = OPENUSD_SILK_MATERIAL_OPACITY_THRESHOLD;
            return true;
        case OPENUSD_MDL_SURFACE_IOR:
            *parameter = OPENUSD_SILK_MATERIAL_IOR;
            return true;
        case OPENUSD_MDL_SURFACE_NORMAL:
            *parameter = OPENUSD_SILK_MATERIAL_NORMAL;
            return true;
        case OPENUSD_MDL_SURFACE_OCCLUSION:
            *parameter = OPENUSD_SILK_MATERIAL_OCCLUSION;
            return true;
        default:
            return false;
    }
}

bool
TryMapWrap(uint32_t wrap, uint32_t* out)
{
    switch (wrap)
    {
        case OPENUSD_MDL_WRAP_BLACK:
            *out = OPENUSD_SILK_WRAP_BLACK;
            return true;
        case OPENUSD_MDL_WRAP_CLAMP:
            *out = OPENUSD_SILK_WRAP_CLAMP;
            return true;
        case OPENUSD_MDL_WRAP_REPEAT:
            *out = OPENUSD_SILK_WRAP_REPEAT;
            return true;
        case OPENUSD_MDL_WRAP_MIRROR:
            *out = OPENUSD_SILK_WRAP_MIRROR;
            return true;
        default:
            return false;
    }
}

bool
TryMapChannel(uint32_t channel, uint32_t* out)
{
    switch (channel)
    {
        case OPENUSD_MDL_CHANNEL_R:
            *out = OPENUSD_SILK_TEXTURE_CHANNEL_R;
            return true;
        case OPENUSD_MDL_CHANNEL_G:
            *out = OPENUSD_SILK_TEXTURE_CHANNEL_G;
            return true;
        case OPENUSD_MDL_CHANNEL_B:
            *out = OPENUSD_SILK_TEXTURE_CHANNEL_B;
            return true;
        case OPENUSD_MDL_CHANNEL_A:
            *out = OPENUSD_SILK_TEXTURE_CHANNEL_A;
            return true;
        case OPENUSD_MDL_CHANNEL_RGB:
            *out = OPENUSD_SILK_TEXTURE_CHANNEL_RGB;
            return true;
        default:
            return false;
    }
}

bool
TryMapColorSpace(uint32_t colorSpace, uint32_t* out)
{
    switch (colorSpace)
    {
        case OPENUSD_MDL_COLOR_SPACE_AUTO:
            *out = OPENUSD_SILK_COLOR_SPACE_AUTO;
            return true;
        case OPENUSD_MDL_COLOR_SPACE_RAW:
            *out = OPENUSD_SILK_COLOR_SPACE_RAW;
            return true;
        case OPENUSD_MDL_COLOR_SPACE_SRGB:
            *out = OPENUSD_SILK_COLOR_SPACE_SRGB;
            return true;
        default:
            return false;
    }
}

std::string
ToStdString(const openusd_mdl_string& value)
{
    if (value.data == nullptr || value.size == 0)
    {
        return std::string();
    }
    return std::string(value.data, value.size);
}
}

HdSilkMdlAdapterState
HdSilkMdlAdapter::GetState()
{
    AdapterCache& cache = GetCache();
    std::call_once(cache.once, [&cache]() { LoadAdapter(cache); });
    return cache.state;
}

std::string
HdSilkMdlAdapter::GetDescription()
{
    AdapterCache& cache = GetCache();
    std::call_once(cache.once, [&cache]() { LoadAdapter(cache); });
    return cache.description.empty() ? DescribeState(cache.state)
                                     : cache.description;
}

std::string
HdSilkMdlAdapter::GetResolvedPath()
{
    AdapterCache& cache = GetCache();
    std::call_once(cache.once, [&cache]() { LoadAdapter(cache); });
    return cache.resolvedPath;
}

HdSilkMdlDistillation
HdSilkMdlAdapter::Distill(
    const std::string& materialPath,
    const std::string& moduleUri,
    const std::string& materialName,
    const std::vector<HdSilkMdlParameter>& parameters)
{
    HdSilkMdlDistillation distillation;
    if (GetState() != HdSilkMdlAdapterState::Loaded)
    {
        distillation.diagnostic = GetDescription();
        return distillation;
    }

    std::vector<openusd_mdl_parameter> wire;
    wire.reserve(parameters.size());
    for (const HdSilkMdlParameter& parameter : parameters)
    {
        openusd_mdl_parameter entry{};
        entry.name.data = parameter.name.c_str();
        entry.name.size = static_cast<uint32_t>(parameter.name.size());
        entry.kind = parameter.kind;
        entry.component_count = parameter.componentCount;
        for (size_t index = 0; index < 4; ++index)
        {
            entry.value[index] = parameter.value[index];
        }
        entry.integer_value = parameter.integerValue;
        entry.text.data = parameter.text.c_str();
        entry.text.size = static_cast<uint32_t>(parameter.text.size());
        wire.push_back(entry);
    }

    openusd_mdl_material_request request{};
    request.struct_size = static_cast<uint32_t>(sizeof(request));
    request.module_uri.data = moduleUri.c_str();
    request.module_uri.size = static_cast<uint32_t>(moduleUri.size());
    request.material_name.data = materialName.c_str();
    request.material_name.size = static_cast<uint32_t>(materialName.size());
    request.material_path.data = materialPath.c_str();
    request.material_path.size = static_cast<uint32_t>(materialPath.size());
    request.parameters = wire.empty() ? nullptr : wire.data();
    request.parameter_count = static_cast<uint32_t>(wire.size());

    AdapterCache& cache = GetCache();
    std::lock_guard<std::mutex> guard(cache.lock);
    const openusd_mdl_distilled_material* result = nullptr;
    const uint32_t status =
        cache.binding.distill(cache.binding.instance, &request, &result);
    if (result == nullptr)
    {
        distillation.diagnostic =
            "the openusd_mdl adapter returned no result (status " +
            std::to_string(status) + ")";
        return distillation;
    }
    if (result->struct_size != sizeof(openusd_mdl_distilled_material))
    {
        cache.binding.releaseResult(cache.binding.instance, result);
        distillation.diagnostic =
            "the openusd_mdl adapter returned a result of an unexpected size";
        return distillation;
    }

    for (uint32_t index = 0; index < result->unsupported_parameter_count; ++index)
    {
        distillation.unsupportedParameters.push_back(
            ToStdString(result->unsupported_parameters[index]));
    }

    if (status != OPENUSD_MDL_STATUS_OK)
    {
        distillation.diagnostic = ToStdString(result->diagnostic);
        if (distillation.diagnostic.empty())
        {
            distillation.diagnostic =
                "the openusd_mdl adapter refused the material (status " +
                std::to_string(status) + ")";
        }
        cache.binding.releaseResult(cache.binding.instance, result);
        return distillation;
    }

    bool mapped = true;
    for (uint32_t index = 0; index < result->scalar_count && mapped; ++index)
    {
        const openusd_mdl_distilled_scalar& source = result->scalars[index];
        HdSilkMdlDistilledScalar scalar;
        if (!TryMapSurfaceInput(source.surface_input, &scalar.parameter) ||
            source.component_count == 0 || source.component_count > 4)
        {
            mapped = false;
            break;
        }
        scalar.componentCount = source.component_count;
        for (uint32_t component = 0; component < scalar.componentCount; ++component)
        {
            scalar.value[component] = source.value[component];
        }
        distillation.scalars.push_back(scalar);
    }
    for (uint32_t index = 0; index < result->texture_count && mapped; ++index)
    {
        const openusd_mdl_distilled_texture& source = result->textures[index];
        HdSilkMdlDistilledTexture texture;
        if (!TryMapSurfaceInput(source.surface_input, &texture.parameter) ||
            !TryMapWrap(source.wrap_s, &texture.wrapS) ||
            !TryMapWrap(source.wrap_t, &texture.wrapT) ||
            !TryMapChannel(source.output_channel, &texture.outputChannel) ||
            !TryMapColorSpace(source.color_space, &texture.colorSpace) ||
            source.component_count == 0 || source.component_count > 4)
        {
            mapped = false;
            break;
        }
        texture.componentCount = source.component_count;
        for (size_t component = 0; component < 4; ++component)
        {
            texture.scale[component] = source.scale[component];
            texture.bias[component] = source.bias[component];
        }
        texture.asset = ToStdString(source.asset);
        if (texture.asset.empty())
        {
            mapped = false;
            break;
        }
        distillation.textures.push_back(std::move(texture));
    }

    cache.binding.releaseResult(cache.binding.instance, result);

    if (!mapped)
    {
        distillation.scalars.clear();
        distillation.textures.clear();
        distillation.diagnostic =
            "the openusd_mdl adapter distilled a surface input this build does "
            "not carry on the material wire format";
        return distillation;
    }
    if (distillation.scalars.empty() && distillation.textures.empty())
    {
        distillation.diagnostic =
            "the openusd_mdl adapter reported success without distilling any "
            "surface input";
        return distillation;
    }

    distillation.succeeded = true;
    return distillation;
}

void
HdSilkMdlAdapter::ResetForTesting()
{
    AdapterCache& cache = GetCache();
    // Force the one-time load to have run, so the reload below is the only
    // load the rest of the process sees.
    (void)GetState();
    std::lock_guard<std::mutex> guard(cache.lock);
    LoadAdapter(cache);
}

PXR_NAMESPACE_CLOSE_SCOPE
