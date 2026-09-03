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
// compile it, find the named material, and reduce that material's parameter
// defaults to plain values. What it does not do: evaluate layered BSDFs,
// generate shader code, or fold a call it does not recognise. Each of those is
// reported by parameter name instead.

#include "sdk_backend.h"

#include "openusd_mdl.h"

#include <mutex>

#if defined(OPENUSD_MDL_WITH_SDK)

#include <mi/base/handle.h>
#include <mi/neuraylib/factory.h>
#include <mi/neuraylib/idatabase.h>
#include <mi/neuraylib/ifunction_definition.h>
#include <mi/neuraylib/iimage.h>
#include <mi/neuraylib/imdl_configuration.h>
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

/// Reduces an MDL module reference to the qualified module name the SDK loads.
/// `OmniPBR.mdl` becomes `::OmniPBR`; a search-path relative `Base/Thing.mdl`
/// becomes `::Base::Thing`. Any leading directory that is not part of the search
/// path is the caller's problem to configure, which is why unresolved modules
/// are reported as MODULE_NOT_FOUND rather than guessed at.
std::string
ToQualifiedModuleName(const std::string& moduleUri)
{
    std::string name = moduleUri;
    const size_t scheme = name.find("://");
    if (scheme != std::string::npos)
    {
        name = name.substr(scheme + 3);
        const size_t host = name.find('/');
        name = host == std::string::npos ? std::string() : name.substr(host + 1);
    }
    if (name.size() > 4 &&
        name.compare(name.size() - 4, 4, ".mdl") == 0)
    {
        name.resize(name.size() - 4);
    }
    std::string qualified;
    std::string segment;
    for (const char character : name)
    {
        if (character == '/' || character == '\\')
        {
            if (!segment.empty())
            {
                qualified += "::" + segment;
                segment.clear();
            }
            continue;
        }
        segment.push_back(character);
    }
    if (!segment.empty())
    {
        qualified += "::" + segment;
    }
    return qualified.empty() ? std::string("::") + name : qualified;
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
    for (mi::Size index = 0; index < count && index < 8; ++index)
    {
        mi::base::Handle<const mi::neuraylib::IMessage> message(
            context->get_message(index));
        if (!message.is_valid_interface() || message->get_string() == nullptr)
        {
            continue;
        }
        if (!text.empty())
        {
            text += "; ";
        }
        text += message->get_string();
    }
    return text;
}

/// Resolves the filesystem path of a texture the SDK already resolved. The
/// image's own filename is preferred because it is what a renderer can open;
/// the MDL file path is the fallback for a resource the SDK knows of but did not
/// materialise on disk.
std::string
ResolveTexturePath(
    mi::neuraylib::ITransaction* transaction,
    const mi::neuraylib::IValue_texture* texture)
{
    if (texture == nullptr)
    {
        return std::string();
    }
    const char* dbName = texture->get_value();
    if (dbName != nullptr && transaction != nullptr)
    {
        mi::base::Handle<const mi::neuraylib::ITexture> resolved(
            transaction->access<mi::neuraylib::ITexture>(dbName));
        if (resolved.is_valid_interface())
        {
            mi::base::Handle<const mi::neuraylib::IImage> image(
                transaction->access<mi::neuraylib::IImage>(resolved->get_image()));
            if (image.is_valid_interface())
            {
                const char* fileName = image->get_filename(0, 0);
                if (fileName != nullptr && fileName[0] != '\0')
                {
                    return std::string(fileName);
                }
            }
        }
    }
    const char* filePath = texture->get_file_path();
    return filePath == nullptr ? std::string() : std::string(filePath);
}

