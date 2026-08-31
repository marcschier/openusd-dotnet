// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

#include "pxr/base/plug/plugin.h"
#include "pxr/usd/ar/assetInfo.h"
#include "pxr/usd/ar/resolvedPath.h"
#include "pxr/usd/ar/resolver.h"
#include "pxr/usd/ar/resolverContext.h"
#include "pxr/usd/ar/resolverContextBinder.h"
#include "pxr/usd/ar/timestamp.h"

struct openusd_resolver_context
{
    ArResolverContext value;
};

struct openusd_resolver_binding
{
    explicit openusd_resolver_binding(const ArResolverContext& value)
        : owner(std::this_thread::get_id())
        , context(value)
        , binder(std::make_unique<ArResolverContextBinder>(value))
    {
    }

    ~openusd_resolver_binding()
    {
        // Destroying ArResolverContextBinder is what unbinds, and it unbinds on whatever thread
        // runs the destructor. Releasing the binder anywhere but its owner thread would pop a
        // binding that thread never pushed, so an off-thread destruction deliberately leaks the
        // binder instead: one leaked binding is recoverable, a corrupted thread-local resolver
        // stack in unrelated code is not.
        if (owner == std::this_thread::get_id())
        {
            return;
        }

        TF_CODING_ERROR(
            "openusd_dotnet: a resolver binding was destroyed off its owner thread; "
            "leaking the binder rather than unbinding an unrelated thread.");
        (void)binder.release();
    }

    openusd_resolver_binding(const openusd_resolver_binding&) = delete;
    openusd_resolver_binding& operator=(const openusd_resolver_binding&) = delete;

    std::thread::id owner;
    ArResolverContext context;
    std::unique_ptr<ArResolverContextBinder> binder;
};

struct openusd_resolved_asset_list
{
    std::vector<openusd_resolved_asset_record> records;
    std::vector<char> data;
    std::vector<size_t> offsets;
};

namespace
{
// Refresh has to know whether *this* context is bound anywhere in the process, not whether the
// calling thread happens to hold some unrelated binding. Bindings are thread local, so the only
// way to answer that is a process-wide registry of the contexts that currently have a live
// binding. It stores one entry per live binding, keyed by ArResolverContext value identity, so a
// nested binding of the same context is released one level at a time and an unrelated context
// never blocks a refresh.
std::mutex& BoundContextMutex()
{
    static std::mutex mutex;
    return mutex;
}

std::vector<ArResolverContext>& BoundContexts()
{
    static std::vector<ArResolverContext> contexts;
    return contexts;
}

void RegisterBoundContext(const ArResolverContext& context)
{
    const std::lock_guard<std::mutex> lock(BoundContextMutex());
    BoundContexts().push_back(context);
}

void UnregisterBoundContext(const ArResolverContext& context)
{
    const std::lock_guard<std::mutex> lock(BoundContextMutex());
    std::vector<ArResolverContext>& contexts = BoundContexts();
    const auto entry = std::find(contexts.begin(), contexts.end(), context);
    if (entry != contexts.end())
    {
        contexts.erase(entry);
    }
}

bool IsContextBoundAnywhere(const ArResolverContext& context)
{
    const std::lock_guard<std::mutex> lock(BoundContextMutex());
    const std::vector<ArResolverContext>& contexts = BoundContexts();
    return std::find(contexts.begin(), contexts.end(), context) != contexts.end();
}

// ArResolverContextBinder installs a thread-local binding, so a binding may only be released on
// the thread that created it and only after every binding created after it. The shim tracks the
// per-thread stack rather than trusting the caller, because unwinding it out of order corrupts
// the resolver state of an unrelated caller instead of failing the call that got it wrong.
struct ResolverBindingStackGuard
{
    ~ResolverBindingStackGuard()
    {
        if (entries.empty())
        {
            return;
        }

        // A thread that exits while still bound is a caller bug: the bindings can no longer be
        // released in order, and destroying them here would unbind while the thread is already
        // tearing down its thread-local state. Release ownership of the binders so nothing
        // unbinds off-thread, and report the leak.
        //
        // The report deliberately does not go through Tf. This runs during thread-local
        // destruction, and the order of this object against TfDiagnosticMgr's own thread-local
        // state is unspecified, so posting a diagnostic here could touch destroyed state. stderr
        // stays valid until process exit.
        std::fprintf(
            stderr,
            "openusd_dotnet: thread exited holding %zu resolver binding(s); leaking them rather "
            "than unbinding during thread teardown.\n",
            entries.size());
        for (openusd_resolver_binding* entry : entries)
        {
            // Upstream's own thread-local binding stack dies with the thread, so these contexts
            // are no longer bound anywhere. Dropping them from the process-wide registry keeps a
            // leaked binding from blocking every later refresh of that context forever.
            UnregisterBoundContext(entry->context);
            (void)entry->binder.release();
        }
        entries.clear();
    }

