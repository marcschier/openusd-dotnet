// Copyright (c) marcschier. Licensed under the MIT License.
//
// MDL SDK-backed module evaluation for the optional openusd_mdl adapter.
//
// This translation unit is the only place an MDL SDK type appears. It compiles
// against the neuraylib headers of the baseline pinned in eng/mdl.lock.json and
// links nothing: the runtime is loaded from a user-supplied location at run
// time through the documented mi_factory entry point, so no MDL SDK binary is
// built into, or redistributed by, anything this repository produces.
//
// What it does: load a module from an explicitly configured search path,
// compile it, find the named material, and reduce its SDK-proven variant
// interface or parameter defaults to plain values. It does not evaluate BSDFs,
// generate shader code, or fold a call it does not recognise. Each of those is
// reported by parameter name instead.

#include "sdk_backend.h"

#include "openusd_mdl.h"

#include <mutex>

#if defined(OPENUSD_MDL_WITH_SDK)

#include <algorithm>
#include <cmath>
#include <cstring>
#include <filesystem>
#include <set>
#include <utility>

#include <mi/base/handle.h>
#include <mi/neuraylib/factory.h>
#include <mi/neuraylib/idatabase.h>
#include <mi/neuraylib/ifunction_call.h>
#include <mi/neuraylib/ifunction_definition.h>
#include <mi/neuraylib/iimage.h>
#include <mi/neuraylib/imdl_configuration.h>
#include <mi/neuraylib/imdl_entity_resolver.h>
#include <mi/neuraylib/imdl_execution_context.h>
#include <mi/neuraylib/imdl_factory.h>
#include <mi/neuraylib/imdl_impexp_api.h>
#include <mi/neuraylib/imodule.h>
#include <mi/neuraylib/ineuray.h>
#include <mi/neuraylib/iscope.h>
#include <mi/neuraylib/istring.h>
#include <mi/neuraylib/itexture.h>
#include <mi/neuraylib/itransaction.h>
#include <mi/neuraylib/ivalue.h>

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
#endif

#endif