/// Reduces one MDL value to the plain form the adapter's C ABI carries. Returns
/// false for a value kind this adapter does not distil, so the parameter is
/// reported by name instead of being narrowed into a different type.
bool
ReduceValue(
    mi::neuraylib::ITransaction* transaction,
    const mi::neuraylib::IValue* value,
    SdkParameterValue* out)
{
    if (value == nullptr)
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
                if (!ReduceValue(transaction, component.get(), &scalar) ||
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
            const char* text = typed->get_value();
            out->kind = OPENUSD_MDL_VALUE_STRING;
            out->text = text == nullptr ? std::string() : std::string(text);
            return true;
        }
        case mi::neuraylib::IValue::VK_TEXTURE:
        {
            mi::base::Handle<const mi::neuraylib::IValue_texture> typed(
                value->get_interface<mi::neuraylib::IValue_texture>());
            const std::string path = ResolveTexturePath(transaction, typed.get());
            if (path.empty())
            {
                // The module names a texture the SDK could not resolve to
                // anything a renderer can open. Reporting it is the only honest
                // outcome; publishing an unresolvable path would fail later, far
                // from the cause.
                return false;
            }
            out->kind = OPENUSD_MDL_VALUE_ASSET;
            out->text = path;
            return true;
        }
        default:
            return false;
    }
}

/// Folds one default expression. Constants reduce directly; a parameter
/// reference is an alias that resolves to another parameter's own default; a
/// direct call is folded only when it is an elemental constructor over operands
/// that themselves fold, which is what makes `color(0.5)` and `float3(a, b, c)`
/// usable without evaluating arbitrary MDL.
bool
ReduceExpression(
    mi::neuraylib::ITransaction* transaction,
    const mi::neuraylib::IExpression* expression,
    const mi::neuraylib::IExpression_list* siblings,
    unsigned int depth,
    SdkParameterValue* out)
{
    if (expression == nullptr || depth > 4)
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
            return ReduceValue(transaction, value.get(), out);
        }
        case mi::neuraylib::IExpression::EK_PARAMETER:
        {
            mi::base::Handle<const mi::neuraylib::IExpression_parameter> parameter(
                expression->get_interface<mi::neuraylib::IExpression_parameter>());
            if (siblings == nullptr)
            {
                return false;
            }
            const mi::Size index = parameter->get_index();
            if (index >= siblings->get_size())
            {
                return false;
            }
            mi::base::Handle<const mi::neuraylib::IExpression> target(
                siblings->get_expression(index));
            return ReduceExpression(
                transaction, target.get(), siblings, depth + 1, out);
        }
        case mi::neuraylib::IExpression::EK_DIRECT_CALL:
        {
            mi::base::Handle<const mi::neuraylib::IExpression_direct_call> call(
                expression->get_interface<mi::neuraylib::IExpression_direct_call>());
            mi::base::Handle<const mi::neuraylib::IFunction_definition> definition(
                transaction->access<mi::neuraylib::IFunction_definition>(
                    call->get_definition()));
            if (!definition.is_valid_interface())
            {
                return false;
            }
            // Only the elemental constructors fold. Anything else is a real
            // computation, and folding one by guessing would publish a value the
            // module does not produce.
            const mi::neuraylib::IFunction_definition::Semantics semantic =
                definition->get_semantic();
            if (semantic !=
                    mi::neuraylib::IFunction_definition::DS_ELEM_CONSTRUCTOR &&
                semantic !=
                    mi::neuraylib::IFunction_definition::DS_CONV_CONSTRUCTOR &&
                semantic !=
                    mi::neuraylib::IFunction_definition::DS_COPY_CONSTRUCTOR)
            {
                return false;
            }
            mi::base::Handle<const mi::neuraylib::IExpression_list> arguments(
                call->get_arguments());
            const mi::Size count = arguments->get_size();
            if (count == 0 || count > 4)
            {
                return false;
            }
            float components[4] = {0.0F, 0.0F, 0.0F, 0.0F};
            for (mi::Size index = 0; index < count; ++index)
            {
                mi::base::Handle<const mi::neuraylib::IExpression> argument(
                    arguments->get_expression(index));
                SdkParameterValue reduced;
                if (!ReduceExpression(
                        transaction, argument.get(), siblings, depth + 1, &reduced))
                {
                    return false;
                }
                if (reduced.kind == OPENUSD_MDL_VALUE_ASSET ||
                    reduced.kind == OPENUSD_MDL_VALUE_STRING)
                {
                    // A single-argument copy of a resource is still that
                    // resource, which is worth carrying; anything else is not a
                    // numeric constructor.
                    if (count != 1)
                    {
                        return false;
                    }
                    *out = reduced;
                    return true;
                }
                if (reduced.componentCount == 1)
                {
                    components[index] = reduced.kind == OPENUSD_MDL_VALUE_INT ||
                            reduced.kind == OPENUSD_MDL_VALUE_BOOL
                        ? static_cast<float>(reduced.integerValue)
                        : reduced.value[0];
                    continue;
                }
                if (count != 1)
                {
                    return false;
                }
                *out = reduced;
                return true;
            }
            // `color(0.5)` and `float3(0.5)` broadcast their single argument,
            // which is the MDL conversion-constructor rule this fold models.
            const mi::Size arity = count;
            out->componentCount = static_cast<uint32_t>(arity);
            for (mi::Size index = 0; index < arity; ++index)
            {
                out->value[index] = components[index];
            }
            out->kind = arity == 1   ? OPENUSD_MDL_VALUE_FLOAT
                : arity == 2         ? OPENUSD_MDL_VALUE_FLOAT2
                : arity == 3         ? OPENUSD_MDL_VALUE_FLOAT3
                                     : OPENUSD_MDL_VALUE_FLOAT4;
            out->origin = OPENUSD_MDL_ORIGIN_MODULE_EXPRESSION;
            return true;
        }
        default:
            return false;
    }
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
    const std::string& materialName)
{
    (void)moduleUri;
    (void)materialName;
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
    RuntimeState& state = State();
    if (!EnsureRuntimeLoaded(state))
    {
        message = state.failure;
        return OPENUSD_MDL_STATUS_SDK_UNAVAILABLE;
    }
    if (state.configured && state.generation == generation &&
        state.searchPaths == searchPaths)
    {
        return OPENUSD_MDL_STATUS_OK;
    }

    const uint32_t status = ConfigureRuntime(state, searchPaths, &message);
    if (status != OPENUSD_MDL_STATUS_OK)
    {
        return status;
    }

    // A new generation, or a different search path set, invalidates everything
    // the database cached about previously loaded modules: a module resolved
    // from an old path must not answer a request made under a new one.
    if (state.configured &&
        (state.generation != generation || state.searchPaths != searchPaths))
    {
        mi::base::Handle<mi::neuraylib::IScope> scope(
            state.database->get_global_scope());
        mi::base::Handle<mi::neuraylib::ITransaction> transaction(
            scope->create_transaction());
        transaction->commit();
        state.database->garbage_collection();
    }

    state.searchPaths = searchPaths;
    state.generation = generation;
    state.configured = true;
    return OPENUSD_MDL_STATUS_OK;
}