    std::vector<openusd_resolver_binding*> entries;
};

thread_local ResolverBindingStackGuard ResolverBindingStack;

// ArGetAvailableResolvers is documented as unsafe to call concurrently with itself or with
// ArCreateResolver, and constructing the primary resolver is exactly an ArCreateResolver call.
// The shim is the only place that can serialize the two for every managed consumer.
std::mutex& ResolverDiscoveryMutex()
{
    static std::mutex mutex;
    return mutex;
}

// Forces the shim's own first touch of the primary resolver to happen under the discovery mutex,
// so no shim call can construct the resolver while another shim call is enumerating candidates.
// After the first call this costs one atomic load, so it stays off the bulk-resolve cost.
//
// This closes the shim-internal race only. OpenUSD constructs its resolver on first use from
// anywhere - a plain UsdStage::Open, a layer read, or any upstream code outside this shim - and
// that first use cannot be serialized from here. The first-use contract therefore still stands:
// register every plugin tree and touch resolution once from a single thread before the process
// goes concurrent.
void EnsurePrimaryResolver()
{
    static std::once_flag once;
    std::call_once(once, []()
    {
        const std::lock_guard<std::mutex> lock(ResolverDiscoveryMutex());
        (void)ArGetResolver();
    });
}

void ResetResolvedAssetOutput(
    openusd_resolved_asset_list** list,
    openusd_resolved_asset_view* view) noexcept
{
    ResetAbiOutput(list);
    ResetVersionedAbiOutput(view);
    if (view == nullptr)
    {
        return;
    }

    uint32_t struct_size = 0;
    std::memcpy(&struct_size, view, sizeof(struct_size));
    if (struct_size >= offsetof(openusd_resolved_asset_view, version) + sizeof(uint32_t))
    {
        const uint32_t version = OPENUSD_RESOLVED_ASSET_VIEW_VERSION;
        std::memcpy(
            reinterpret_cast<unsigned char*>(view) +
                offsetof(openusd_resolved_asset_view, version),
            &version,
            sizeof(version));
    }
}

void AppendResolvedString(openusd_resolved_asset_list& list, const std::string& value)
{
    if (value.find('\0') != std::string::npos)
    {
        throw std::invalid_argument("Resolved-asset strings must not contain embedded NULs.");
    }
    list.offsets.push_back(list.data.size());
    list.data.insert(list.data.end(), value.begin(), value.end());
    list.data.push_back('\0');
}

void FillResolvedAssetView(
    openusd_resolved_asset_list& list,
    openusd_resolved_asset_view* view)
{
    view->version = OPENUSD_RESOLVED_ASSET_VIEW_VERSION;
    view->records = list.records.empty() ? nullptr : list.records.data();
    view->records_size = list.records.size() * sizeof(openusd_resolved_asset_record);
    view->record_count = list.records.size();
    view->data = list.data.empty() ? nullptr : list.data.data();
    view->data_size = list.data.size();
    view->offsets = list.offsets.empty() ? nullptr : list.offsets.data();
    view->offsets_size = list.offsets.size() * sizeof(size_t);
    view->string_count = list.offsets.size();
}

openusd_status ReadStringListValues(
    const openusd_string_list_view* view,
    const char* label,
    std::vector<std::string>* values,
    openusd_error_buffer* error)
{
    const openusd_status validation = ValidateStringListView(view, label, error);
    if (validation != OPENUSD_STATUS_OK)
    {
        return validation;
    }

    values->clear();
    values->reserve(view->count);
    for (size_t index = 0; index < view->count; ++index)
    {
        values->emplace_back(view->data + view->offsets[index]);
    }
    return OPENUSD_STATUS_OK;
}

std::string PluginKind(const PlugPluginPtr& plugin)
{
    if (plugin->IsResource())
    {
        return "resource";
    }
#ifdef PXR_PYTHON_SUPPORT_ENABLED
    if (plugin->IsPythonModule())
    {
        return "python";
    }
#endif
    return "library";
}
}