namespace openusd_mdl
{
namespace
{
std::mutex&
BackendLock()
{
    static std::mutex lock;
    return lock;
}

constexpr const char* kRuntimePathVariable = "OPENUSD_MDL_SDK_RUNTIME";

#if defined(OPENUSD_MDL_WITH_SDK)

constexpr size_t kMaxModuleScopes = 32;
constexpr unsigned int kMaxPrototypeDepth = 8;
constexpr unsigned int kMaxExpressionDepth = 16;
constexpr unsigned int kMaxExpressionWork = 4096;
constexpr size_t kMaxDiagnosticBytes = 32768;

#if defined(_WIN32)
using RuntimeHandle = HMODULE;
constexpr const char* kRuntimeLibraryName = "libmdl_sdk.dll";
#elif defined(__APPLE__)
using RuntimeHandle = void*;
constexpr const char* kRuntimeLibraryName = "libmdl_sdk.dylib";
#else
using RuntimeHandle = void*;
constexpr const char* kRuntimeLibraryName = "libmdl_sdk.so";
#endif

bool
IsAbsolute(const std::string& path)
{
#if defined(_WIN32)
    if (path.size() >= 3 && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
    {
        return true;
    }
    return path.size() >= 2 && (path[0] == '\\' || path[0] == '/') &&
        (path[1] == '\\' || path[1] == '/');
#else
    return !path.empty() && path[0] == '/';
#endif
}

std::string
ReadEnvironment(const char* name)
{
#if defined(_WIN32)
    // GetEnvironmentVariable, not _dupenv_s: the CRT keeps its own copy of the
    // environment, and a caller that set the variable through the Win32 API --
    // which is what OpenUSD's ArchSetEnv does -- updates the process block the
    // CRT copy does not see.
    DWORD required = GetEnvironmentVariableA(name, nullptr, 0);
    if (required == 0)
    {
        return std::string();
    }
    std::string value(static_cast<size_t>(required), '\0');
    const DWORD written =
        GetEnvironmentVariableA(name, value.data(), required);
    if (written == 0 || written >= required)
    {
        return std::string();
    }
    value.resize(written);
    return value;
#else
    const char* value = getenv(name);
    return value == nullptr ? std::string() : std::string(value);
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
    std::string path(directory);
    if (!path.empty() && path.back() != '\\' && path.back() != '/')
    {
        path.push_back(separator);
    }
    path.append(fileName);
    return path;
}

RuntimeHandle
OpenRuntime(const std::string& path)
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
    return LoadLibraryExW(
        widePath.c_str(),
        nullptr,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
#else
    return dlopen(path.c_str(), RTLD_NOW | RTLD_LOCAL);
#endif
}

void*
ResolveRuntimeSymbol(RuntimeHandle handle, const char* name)
{
#if defined(_WIN32)
    return reinterpret_cast<void*>(GetProcAddress(handle, name));
#else
    return dlsym(handle, name);
#endif
}

/// Everything the backend owns once a runtime is up. Kept in one struct so the
/// teardown order -- transaction, then api components, then neuray, then the
/// library -- is stated in one place.
struct RuntimeState
{
    RuntimeHandle handle = nullptr;
    mi::base::Handle<mi::neuraylib::INeuray> neuray;
    mi::base::Handle<mi::neuraylib::IMdl_factory> factory;
    mi::base::Handle<mi::neuraylib::IMdl_impexp_api> impexp;
    mi::base::Handle<mi::neuraylib::IDatabase> database;
    bool started = false;
    std::string description;
    std::string failure;
    std::vector<std::string> searchPaths;
    uint64_t generation = 0;
    bool configured = false;
    // SDK names alone are not file identities. Sibling scopes prevent modules
    // with identical qualified names in different configured roots from mixing.
    std::map<
        std::pair<std::string, std::string>,
        mi::base::Handle<mi::neuraylib::IScope>> moduleScopes;
};

RuntimeState&
State()
{
    // Deliberately never destroyed. The neuray handles this owns belong to a
    // dynamically loaded runtime, and running their destructors during static
    // teardown -- after that runtime may already have been unloaded -- is a
    // crash with no upside. A process that is exiting has nothing to reclaim.
    static RuntimeState* state = new RuntimeState();
    return *state;
}

/// Loads libmdl_sdk from an explicit, absolute, user-supplied location. There is
/// no bare-name load and no search of the working directory, for the same reason
/// the adapter loader itself refuses one.
bool
EnsureRuntimeLoaded(RuntimeState& state)
{
    if (state.handle != nullptr)
    {
        return state.neuray.is_valid_interface();
    }
    if (!state.failure.empty())
    {
        return false;
    }

    const std::string configured = ReadEnvironment(kRuntimePathVariable);
    if (configured.empty())
    {
        state.failure =
            "no MDL SDK runtime is configured; set OPENUSD_MDL_SDK_RUNTIME to the "
            "absolute path of the directory holding libmdl_sdk, or to that library "
            "itself";
        return false;
    }
    if (!IsAbsolute(configured))
    {
        state.failure =
            "OPENUSD_MDL_SDK_RUNTIME is not an absolute path; a relative path would "
            "be resolved against the process working directory";
        return false;
    }

    // A directory is accepted as a convenience, because that is what the SDK
    // archive extracts to; the library name is appended rather than searched for.
    std::string path = configured;
    if (path.find("libmdl_sdk") == std::string::npos)
    {
        path = JoinPath(configured, kRuntimeLibraryName);
    }

    state.handle = OpenRuntime(path);
    if (state.handle == nullptr)
    {
        state.failure = "could not load the MDL SDK runtime at '" + path + "'";
        return false;
    }

    // mi_factory is the documented entry point; the neuraylib helper turns the
    // raw symbol into the versioned interface, so the API version check is the
    // SDK's own rather than a version number this file restates.
    void* symbol = ResolveRuntimeSymbol(state.handle, "mi_factory");
    if (symbol == nullptr)
    {
        state.failure = "'" + path + "' exports no mi_factory entry point";
        return false;
    }

    state.neuray = mi::base::Handle<mi::neuraylib::INeuray>(
        mi::neuraylib::mi_factory<mi::neuraylib::INeuray>(symbol));
    if (!state.neuray.is_valid_interface())
    {
        state.failure = "the MDL SDK runtime at '" + path +
            "' does not implement the neuray API version this adapter was built "
            "against";
        return false;
    }

    const char* version = state.neuray->get_version();
    state.description = std::string("MDL SDK ") +
        (version == nullptr ? "(unknown version)" : version) + " loaded from " + path;
    return true;
}

/// Applies the caller's search paths and starts neuray. The MDL system and user
/// paths are deliberately not added: the only directories this backend resolves
/// modules from are the ones the caller named.
uint32_t
ConfigureRuntime(
    RuntimeState& state,
    const std::vector<std::string>& searchPaths,
    std::string* diagnostic)
{
    mi::base::Handle<mi::neuraylib::IMdl_configuration> configuration(
        state.neuray->get_api_component<mi::neuraylib::IMdl_configuration>());
    if (!configuration.is_valid_interface())
    {
        *diagnostic = "the MDL SDK runtime exposes no IMdl_configuration";
        return OPENUSD_MDL_STATUS_SDK_UNAVAILABLE;
    }
    configuration->clear_mdl_paths();
    for (const std::string& path : searchPaths)
    {
        if (configuration->add_mdl_path(path.c_str()) != 0)
        {
            *diagnostic = "the MDL SDK rejected the module search path '" + path + "'";
            return OPENUSD_MDL_STATUS_INVALID_ARGUMENT;
        }
    }

    if (!state.started)
    {
        if (state.neuray->start(true) != 0)
        {
            *diagnostic = "the MDL SDK runtime failed to start";
            return OPENUSD_MDL_STATUS_SDK_UNAVAILABLE;
        }
        state.started = true;
        state.factory = mi::base::Handle<mi::neuraylib::IMdl_factory>(
            state.neuray->get_api_component<mi::neuraylib::IMdl_factory>());
        state.impexp = mi::base::Handle<mi::neuraylib::IMdl_impexp_api>(
            state.neuray->get_api_component<mi::neuraylib::IMdl_impexp_api>());
        state.database = mi::base::Handle<mi::neuraylib::IDatabase>(
            state.neuray->get_api_component<mi::neuraylib::IDatabase>());
        if (!state.factory.is_valid_interface() ||
            !state.impexp.is_valid_interface() ||
            !state.database.is_valid_interface())
        {
            *diagnostic = "the MDL SDK runtime exposes an incomplete API surface";
            return OPENUSD_MDL_STATUS_SDK_UNAVAILABLE;
        }
    }
    return OPENUSD_MDL_STATUS_OK;
}

uint32_t
ConfigureLocked(
    RuntimeState& state,
    const std::vector<std::string>& searchPaths,
    uint64_t generation,
    std::string* diagnostic)
{
    if (!EnsureRuntimeLoaded(state))
    {
        *diagnostic = state.failure;
        return OPENUSD_MDL_STATUS_SDK_UNAVAILABLE;
    }
    const bool changed = !state.configured || state.generation != generation ||
        state.searchPaths != searchPaths;
    if (!changed)
    {
        return OPENUSD_MDL_STATUS_OK;
    }
    const uint32_t status = ConfigureRuntime(state, searchPaths, diagnostic);
    if (status != OPENUSD_MDL_STATUS_OK)
    {
        return status;
    }
    for (auto entry = state.moduleScopes.begin(); entry != state.moduleScopes.end();)
    {
        if (state.database->remove_scope(entry->second->get_id()) != 0)
        {
            *diagnostic = "the MDL SDK could not invalidate module '" +
                entry->first.first + "'";
            return OPENUSD_MDL_STATUS_DISTILLATION_FAILED;
        }
        entry = state.moduleScopes.erase(entry);
    }
    state.database->garbage_collection();
    state.searchPaths = searchPaths;
    state.generation = generation;
    state.configured = true;
    return OPENUSD_MDL_STATUS_OK;
}

bool
CopySdkText(const char* text, std::string* out)
{
    size_t length = 0;
    if (text != nullptr)
    {
        while (length <= kMaxSdkTextBytes && text[length] != '\0')
        {
            ++length;
        }
    }
    if (length > kMaxSdkTextBytes)
    {
        return false;
    }
    out->assign(text == nullptr ? "" : text, length);
    return true;
}

void
AppendDiagnostic(std::string* diagnostic, const std::string& message)
{
    if (message.empty() || diagnostic->size() >= kMaxDiagnosticBytes)
    {
        return;
    }
    const std::string separator = diagnostic->empty() ? "" : "; ";
    if (diagnostic->size() + separator.size() + message.size() >
        kMaxDiagnosticBytes)
    {
        const std::string notice = "; further SDK diagnostics exceed the text limit";
        diagnostic->resize(
            std::min(diagnostic->size(), kMaxDiagnosticBytes - notice.size()));
        *diagnostic += notice;
        return;
    }
    *diagnostic += separator + message;
}

bool
PathContains(
    const std::filesystem::path& directory,
    const std::filesystem::path& file)
{
    auto part = file.begin();
    for (const auto& expected : directory)
    {
        if (part == file.end())
        {
            return false;
        }
#if defined(_WIN32)
        const std::wstring& left = expected.native();
        const std::wstring& right = part->native();
        if (CompareStringOrdinal(
                left.c_str(), static_cast<int>(left.size()),
                right.c_str(), static_cast<int>(right.size()), TRUE) != CSTR_EQUAL)
#else
        if (expected != *part)
#endif
        {
            return false;
        }
        ++part;
    }
    return true;
}

struct ModuleLocation
{
    std::string filename;
    std::string qualified;
    std::vector<std::string> searchPaths;
};

bool
LocateModule(
    RuntimeState& state,
    const std::string& moduleUri,
    ModuleLocation* location,
    std::string* diagnostic)
{
    if (moduleUri.empty() || moduleUri.find("://") != std::string::npos)
    {
        *diagnostic = "only modules inside the configured local search paths can be loaded";
        return false;
    }
    std::string fileUri = moduleUri;
    if (fileUri.rfind("::", 0) == 0)
    {
        if (ConfigureRuntime(state, state.searchPaths, diagnostic) != OPENUSD_MDL_STATUS_OK)
        {
            return false;
        }
        mi::base::Handle<mi::neuraylib::IMdl_configuration> configuration(
            state.neuray->get_api_component<mi::neuraylib::IMdl_configuration>());
        mi::base::Handle<mi::neuraylib::IMdl_entity_resolver> resolver(
            configuration->get_entity_resolver());
        mi::base::Handle<mi::neuraylib::IMdl_resolved_module> module(
            resolver->resolve_module(fileUri.c_str(), nullptr, nullptr, 0, 0));
        if (!module || !CopySdkText(module->get_filename(), &fileUri) || fileUri.empty())
        {
            *diagnostic = "the MDL SDK could not resolve module '" + moduleUri + "'";
            return false;
        }
    }
    else
    {
        std::replace(fileUri.begin(), fileUri.end(), '\\', '/');
    }
    const std::filesystem::path requested = std::filesystem::u8path(fileUri);
    for (size_t index = 0; index < state.searchPaths.size(); ++index)
    {
        std::error_code error;
        const std::filesystem::path root = std::filesystem::canonical(
            std::filesystem::u8path(state.searchPaths[index]), error);
        if (error)
        {
            *diagnostic = "cannot access module search path '" +
                state.searchPaths[index] + "': " + error.message();
            return false;
        }
        const std::filesystem::path candidate = std::filesystem::canonical(
            IsAbsolute(fileUri) ? requested : root / requested, error);
        if (error || !PathContains(root, candidate) ||
            !std::filesystem::is_regular_file(candidate, error) || error)
        {
            continue;
        }
        location->filename = candidate.u8string();
        if (location->filename.size() > kMaxSdkTextBytes)
        {
            *diagnostic = "the resolved module filename exceeds the text limit";
            return false;
        }
        // The root containing an explicitly requested file takes precedence.
        // No per-asset path is added, even for package/file names with spaces.
        location->searchPaths = state.searchPaths;
        std::rotate(
            location->searchPaths.begin(),
            location->searchPaths.begin() + static_cast<std::ptrdiff_t>(index),
            location->searchPaths.begin() + static_cast<std::ptrdiff_t>(index + 1));
        if (ConfigureRuntime(state, location->searchPaths, diagnostic) !=
            OPENUSD_MDL_STATUS_OK)
        {
            return false;
        }
        mi::base::Handle<const mi::IString> name(
            state.impexp->get_mdl_module_name(location->filename.c_str()));
        if (!name || !CopySdkText(name->get_c_str(), &location->qualified) ||
            location->qualified.empty())
        {
            *diagnostic = "the MDL SDK could not derive the module name for '" +
                location->filename + "'";
            return false;
        }
        return true;
    }
    *diagnostic = "module '" + moduleUri +
        "' is not a file inside any configured module search path";
    return false;
}

std::string
ContextMessages(mi::neuraylib::IMdl_execution_context* context)
{
    std::string text;
    if (context == nullptr)
    {
        return text;
    }
    const mi::Size count = context->get_messages_count();
    mi::Size index = 0;
    unsigned int reported = 0;
    for (; index < count && index < kMaxSdkParameters && reported < 8; ++index)
    {
        mi::base::Handle<const mi::neuraylib::IMessage> message(
            context->get_message(index));
        if (!message.is_valid_interface() ||
            message->get_severity() > mi::base::MESSAGE_SEVERITY_WARNING)
        {
            continue;
        }
        std::string messageText;
        if (!CopySdkText(message->get_string(), &messageText))
        {
            messageText = "an SDK diagnostic exceeds the text limit";
        }
        AppendDiagnostic(&text, messageText);
        ++reported;
    }
    if (index < count)
    {
        AppendDiagnostic(&text, "additional SDK messages exceed the bounded diagnostic list");
    }
    return text;
}

struct ReductionContext
{
    mi::neuraylib::ITransaction* transaction;
    mi::neuraylib::IMdl_factory* factory;
    mi::neuraylib::IMdl_entity_resolver* resolver;
    const mi::neuraylib::IFunction_definition* definition;
    const mi::neuraylib::IExpression_list* defaults;
    const std::map<std::string, SdkParameterValue>& authored;
    unsigned int remainingWork = kMaxExpressionWork;
    size_t remainingText = kMaxSdkTotalTextBytes;
    bool exhausted = false;
    std::string issue{};

    bool Step(unsigned int depth)
    {
        if (depth > kMaxExpressionDepth || remainingWork == 0)
        {
            exhausted = true;
            issue = "SDK expression depth/work limit exceeded (16 levels, 4096 nodes)";
            return false;
        }
        --remainingWork;
        return true;
    }

    bool Text(const char* text, std::string* out)
    {
        if (!CopySdkText(text, out) || out->size() > remainingText)
        {
            exhausted = true;
            issue = "SDK expression text limit exceeded";
            return false;
        }
        remainingText -= out->size();
        return true;
    }
};

bool
ResolveTexture(
    ReductionContext& context,
    const mi::neuraylib::IValue_texture* texture,
    SdkParameterValue* out)
{
    mi::base::Handle<const mi::neuraylib::IType_texture> type(texture->get_type());
    std::string path;
    std::string owner;
    std::string databaseName;
    std::string selector;
    if (!context.Text(texture->get_file_path(), &path) ||
        !context.Text(texture->get_owner_module(), &owner) ||
        !context.Text(texture->get_value(), &databaseName) ||
        !context.Text(texture->get_selector(), &selector))
    {
        return false;
    }
    if (type->get_shape() != mi::neuraylib::IType_texture::TS_2D || !selector.empty())
    {
        context.issue = "only unselected texture_2d resources are projected: '" + path + "'";
        return false;
    }
    out->kind = OPENUSD_MDL_VALUE_ASSET;
    if (path.empty() && databaseName.empty() && owner.empty())
    {
        // An SDK invalid/unset texture_2d() is absence. A nonempty unresolved
        // path, owner, or DB reference must never take this branch.
        return true;
    }

    const float gamma = texture->get_gamma();
    if (gamma == 0.0F)
    {
        out->colorSpace = "auto";
    }
    else if (gamma == 1.0F)
    {
        out->colorSpace = "raw";
    }
    else if (gamma == 2.2F)
    {
        out->colorSpace = "srgb";
    }
    else
    {
        context.issue = "texture gamma is outside the surface record: '" + path + "'";
        return false;
    }

    if (!path.empty())
    {
        mi::base::Handle<const mi::IString> moduleName(
            owner.empty() ? nullptr : context.factory->get_db_module_name(owner.c_str()));
        mi::base::Handle<const mi::neuraylib::IModule> module(
            context.transaction->access<mi::neuraylib::IModule>(
                moduleName ? moduleName->get_c_str() : context.definition->get_module()));
        std::string ownerFile;
        if (!module || !context.Text(module->get_filename(), &ownerFile))
        {
            context.issue = "texture owner module is unavailable: '" + owner + "'";
            return false;
        }
        mi::base::Handle<mi::neuraylib::IMdl_execution_context> messages(
            context.factory->create_execution_context());
        mi::base::Handle<mi::neuraylib::IMdl_resolved_resource> resource(
            context.resolver->resolve_resource(
                path.c_str(), ownerFile.c_str(),
                owner.empty() ? module->get_mdl_name() : owner.c_str(),
                0, 0, messages.get()));
        if (!resource || resource->has_sequence_marker() ||
            resource->get_uvtile_mode() != mi::neuraylib::UVTILE_MODE_NONE ||
            resource->get_count() != 1)
        {
            context.issue = "unresolved or non-single-file texture '" + path +
                "' in '" + owner + "'";
            AppendDiagnostic(&context.issue, ContextMessages(messages.get()));
            return false;
        }
        mi::base::Handle<const mi::neuraylib::IMdl_resolved_resource_element> element(
            resource->get_element(0));
        if (!element || element->get_count() != 1 ||
            !context.Text(element->get_filename(0), &out->text))
        {
            context.issue = "texture has no single filesystem asset: '" + path + "'";
            return false;
        }
    }
    else if (!databaseName.empty())
    {
        mi::base::Handle<const mi::neuraylib::ITexture> resolved(
            context.transaction->access<mi::neuraylib::ITexture>(databaseName.c_str()));
        mi::base::Handle<const mi::neuraylib::IImage> image(
            resolved ? context.transaction->access<mi::neuraylib::IImage>(
                           resolved->get_image()) : nullptr);
        if (!image || !context.Text(image->get_filename(0, 0), &out->text))
        {
            context.issue = "texture DB reference has no filesystem asset: '" + databaseName + "'";
            return false;
        }
    }
    std::error_code error;
    if (!IsAbsolute(out->text) ||
        !std::filesystem::is_regular_file(std::filesystem::u8path(out->text), error) ||
        error)
    {
        context.issue = "texture is not a resolved regular file: '" + path + "'";
        return false;
    }
    return true;
}

/// Reduces one MDL value to the plain form the adapter's C ABI carries. Returns
/// false for a value kind this adapter does not distil, so the parameter is
/// reported by name instead of being narrowed into a different type.
bool
ReduceValue(
    ReductionContext& context,
    const mi::neuraylib::IValue* value,
    unsigned int depth,
    SdkParameterValue* out)
{
    if (value == nullptr || !context.Step(depth))
    {
        return false;
    }
    switch (value->get_kind())
    {
        case mi::neuraylib::IValue::VK_BOOL:
        {
            mi::base::Handle<const mi::neuraylib::IValue_bool> typed(
                value->get_interface<mi::neuraylib::IValue_bool>());
            out->kind = OPENUSD_MDL_VALUE_BOOL;
            out->componentCount = 1;
            out->integerValue = typed->get_value() ? 1 : 0;
            return true;
        }
        case mi::neuraylib::IValue::VK_INT:
        {
            mi::base::Handle<const mi::neuraylib::IValue_int> typed(
                value->get_interface<mi::neuraylib::IValue_int>());
            out->kind = OPENUSD_MDL_VALUE_INT;
            out->componentCount = 1;
            out->integerValue = typed->get_value();
            return true;
        }
        case mi::neuraylib::IValue::VK_FLOAT:
        {
            mi::base::Handle<const mi::neuraylib::IValue_float> typed(
                value->get_interface<mi::neuraylib::IValue_float>());
            out->kind = OPENUSD_MDL_VALUE_FLOAT;
            out->componentCount = 1;
            out->value[0] = typed->get_value();
            return true;
        }
        case mi::neuraylib::IValue::VK_DOUBLE:
        {
            mi::base::Handle<const mi::neuraylib::IValue_double> typed(
                value->get_interface<mi::neuraylib::IValue_double>());
            out->kind = OPENUSD_MDL_VALUE_FLOAT;
            out->componentCount = 1;
            out->value[0] = static_cast<float>(typed->get_value());
            return true;
        }
        case mi::neuraylib::IValue::VK_COLOR:
        case mi::neuraylib::IValue::VK_VECTOR:
        {
            mi::base::Handle<const mi::neuraylib::IValue_compound> typed(
                value->get_interface<mi::neuraylib::IValue_compound>());
            const mi::Size size = typed->get_size();
            if (size == 0 || size > 4)
            {
                return false;
            }
            for (mi::Size index = 0; index < size; ++index)
            {
                mi::base::Handle<const mi::neuraylib::IValue> component(
                    typed->get_value(index));
                SdkParameterValue scalar;
                if (!ReduceValue(context, component.get(), depth + 1, &scalar) ||
                    scalar.componentCount != 1 ||
                    (scalar.kind != OPENUSD_MDL_VALUE_FLOAT &&
                     scalar.kind != OPENUSD_MDL_VALUE_INT))
                {
                    return false;
                }
                out->value[index] = scalar.kind == OPENUSD_MDL_VALUE_INT
                    ? static_cast<float>(scalar.integerValue)
                    : scalar.value[0];
            }
            out->componentCount = static_cast<uint32_t>(size);
            out->kind = size == 2 ? OPENUSD_MDL_VALUE_FLOAT2
                : size == 3      ? OPENUSD_MDL_VALUE_FLOAT3
                                 : OPENUSD_MDL_VALUE_FLOAT4;
            return true;
        }
        case mi::neuraylib::IValue::VK_STRING:
        {
            mi::base::Handle<const mi::neuraylib::IValue_string> typed(
                value->get_interface<mi::neuraylib::IValue_string>());
            out->kind = OPENUSD_MDL_VALUE_STRING;
            return context.Text(typed->get_value(), &out->text);
        }
        case mi::neuraylib::IValue::VK_TEXTURE:
        {
            mi::base::Handle<const mi::neuraylib::IValue_texture> typed(
                value->get_interface<mi::neuraylib::IValue_texture>());
            return ResolveTexture(context, typed.get(), out);
        }
        default:
            context.issue = "SDK value kind " + std::to_string(value->get_kind()) +
                " is outside the surface parameter subset";
            return false;
    }
}

bool
ReduceExpression(
    ReductionContext& context,
    const mi::neuraylib::IExpression* expression,
    unsigned int depth,
    SdkParameterValue* out);

bool
ReduceConstructor(
    ReductionContext& context,
    const mi::neuraylib::IExpression* expression,
    const char* definitionName,
    const mi::neuraylib::IExpression_list* arguments,
    unsigned int depth,
    SdkParameterValue* out)
{
    std::string name;
    if (!context.Text(definitionName, &name))
    {
        return false;
    }
    mi::base::Handle<const mi::neuraylib::IFunction_definition> definition(
        context.transaction->access<mi::neuraylib::IFunction_definition>(name.c_str()));
    if (!definition || arguments == nullptr)
    {
        context.issue = "SDK call definition is unavailable: '" + name + "'";
        return false;
    }
    const auto semantic = definition->get_semantic();
    if (semantic != mi::neuraylib::IFunction_definition::DS_ELEM_CONSTRUCTOR &&
        semantic != mi::neuraylib::IFunction_definition::DS_CONV_CONSTRUCTOR &&
        semantic != mi::neuraylib::IFunction_definition::DS_COPY_CONSTRUCTOR)
    {
        context.issue = "unsupported SDK call '" + name + "'";
        return false;
    }
    const mi::Size count = arguments->get_size();
    if (count == 0 || count > 4)
    {
        context.issue = "unsupported constructor arity: '" + name + "'";
        return false;
    }
    mi::base::Handle<const mi::neuraylib::IType> type(expression->get_type());
    type = mi::base::Handle<const mi::neuraylib::IType>(type->skip_all_type_aliases());
    const auto kind = type->get_kind();
    mi::Size arity = 1;
    if (kind == mi::neuraylib::IType::TK_COLOR)
    {
        arity = 3;
    }
    else if (kind == mi::neuraylib::IType::TK_VECTOR)
    {
        mi::base::Handle<const mi::neuraylib::IType_vector> vector(
            type->get_interface<mi::neuraylib::IType_vector>());
        mi::base::Handle<const mi::neuraylib::IType_atomic> element(vector->get_element_type());
        if (element->get_kind() != mi::neuraylib::IType::TK_FLOAT)
        {
            context.issue = "unsupported vector element type: '" + name + "'";
            return false;
        }
        arity = vector->get_size();
    }
    else if (kind != mi::neuraylib::IType::TK_FLOAT &&
        kind != mi::neuraylib::IType::TK_DOUBLE &&
        kind != mi::neuraylib::IType::TK_INT &&
        kind != mi::neuraylib::IType::TK_BOOL &&
        kind != mi::neuraylib::IType::TK_TEXTURE &&
        kind != mi::neuraylib::IType::TK_STRING)
    {
        context.issue = "unsupported constructor type: '" + name + "'";
        return false;
    }
    if (arity == 0 || arity > 4 || (count != 1 && count != arity))
    {
        context.issue = "unsupported constructor shape: '" + name + "'";
        return false;
    }
    double components[4]{};
    bool authored = false;
    for (mi::Size index = 0; index < count; ++index)
    {
        mi::base::Handle<const mi::neuraylib::IExpression> argument(
            arguments->get_expression(index));
        SdkParameterValue reduced;
        if (!ReduceExpression(context, argument.get(), depth + 1, &reduced))
        {
            return false;
        }
        if ((kind == mi::neuraylib::IType::TK_TEXTURE &&
             reduced.kind == OPENUSD_MDL_VALUE_ASSET) ||
            (kind == mi::neuraylib::IType::TK_STRING &&
             reduced.kind == OPENUSD_MDL_VALUE_STRING))
        {
            if (count != 1 || semantic != mi::neuraylib::IFunction_definition::DS_COPY_CONSTRUCTOR)
            {
                context.issue = "only resource/string copy constructors are projected: '" + name + "'";
                return false;
            }
            *out = std::move(reduced);
            return true;
        }
        if (reduced.componentCount == 0 ||
            (reduced.kind != OPENUSD_MDL_VALUE_BOOL &&
             reduced.kind != OPENUSD_MDL_VALUE_INT &&
             reduced.kind != OPENUSD_MDL_VALUE_FLOAT &&
             reduced.kind != OPENUSD_MDL_VALUE_FLOAT2 &&
             reduced.kind != OPENUSD_MDL_VALUE_FLOAT3 &&
             reduced.kind != OPENUSD_MDL_VALUE_FLOAT4))
        {
            context.issue = "constructor operand is not a numeric value: '" + name + "'";
            return false;
        }
        if (reduced.componentCount != 1)
        {
            if (count != 1 || reduced.componentCount != arity)
            {
                context.issue = "constructor operand has an unsupported shape: '" + name + "'";
                return false;
            }
            std::copy_n(reduced.value, arity, components);
        }
        else
        {
            components[index] = reduced.kind == OPENUSD_MDL_VALUE_BOOL
                ? (reduced.integerValue != 0 ? 1.0 : 0.0)
                : reduced.kind == OPENUSD_MDL_VALUE_INT
                    ? static_cast<double>(reduced.integerValue) : reduced.value[0];
            if (count == 1)
            {
                std::fill_n(components, arity, components[0]);
            }
        }
        authored = authored || reduced.origin == OPENUSD_MDL_ORIGIN_AUTHORED;
        for (const std::string& source : reduced.authoredSources)
        {
            if (std::find(out->authoredSources.begin(), out->authoredSources.end(), source) ==
                out->authoredSources.end())
            {
                out->authoredSources.push_back(source);
            }
        }
    }
    out->componentCount = static_cast<uint32_t>(arity);
    out->origin = authored ? OPENUSD_MDL_ORIGIN_AUTHORED : OPENUSD_MDL_ORIGIN_MODULE_EXPRESSION;
    if (kind == mi::neuraylib::IType::TK_BOOL)
    {
        out->kind = OPENUSD_MDL_VALUE_BOOL;
        out->integerValue = components[0] != 0.0F ? 1 : 0;
    }
    else if (kind == mi::neuraylib::IType::TK_INT)
    {
        const double value = components[0];
        if (!std::isfinite(value) || value < INT32_MIN || value > INT32_MAX)
        {
            context.issue = "integer constructor is out of range: '" + name + "'";
            return false;
        }
        out->kind = OPENUSD_MDL_VALUE_INT;
        out->integerValue = static_cast<int32_t>(value);
    }
    else
    {
        out->kind = arity == 1 ? OPENUSD_MDL_VALUE_FLOAT :
            arity == 2 ? OPENUSD_MDL_VALUE_FLOAT2 :
            arity == 3 ? OPENUSD_MDL_VALUE_FLOAT3 : OPENUSD_MDL_VALUE_FLOAT4;
        for (mi::Size index = 0; index < arity; ++index)
        {
            out->value[index] = static_cast<float>(components[index]);
        }
    }
    return true;
}

bool
ReduceExpression(
    ReductionContext& context,
    const mi::neuraylib::IExpression* expression,
    unsigned int depth,
    SdkParameterValue* out)
{
    if (expression == nullptr || !context.Step(depth))
    {
        return false;
    }
    switch (expression->get_kind())
    {
        case mi::neuraylib::IExpression::EK_CONSTANT:
        {
            mi::base::Handle<const mi::neuraylib::IExpression_constant> constant(
                expression->get_interface<mi::neuraylib::IExpression_constant>());
            mi::base::Handle<const mi::neuraylib::IValue> value(constant->get_value());
            return ReduceValue(context, value.get(), depth, out);
        }
        case mi::neuraylib::IExpression::EK_PARAMETER:
        {
            mi::base::Handle<const mi::neuraylib::IExpression_parameter> parameter(
                expression->get_interface<mi::neuraylib::IExpression_parameter>());
            const mi::Size index = parameter->get_index();
            std::string name;
            if (index >= context.definition->get_parameter_count() ||
                !context.Text(context.definition->get_parameter_name(index), &name))
            {
                context.issue = "SDK parameter reference is outside the formal parameter list";
                return false;
            }
            const auto authored = context.authored.find(name);
            if (authored != context.authored.end())
            {
                *out = authored->second;
                out->authoredSources = {name};
                return true;
            }
            // Default-list indices are not formal parameter indices: parameters
            // without defaults are omitted from that list by the SDK.
            mi::base::Handle<const mi::neuraylib::IExpression> target(
                context.defaults ? context.defaults->get_expression(name.c_str()) : nullptr);
            if (!target)
            {
                context.issue = "no authored value or default for parameter '" + name + "'";
                return false;
            }
            return ReduceExpression(context, target.get(), depth + 1, out);
        }
        case mi::neuraylib::IExpression::EK_DIRECT_CALL:
        {
            mi::base::Handle<const mi::neuraylib::IExpression_direct_call> call(
                expression->get_interface<mi::neuraylib::IExpression_direct_call>());
            mi::base::Handle<const mi::neuraylib::IExpression_list> arguments(
                call->get_arguments());
            return ReduceConstructor(
                context, expression, call->get_definition(), arguments.get(), depth, out);
        }
        case mi::neuraylib::IExpression::EK_CALL:
        {
            mi::base::Handle<const mi::neuraylib::IExpression_call> expressionCall(
                expression->get_interface<mi::neuraylib::IExpression_call>());
            std::string name;
            if (!context.Text(expressionCall->get_call(), &name))
            {
                return false;
            }
            mi::base::Handle<const mi::neuraylib::IFunction_call> call(
                context.transaction->access<mi::neuraylib::IFunction_call>(name.c_str()));
            if (!call)
            {
                context.issue = "SDK indirect call is unavailable: '" + name + "'";
                return false;
            }
            mi::base::Handle<const mi::neuraylib::IExpression_list> arguments(call->get_arguments());
            return ReduceConstructor(
                context, expression, call->get_function_definition(), arguments.get(), depth, out);
        }
        case mi::neuraylib::IExpression::EK_TEMPORARY:
        {
            mi::base::Handle<const mi::neuraylib::IExpression_temporary> temporary(
                expression->get_interface<mi::neuraylib::IExpression_temporary>());
            if (temporary->get_index() >= context.definition->get_temporary_count() ||
                context.definition->get_temporary_count() > kMaxSdkParameters)
            {
                context.issue = "SDK temporary is outside the bounded definition";
                return false;
            }
            mi::base::Handle<const mi::neuraylib::IExpression> target(
                context.definition->get_temporary(temporary->get_index()));
            return ReduceExpression(context, target.get(), depth + 1, out);
        }
        default:
            context.issue = "unsupported SDK expression kind";
            return false;
    }
}

SdkMaterialKind
KnownRoot(const mi::neuraylib::IFunction_definition* definition)
{
    const char* module = definition->get_mdl_module_name();
    const char* name = definition->get_mdl_simple_name();
    if (module != nullptr && name != nullptr)
    {
        if (std::strcmp(module, "::OmniPBR") == 0 && std::strcmp(name, "OmniPBR") == 0)
        {
            return SdkMaterialKind::OmniPbr;
        }
        if (std::strcmp(module, "::OmniGlass") == 0 && std::strcmp(name, "OmniGlass") == 0)
        {
            return SdkMaterialKind::OmniGlass;
        }
    }
    return SdkMaterialKind::Unspecified;
}

bool
ResolveVariantRoot(
    mi::neuraylib::ITransaction* transaction,
    const mi::neuraylib::IFunction_definition* definition,
    SdkMaterialResolution* resolution)
{
    resolution->kind = KnownRoot(definition);
    resolution->isVariant = definition->get_prototype() != nullptr;
    if (resolution->kind != SdkMaterialKind::Unspecified || !resolution->isVariant)
    {
        return true;
    }
    mi::base::Handle<const mi::neuraylib::IFunction_definition> owner;
    std::set<std::string> visited;
    for (unsigned int depth = 0; depth < kMaxPrototypeDepth; ++depth)
    {
        std::string prototype;
        if (!CopySdkText(definition->get_prototype(), &prototype) ||
            prototype.empty() || !visited.insert(prototype).second)
        {
            std::string name;
            if (!CopySdkText(definition->get_mdl_simple_name(), &name))
            {
                name = "(SDK name exceeds the text limit)";
            }
            resolution->unresolved.push_back("body:" + name);
            resolution->diagnostic = "unsupported variant root '" + name +
                "'; only SDK-proven OmniPBR/OmniGlass variant interfaces are admitted";
            return false;
        }
        mi::base::Handle<const mi::neuraylib::IFunction_definition> target(
            transaction->access<mi::neuraylib::IFunction_definition>(prototype.c_str()));
        if (!target || !target->is_material() ||
            target->get_parameter_count() > kMaxSdkParameters ||
            definition->get_parameter_count() != target->get_parameter_count())
        {
            resolution->unresolved.push_back("prototype:" + prototype);
            resolution->diagnostic = "unsupported SDK variant parameter interface: '" + prototype + "'";
            return false;
        }
        // (*) variants retain the prototype's formal interface. The SDK moves
        // named overrides into these defaults, then inlines the BSDF body.
        // Validate that relationship, never reverse-engineer the inlined BSDF.
        for (mi::Size index = 0; index < target->get_parameter_count(); ++index)
        {
            std::string name;
            std::string targetName;
            std::string type;
            std::string targetType;
            if (!CopySdkText(definition->get_parameter_name(index), &name) ||
                !CopySdkText(target->get_parameter_name(index), &targetName) ||
                !CopySdkText(definition->get_mdl_parameter_type_name(index), &type) ||
                !CopySdkText(target->get_mdl_parameter_type_name(index), &targetType) ||
                name != targetName || type != targetType)
            {
                resolution->unresolved.push_back("prototype:" + prototype);
                resolution->diagnostic = "variant formal names/types do not match '" + prototype + "'";
                return false;
            }
        }
        resolution->kind = KnownRoot(target.get());
        if (resolution->kind != SdkMaterialKind::Unspecified)
        {
            return true;
        }
        owner = std::move(target);
        definition = owner.get();
    }
    resolution->unresolved.emplace_back("prototype:depth");
    resolution->diagnostic = "SDK variant prototype depth exceeds 8 retained links";
    return false;
}

SdkMaterialResolution
ResolveInTransaction(
    RuntimeState& state,
    mi::neuraylib::ITransaction* transaction,
    const ModuleLocation& location,
    const std::string& materialName,
    const std::map<std::string, SdkParameterValue>& authored)
{
    SdkMaterialResolution resolution;
    mi::base::Handle<mi::neuraylib::IMdl_execution_context> context(
        state.factory->create_execution_context());
    // The surface record needs resource identities, not decoded image data.
    // Preserve SDK resource paths/owners and resolve them with its resolver.
    if (context->set_option("resolve_resources", false) != 0)
    {
        resolution.status = OPENUSD_MDL_STATUS_SDK_UNAVAILABLE;
        resolution.diagnostic = "the MDL SDK cannot preserve unresolved resource identities";
        return resolution;
    }
    const mi::Sint32 loaded =
        state.impexp->load_module(transaction, location.qualified.c_str(), context.get());
    resolution.diagnostic = ContextMessages(context.get());
    if (loaded < 0)
    {
        resolution.status = OPENUSD_MDL_STATUS_MODULE_COMPILE_FAILED;
        resolution.diagnostic = "MDL module '" + location.filename + "' did not compile (" +
            std::to_string(loaded) + "): " + resolution.diagnostic;
        return resolution;
    }
    mi::base::Handle<const mi::IString> moduleName(
        state.factory->get_db_module_name(location.qualified.c_str()));
    mi::base::Handle<const mi::neuraylib::IModule> module(
        moduleName ? transaction->access<mi::neuraylib::IModule>(moduleName->get_c_str()) : nullptr);
    std::string filename;
    std::error_code error;
    if (!module || !CopySdkText(module->get_filename(), &filename) ||
        !std::filesystem::equivalent(
            std::filesystem::u8path(filename), std::filesystem::u8path(location.filename), error) ||
        error)
    {
        resolution.status = OPENUSD_MDL_STATUS_MODULE_COMPILE_FAILED;
        resolution.diagnostic = "the SDK module identity does not match requested file '" +
            location.filename + "'";
        return resolution;
    }
    if (module->get_material_count() > kMaxSdkParameters)
    {
        resolution.status = OPENUSD_MDL_STATUS_EXPRESSION_UNSUPPORTED;
        resolution.diagnostic = "module '" + location.filename +
            "' exceeds the 256 material-definition limit";
        return resolution;
    }
    const std::string requestedName = materialName.empty()
        ? std::filesystem::u8path(location.filename).stem().u8string() : materialName;
    std::string definitionName;
    for (mi::Size index = 0; index < module->get_material_count(); ++index)
    {
        std::string candidate;
        if (!CopySdkText(module->get_material(index), &candidate))
        {
            resolution.status = OPENUSD_MDL_STATUS_EXPRESSION_UNSUPPORTED;
            resolution.diagnostic = "an SDK material-definition name exceeds the text limit";
            return resolution;
        }
        mi::base::Handle<const mi::neuraylib::IFunction_definition> definition(
            transaction->access<mi::neuraylib::IFunction_definition>(candidate.c_str()));
        std::string name;
        if (!definition || !CopySdkText(definition->get_mdl_simple_name(), &name) ||
            name != requestedName)
        {
            continue;
        }
        if (!definitionName.empty())
        {
            resolution.status = OPENUSD_MDL_STATUS_UNSUPPORTED_MATERIAL;
            resolution.diagnostic = "material name '" + requestedName + "' is ambiguous";
            return resolution;
        }
        definitionName = std::move(candidate);
    }
    if (definitionName.empty())
    {
        resolution.status = OPENUSD_MDL_STATUS_UNSUPPORTED_MATERIAL;
        resolution.diagnostic = "MDL module '" + location.filename +
            "' declares no material named '" + requestedName + "'";
        return resolution;
    }
    mi::base::Handle<const mi::neuraylib::IFunction_definition> definition(
        transaction->access<mi::neuraylib::IFunction_definition>(definitionName.c_str()));
    if (definition->get_parameter_count() > kMaxSdkParameters)
    {
        resolution.status = OPENUSD_MDL_STATUS_EXPRESSION_UNSUPPORTED;
        resolution.diagnostic = "material '" + requestedName + "' exceeds the 256 formal-parameter limit";
        return resolution;
    }
    if (!ResolveVariantRoot(transaction, definition.get(), &resolution))
    {
        resolution.status = OPENUSD_MDL_STATUS_EXPRESSION_UNSUPPORTED;
        return resolution;
    }
    mi::base::Handle<const mi::neuraylib::IExpression_list> defaults(definition->get_defaults());
    mi::base::Handle<mi::neuraylib::IMdl_configuration> configuration(
        state.neuray->get_api_component<mi::neuraylib::IMdl_configuration>());
    mi::base::Handle<mi::neuraylib::IMdl_entity_resolver> resolver(
        configuration->get_entity_resolver());
    ReductionContext reduction{
        transaction, state.factory.get(), resolver.get(), definition.get(), defaults.get(), authored};
    for (mi::Size index = 0; index < definition->get_parameter_count(); ++index)
    {
        std::string name;
        if (!reduction.Text(definition->get_parameter_name(index), &name))
        {
            break;
        }
        SdkParameterValue reduced;
        const auto override = authored.find(name);
        if (override != authored.end())
        {
            reduced = override->second;
        }
        else
        {
            mi::base::Handle<const mi::neuraylib::IExpression> expression(
                defaults ? defaults->get_expression(name.c_str()) : nullptr);
            if (!expression)
            {
                continue;
            }
            reduction.issue.clear();
            if (!ReduceExpression(reduction, expression.get(), 0, &reduced))
            {
                resolution.unresolved.push_back(name);
                AppendDiagnostic(
                    &resolution.diagnostic, "parameter '" + name + "': " +
                        (reduction.issue.empty() ? "unsupported SDK expression" : reduction.issue));
                if (reduction.exhausted)
                {
                    break;
                }
                continue;
            }
        }
        if (!reduced.colorSpace.empty() && !reduced.text.empty())
        {
            SdkParameterValue metadata;
            metadata.kind = OPENUSD_MDL_VALUE_STRING;
            metadata.text = reduced.colorSpace;
            metadata.origin = reduced.origin;
            resolution.defaults.emplace("colorSpace:" + name, std::move(metadata));
        }
        resolution.defaults.emplace(std::move(name), std::move(reduced));
    }
    if (reduction.exhausted)
    {
        resolution.defaults.clear();
        resolution.status = OPENUSD_MDL_STATUS_EXPRESSION_UNSUPPORTED;
        AppendDiagnostic(&resolution.diagnostic, reduction.issue);
        return resolution;
    }
    if (resolution.kind == SdkMaterialKind::Unspecified)
    {
        // Preserve the pre-existing direct parameter-default API, but do not
        // misrepresent it as a proven wrapper or a clean BSDF projection.
        resolution.unresolved.push_back("body:" + requestedName);
        AppendDiagnostic(
            &resolution.diagnostic, "material '" + requestedName +
                "' has no admitted SDK variant root; parameter-default projection only, body unsupported");
    }
    if (resolution.defaults.empty())
    {
        resolution.status = OPENUSD_MDL_STATUS_EXPRESSION_UNSUPPORTED;
        AppendDiagnostic(
            &resolution.diagnostic, "MDL material '" + requestedName +
                "' declares no parameter value inside the projection subset");
    }
    return resolution;
}
#endif
}

SdkBackend&
SdkBackend::Instance()
{
    static SdkBackend backend;
    return backend;
}

bool
SdkBackend::IsCompiledIn()
{
#if defined(OPENUSD_MDL_WITH_SDK)
    return true;
#else
    return false;
#endif
}

#if !defined(OPENUSD_MDL_WITH_SDK)

uint32_t
SdkBackend::Configure(
    const std::vector<std::string>& searchPaths,
    uint64_t generation,
    std::string* diagnostic)
{
    (void)searchPaths;
    (void)generation;
    if (diagnostic != nullptr)
    {
        *diagnostic = "this adapter was built without an MDL SDK";
    }
    return OPENUSD_MDL_STATUS_SDK_UNAVAILABLE;
}

SdkMaterialResolution
SdkBackend::ResolveMaterial(
    const std::string& moduleUri,
    const std::string& materialName,
    const std::vector<std::string>& searchPaths,
    uint64_t generation,
    const std::map<std::string, SdkParameterValue>& authored)
{
    (void)moduleUri;
    (void)materialName;
    (void)searchPaths;
    (void)generation;
    (void)authored;
    SdkMaterialResolution resolution;
    resolution.status = OPENUSD_MDL_STATUS_SDK_UNAVAILABLE;
    resolution.diagnostic = "this adapter was built without an MDL SDK";
    return resolution;
}

std::string
SdkBackend::Describe()
{
    return "no MDL SDK backend is compiled in";
}

bool
SdkBackend::IsAvailable()
{
    return false;
}

#else

uint32_t
SdkBackend::Configure(
    const std::vector<std::string>& searchPaths,
    uint64_t generation,
    std::string* diagnostic)
{
    std::string ignored;
    std::string& message = diagnostic == nullptr ? ignored : *diagnostic;
    std::lock_guard<std::mutex> guard(BackendLock());
    return ConfigureLocked(State(), searchPaths, generation, &message);
}

SdkMaterialResolution
SdkBackend::ResolveMaterial(
    const std::string& moduleUri,
    const std::string& materialName,
    const std::vector<std::string>& searchPaths,
    uint64_t generation,
    const std::map<std::string, SdkParameterValue>& authored)
{
    SdkMaterialResolution resolution;
    std::lock_guard<std::mutex> guard(BackendLock());
    RuntimeState& state = State();
    resolution.status = ConfigureLocked(state, searchPaths, generation, &resolution.diagnostic);
    if (resolution.status != OPENUSD_MDL_STATUS_OK)
    {
        return resolution;
    }
    if (state.searchPaths.empty())
    {
        resolution.status = OPENUSD_MDL_STATUS_MODULE_NOT_FOUND;
        resolution.diagnostic =
            "no MDL module search path is configured, so module '" + moduleUri +
            "' cannot be resolved; supply the module directory through the "
            "adapter configuration";
        return resolution;
    }

    ModuleLocation location;
    if (!LocateModule(state, moduleUri, &location, &resolution.diagnostic))
    {
        resolution.status = OPENUSD_MDL_STATUS_MODULE_NOT_FOUND;
        return resolution;
    }
    const auto key = std::make_pair(location.filename, materialName);
    auto cached = state.moduleScopes.find(key);
    if (cached == state.moduleScopes.end())
    {
        if (state.moduleScopes.size() >= kMaxModuleScopes)
        {
            const auto first = state.moduleScopes.begin();
            if (state.database->remove_scope(first->second->get_id()) != 0)
            {
                resolution.status = OPENUSD_MDL_STATUS_DISTILLATION_FAILED;
                resolution.diagnostic = "the SDK module-scope cache could not evict '" +
                    first->first.first + "'";
                return resolution;
            }
            state.moduleScopes.erase(first);
            state.database->garbage_collection();
        }
        mi::base::Handle<mi::neuraylib::IScope> scope(state.database->create_scope(nullptr));
        if (!scope)
        {
            resolution.status = OPENUSD_MDL_STATUS_DISTILLATION_FAILED;
            resolution.diagnostic = "the SDK could not create an isolated module scope";
            return resolution;
        }
        cached = state.moduleScopes.emplace(key, std::move(scope)).first;
    }
    mi::base::Handle<mi::neuraylib::ITransaction> transaction(cached->second->create_transaction());
    if (!transaction)
    {
        resolution.status = OPENUSD_MDL_STATUS_DISTILLATION_FAILED;
        resolution.diagnostic = "the SDK could not create a module transaction";
        return resolution;
    }
    // The helper releases every DB element before commit/abort; only plain
    // values survive into a result owned by the public adapter instance.
    resolution = ResolveInTransaction(state, transaction.get(), location, materialName, authored);
    if (resolution.status == OPENUSD_MDL_STATUS_OK)
    {
        if (transaction->commit() != 0)
        {
            resolution.defaults.clear();
            resolution.status = OPENUSD_MDL_STATUS_DISTILLATION_FAILED;
            resolution.diagnostic = "the SDK could not commit material '" + materialName + "'";
        }
    }
    else
    {
        transaction->abort();
    }
    return resolution;
}

std::string
SdkBackend::Describe()
{
    std::lock_guard<std::mutex> guard(BackendLock());
    RuntimeState& state = State();
    if (state.description.empty())
    {
        return state.failure.empty() ? "no MDL SDK runtime is loaded" : state.failure;
    }
    return state.description;
}

bool
SdkBackend::IsAvailable()
{
    std::lock_guard<std::mutex> guard(BackendLock());
    return State().started;
}

#endif
}