SdkMaterialResolution
SdkBackend::ResolveMaterial(
    const std::string& moduleUri,
    const std::string& materialName)
{
    SdkMaterialResolution resolution;
    std::lock_guard<std::mutex> guard(BackendLock());
    RuntimeState& state = State();
    if (!state.started || !state.configured)
    {
        resolution.status = OPENUSD_MDL_STATUS_SDK_UNAVAILABLE;
        resolution.diagnostic = state.failure.empty()
            ? "the MDL SDK backend has no configured module search path"
            : state.failure;
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

    mi::base::Handle<mi::neuraylib::IScope> scope(state.database->get_global_scope());
    mi::base::Handle<mi::neuraylib::ITransaction> transaction(
        scope->create_transaction());
    mi::base::Handle<mi::neuraylib::IMdl_execution_context> context(
        state.factory->create_execution_context());

    const std::string qualified = ToQualifiedModuleName(moduleUri);
    const mi::Sint32 loaded =
        state.impexp->load_module(transaction.get(), qualified.c_str(), context.get());
    const std::string messages = ContextMessages(context.get());
    if (loaded < 0)
    {
        transaction->abort();
        // -1 is "module name invalid", -2 is "failed to find or initialize". Both
        // are the caller's search path, not the module's contents, so they are
        // reported as not-found rather than as a compile failure.
        resolution.status = loaded == -2 || loaded == -1
            ? OPENUSD_MDL_STATUS_MODULE_NOT_FOUND
            : OPENUSD_MDL_STATUS_MODULE_COMPILE_FAILED;
        resolution.diagnostic = "MDL module '" + qualified + "' did not load (" +
            std::to_string(loaded) + ")" +
            (messages.empty() ? std::string() : ": " + messages);
        return resolution;
    }

    mi::base::Handle<const mi::IString> moduleDbName(
        state.factory->get_db_module_name(qualified.c_str()));
    if (!moduleDbName.is_valid_interface())
    {
        transaction->abort();
        resolution.status = OPENUSD_MDL_STATUS_MODULE_COMPILE_FAILED;
        resolution.diagnostic =
            "MDL module '" + qualified + "' has no database name";
        return resolution;
    }
    mi::base::Handle<const mi::neuraylib::IModule> module(
        transaction->access<mi::neuraylib::IModule>(moduleDbName->get_c_str()));
    if (!module.is_valid_interface())
    {
        transaction->abort();
        resolution.status = OPENUSD_MDL_STATUS_MODULE_COMPILE_FAILED;
        resolution.diagnostic = "MDL module '" + qualified + "' is not in the database";
        return resolution;
    }

    // Find the material by its simple name. The database name carries the module
    // prefix and the signature, so matching on a suffix boundary is what keeps
    // `Metal` from matching `BrushedMetal`.
    std::string definitionName;
    const std::string needle = "::" + materialName + "(";
    for (mi::Size index = 0; index < module->get_material_count(); ++index)
    {
        const char* candidate = module->get_material(index);
        if (candidate == nullptr)
        {
            continue;
        }
        const std::string text(candidate);
        if (text.find(needle) != std::string::npos)
        {
            definitionName = text;
            break;
        }
    }
    // Every database handle is released before the transaction is committed.
    // neuray reports a still-referenced element as an error and leaves the
    // database in a state later transactions inherit, so the release is a
    // correctness requirement rather than tidiness.
    module.reset();
    if (definitionName.empty())
    {
        transaction->abort();
        resolution.status = OPENUSD_MDL_STATUS_UNSUPPORTED_MATERIAL;
        resolution.diagnostic = "MDL module '" + qualified +
            "' declares no material named '" + materialName + "'";
        return resolution;
    }

    {
        mi::base::Handle<const mi::neuraylib::IFunction_definition> definition(
            transaction->access<mi::neuraylib::IFunction_definition>(
                definitionName.c_str()));
        if (!definition.is_valid_interface())
        {
            transaction->abort();
            resolution.status = OPENUSD_MDL_STATUS_UNSUPPORTED_MATERIAL;
            resolution.diagnostic =
                "MDL material '" + definitionName + "' is not in the database";
            return resolution;
        }

        mi::base::Handle<const mi::neuraylib::IExpression_list> defaults(
            definition->get_defaults());
        const mi::Size parameterCount = definition->get_parameter_count();
        for (mi::Size index = 0; index < parameterCount; ++index)
        {
            const char* name = definition->get_parameter_name(index);
            if (name == nullptr)
            {
                continue;
            }
            mi::base::Handle<const mi::neuraylib::IExpression> expression(
                defaults.is_valid_interface() ? defaults->get_expression(name)
                                              : nullptr);
            if (!expression.is_valid_interface())
            {
                // A parameter with no default states nothing this backend can
                // carry. It is not reported: the material simply leaves it to
                // the caller.
                continue;
            }
            SdkParameterValue reduced;
            reduced.origin = OPENUSD_MDL_ORIGIN_MODULE_DEFAULT;
            if (ReduceExpression(
                    transaction.get(), expression.get(), defaults.get(), 0, &reduced))
            {
                resolution.defaults.emplace(std::string(name), reduced);
                continue;
            }
            resolution.unresolved.emplace_back(name);
        }
    }

    transaction->commit();
    resolution.status = OPENUSD_MDL_STATUS_OK;
    if (resolution.defaults.empty())
    {
        resolution.status = OPENUSD_MDL_STATUS_EXPRESSION_UNSUPPORTED;
        resolution.diagnostic = "MDL material '" + materialName + "' in module '" +
            qualified +
            "' declares no parameter default this adapter reduces to a value";
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