openusd_status openusd_get_registered_plugins(
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetStringListOutput(list, view);
        if (list == nullptr || view == nullptr || !IsAligned(view) ||
            view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(error, "A versioned plugin-list output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            PlugPluginPtrVector plugins = PlugRegistry::GetInstance().GetAllPlugins();

            // Null entries are dropped before sorting, not skipped while packing: the comparator
            // dereferences every element, so a null that survived to the sort would crash before
            // any later skip could run.
            plugins.erase(
                std::remove_if(
                    plugins.begin(),
                    plugins.end(),
                    [](const PlugPluginPtr& plugin) { return !plugin; }),
                plugins.end());
            std::sort(
                plugins.begin(),
                plugins.end(),
                [](const PlugPluginPtr& left, const PlugPluginPtr& right)
                {
                    return left->GetName() < right->GetName();
                });

            std::vector<std::string> values;
            values.reserve(plugins.size() * 5u);
            for (const PlugPluginPtr& plugin : plugins)
            {
                values.push_back(plugin->GetName());
                values.push_back(PluginKind(plugin));
                values.push_back(plugin->IsLoaded() ? "loaded" : "unloaded");
                values.push_back(plugin->GetPath());
                values.push_back(plugin->GetResourcePath());
            }

            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), values, view);
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_resolver_get_primary_type_name(
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiStringOutput(buffer, capacity);
        ResetAbiOutput(required);
        if (required == nullptr)
        {
            WriteError(error, "A required-size output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        EnsurePrimaryResolver();
        const TfType type = TfType::Find(ArGetUnderlyingResolver());
        const std::string name = type ? type.GetTypeName() : std::string("ArResolver");
        return CopyString(name, buffer, capacity, required);
    });
}
openusd_status openusd_resolver_get_uri_schemes(
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetStringListOutput(list, view);
        if (list == nullptr || view == nullptr || !IsAligned(view) ||
            view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(error, "A versioned URI-scheme list output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            EnsurePrimaryResolver();
            const std::vector<std::string>& schemes = ArGetRegisteredURISchemes();
            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), schemes, view);
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_resolver_get_available_type_names(
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetStringListOutput(list, view);
        if (list == nullptr || view == nullptr || !IsAligned(view) ||
            view->struct_size < sizeof(openusd_string_list_view))
        {
            WriteError(error, "A versioned resolver-type list output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return GuardStringListOutput(error, list, view, [&](auto& result)
        {
            // The primary resolver is constructed first and under the same mutex, so this
            // enumeration can never run concurrently with the shim's own resolver construction.
            EnsurePrimaryResolver();
            std::vector<std::string> names;
            {
                const std::lock_guard<std::mutex> lock(ResolverDiscoveryMutex());
                const std::vector<TfType> types = ArGetAvailableResolvers();
                names.reserve(types.size());
                for (const TfType& type : types)
                {
                    names.push_back(type ? type.GetTypeName() : std::string());
                }
            }
            std::sort(names.begin(), names.end());

            result = std::make_unique<openusd_string_list>();
            FillStringList(result.get(), names, view);
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_resolver_context_create(
    const openusd_string_list_view* context_strings,
    openusd_resolver_context** context,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(context);
        if (context == nullptr)
        {
            WriteError(error, "A resolver-context output handle is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        std::vector<std::string> values;
        const openusd_status read =
            ReadStringListValues(context_strings, "resolver-context list", &values, error);
        if (read != OPENUSD_STATUS_OK)
        {
            return read;
        }
        if ((values.size() % 2u) != 0u)
        {
            WriteError(
                error,
                "Resolver-context strings must be scheme and context-string pairs.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            EnsurePrimaryResolver();
            auto handle = std::make_unique<openusd_resolver_context>();
            if (values.empty())
            {
                handle->value = ArGetResolver().CreateDefaultContext();
            }
            else
            {
                std::vector<std::pair<std::string, std::string>> pairs;
                pairs.reserve(values.size() / 2u);
                for (size_t index = 0; index < values.size(); index += 2u)
                {
                    pairs.emplace_back(values[index], values[index + 1u]);
                }
                handle->value = ArGetResolver().CreateContextFromStrings(pairs);
            }

            *context = handle.release();
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_resolver_context_create_for_asset(
    const char* asset_path,
    openusd_resolver_context** context,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(context);
        if (asset_path == nullptr || asset_path[0] == '\0' || context == nullptr)
        {
            WriteError(error, "An asset path and resolver-context output handle are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            EnsurePrimaryResolver();
            auto handle = std::make_unique<openusd_resolver_context>();
            handle->value = ArGetResolver().CreateDefaultContextForAsset(asset_path);
            *context = handle.release();
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_resolver_context_get_debug_string(
    const openusd_resolver_context* context,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiStringOutput(buffer, capacity);
        ResetAbiOutput(required);
        if (context == nullptr || required == nullptr)
        {
            WriteError(error, "A resolver context and required-size output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return CopyString(context->value.GetDebugString(), buffer, capacity, required);
    });
}

openusd_status openusd_resolver_context_is_empty(
    const openusd_resolver_context* context,
    int32_t* value,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(value);
        if (context == nullptr || value == nullptr)
        {
            WriteError(error, "A resolver context and boolean output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        *value = context->value.IsEmpty() ? 1 : 0;
        return OPENUSD_STATUS_OK;
    });
}

openusd_status openusd_resolver_context_refresh(
    openusd_resolver_context* const context,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        if (context == nullptr)
        {
            WriteError(error, "A resolver context is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (IsContextBoundAnywhere(context->value))
        {
            WriteError(
                error,
                "A resolver context cannot be refreshed while it is bound on any thread.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        // RefreshContext is process-wide: it invalidates resolver caches for every thread and
        // sends ArNotice::ResolverChanged, whose listeners mutate their own state. Upstream
        // documents concurrent refreshes of one context as unsafe, and the shim cannot make them
        // safe, so this stays a single-threaded quiescent-point operation by contract. The
        // rejection above is scoped to this context by value identity: an unrelated context bound
        // on this or any other thread never blocks it, and this context bound on another thread
        // always does.
        EnsurePrimaryResolver();
        ArGetResolver().RefreshContext(context->value);
        return OPENUSD_STATUS_OK;
    });
}

void openusd_resolver_context_release(openusd_resolver_context* context)
{
    delete context;
}

openusd_status openusd_resolver_context_bind(
    openusd_resolver_context* const context,
    openusd_resolver_binding** binding,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(binding);
        if (context == nullptr || binding == nullptr)
        {
            WriteError(error, "A resolver context and binding output handle are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            EnsurePrimaryResolver();
            auto handle = std::make_unique<openusd_resolver_binding>(context->value);
            ResolverBindingStack.entries.push_back(handle.get());
            try
            {
                RegisterBoundContext(handle->context);
            }
            catch (...)
            {
                // The two registries have to agree. If the process-wide one could not record the
                // binding, roll the thread stack back before the unique_ptr unbinds, or the stack
                // would keep a pointer to a destroyed binding.
                ResolverBindingStack.entries.pop_back();
                throw;
            }
            *binding = handle.release();
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_resolver_context_unbind(
    openusd_resolver_binding* const binding,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        if (binding == nullptr)
        {
            WriteError(error, "A resolver binding is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (binding->owner != std::this_thread::get_id())
        {
            WriteError(
                error,
                "A resolver binding must be released on the thread that created it.");
            return OPENUSD_STATUS_WRONG_THREAD;
        }
        if (ResolverBindingStack.entries.empty() ||
            ResolverBindingStack.entries.back() != binding)
        {
            WriteError(
                error,
                "Resolver bindings must be released in reverse creation order.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        // Both rejections above leave the binding owned by the caller and still on this thread's
        // stack, so a caller that unbinds from the wrong thread or out of order can retry from
        // the right thread and in the right order without leaking the handle.
        ResolverBindingStack.entries.pop_back();
        const ArResolverContext released = binding->context;
        delete binding;

        // Deregistered only after the binder is destroyed. A refresh racing this window sees the
        // context as still bound and is rejected, which the caller can retry; the reverse order
        // would let a refresh run while the context is genuinely still bound.
        UnregisterBoundContext(released);
        return OPENUSD_STATUS_OK;
    });
}

openusd_status openusd_resolver_resolve(
    const openusd_string_list_view* asset_paths,
    const openusd_resolver_context* context,
    const char* anchor_asset_path,
    openusd_resolved_asset_list** list,
    openusd_resolved_asset_view* view,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        const uint32_t structSize = view == nullptr ? 0 : view->struct_size;
        const uint32_t requestedVersion = view == nullptr ? 0 : view->version;
        // ABI_OUTPUT_INITIALIZATION
        ResetResolvedAssetOutput(list, view);
        if (list == nullptr || view == nullptr || !IsAligned(view) ||
            structSize < sizeof(openusd_resolved_asset_view) ||
            requestedVersion != OPENUSD_RESOLVED_ASSET_VIEW_VERSION)
        {
            WriteError(
                error,
                "A resolved-asset output list and view version 1 are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        std::vector<std::string> paths;
        const openusd_status read =
            ReadStringListValues(asset_paths, "asset-path list", &paths, error);
        if (read != OPENUSD_STATUS_OK)
        {
            return read;
        }

        std::unique_ptr<openusd_resolved_asset_list> result;
        const openusd_status status = Guard(error, [&]() -> openusd_status
        {
            result = std::make_unique<openusd_resolved_asset_list>();
            result->records.reserve(paths.size());
            result->offsets.reserve(paths.size() * 5u);

            EnsurePrimaryResolver();
            ArResolver& resolver = ArGetResolver();
            const ArResolvedPath anchor =
                anchor_asset_path == nullptr || anchor_asset_path[0] == '\0'
                    ? ArResolvedPath()
                    : ArResolvedPath(anchor_asset_path);
            // The batch is bound once for every entry rather than per element: binding is the
            // expensive, order-sensitive part of upstream resolution, and a per-element binding
            // would also make the batch observably different from a stage opened with the same
            // context.
            //
            // Any non-null context is bound, including an empty one. An empty ArResolverContext
            // is not "no context": binding it shadows whatever context the calling thread already
            // has bound, which is the only way a caller can ask for ambient-free resolution.
            // Skipping the bind for an empty context would silently resolve against the ambient
            // context instead, so passing a context and passing null would differ only by
            // accident. Null still means "use whatever is already bound".
            std::unique_ptr<ArResolverContextBinder> binder;
            if (context != nullptr)
            {
                binder = std::make_unique<ArResolverContextBinder>(context->value);
            }

            for (const std::string& path : paths)
            {
                const std::string identifier = resolver.CreateIdentifier(path, anchor);

                // An empty identifier means the resolver refused to identify the asset at all.
                // Upstream composition treats that as unusable - SdfLayer and UsdStage will not
                // open it - so the record is reported unresolved rather than falling back to
                // resolving the raw asset path. The fallback would report a resolved path for an
                // asset a stage opened with the same context cannot load, which is exactly the
                // disagreement this ABI exists to avoid.
                const bool identified = !identifier.empty();
                const std::string& lookup = identified ? identifier : path;
                const ArResolvedPath resolved =
                    identified ? resolver.Resolve(identifier) : ArResolvedPath();

                openusd_resolved_asset_record record{};
                record.resolved = resolved ? 1 : 0;
                record.context_dependent = resolver.IsContextDependentPath(lookup) ? 1 : 0;

                std::string version;
                std::string assetName;
                if (resolved)
                {
                    const ArAssetInfo info = resolver.GetAssetInfo(lookup, resolved);
                    version = info.version;
                    assetName = info.assetName;
                    const ArTimestamp timestamp =
                        resolver.GetModificationTimestamp(lookup, resolved);
                    record.timestamp_valid = timestamp.IsValid() ? 1 : 0;
                    record.modification_time =
                        timestamp.IsValid() ? timestamp.GetTime() : 0.0;
                }

                record.identifier_offset = result->offsets.size();
                AppendResolvedString(*result, identifier);
                record.resolved_path_offset = result->offsets.size();
                AppendResolvedString(*result, resolved.GetPathString());
                record.extension_offset = result->offsets.size();
                AppendResolvedString(*result, resolver.GetExtension(lookup));
                record.asset_version_offset = result->offsets.size();
                AppendResolvedString(*result, version);
                record.asset_name_offset = result->offsets.size();
                AppendResolvedString(*result, assetName);
                result->records.push_back(record);
            }

            FillResolvedAssetView(*result, view);
            return OPENUSD_STATUS_OK;
        });

        if (status == OPENUSD_STATUS_OK && result)
        {
            *list = result.release();
            return OPENUSD_STATUS_OK;
        }
        result.reset();
        ResetResolvedAssetOutput(list, view);
        return status == OPENUSD_STATUS_OK ? OPENUSD_STATUS_NATIVE_ERROR : status;
    });
}

void openusd_resolved_asset_list_release(openusd_resolved_asset_list* list)
{
    delete list;
}

openusd_status openusd_stage_open_with_context(
    const char* path,
    const openusd_resolver_context* context,
    openusd_stage** stage,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(stage);
        if (path == nullptr || path[0] == '\0' || context == nullptr || stage == nullptr)
        {
            WriteError(
                error,
                "A stage path, resolver context, and output handle are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        *stage = nullptr;
        return Guard(error, [&]()
        {
            EnsurePrimaryResolver();
            TfErrorMark mark;
            UsdStageRefPtr value = UsdStage::Open(path, context->value);
            if (!value || !mark.IsClean())
            {
                std::string message = ConsumeErrors(mark);
                if (message.empty())
                {
                    message = std::string("Could not open stage: ") + path;
                }
                WriteError(error, message);
                return value ? OPENUSD_STATUS_NATIVE_ERROR : OPENUSD_STATUS_NOT_FOUND;
            }

            auto handle = std::make_unique<openusd_stage>(std::move(value));
            *stage = handle.release();
            return OPENUSD_STATUS_OK;
        });
    });
}
