// Copyright (c) marcschier. Licensed under the MIT License.
//
// A deliberately small third-party style URI resolver used to prove the project's plugin
// contract end to end: it lives in its own plugin tree with its own unflattened plugInfo.json,
// registers a URI scheme, implements resolver contexts, and is never linked into the shim.

#include "pxr/pxr.h"

#include "pxr/base/tf/hash.h"
#include "pxr/base/tf/stringUtils.h"
#include "pxr/usd/ar/assetInfo.h"
#include "pxr/usd/ar/defineResolver.h"
#include "pxr/usd/ar/defineResolverContext.h"
#include "pxr/usd/ar/filesystemAsset.h"
#include "pxr/usd/ar/resolvedPath.h"
#include "pxr/usd/ar/resolver.h"
#include "pxr/usd/ar/timestamp.h"
#include "pxr/usd/ar/writableAsset.h"

#include <filesystem>
#include <memory>
#include <string>
#include <system_error>
#include <utility>

PXR_NAMESPACE_OPEN_SCOPE

namespace
{
constexpr const char* TestResolverScheme = "openusdtest://";
constexpr double TestResolverTimestamp = 1234.5;

// An asset name with this prefix is deliberately refused an identifier even though the file it
// names exists and _Resolve would find it. That is the only way to prove the shim treats an empty
// CreateIdentifier as unresolved instead of silently resolving the raw asset path, which is what
// upstream composition does.
constexpr const char* TestResolverUnidentifiedPrefix = "no-identifier-";
}

/// Context object that maps the test URI scheme onto a directory.
class OpenUsdTestResolverContext
{
public:
    OpenUsdTestResolverContext() = default;

    explicit OpenUsdTestResolverContext(std::string root)
        : _root(std::move(root))
    {
    }

    const std::string& GetRoot() const
    {
        return _root;
    }

    bool operator<(const OpenUsdTestResolverContext& rhs) const
    {
        return _root < rhs._root;
    }

    bool operator==(const OpenUsdTestResolverContext& rhs) const
    {
        return _root == rhs._root;
    }

private:
    std::string _root;
};

inline size_t hash_value(const OpenUsdTestResolverContext& context)
{
    return TfHash()(context.GetRoot());
}

inline std::string ArGetDebugString(const OpenUsdTestResolverContext& context)
{
    return "OpenUsdTestResolverContext(" + context.GetRoot() + ")";
}

AR_DECLARE_RESOLVER_CONTEXT(OpenUsdTestResolverContext);

class OpenUsdTestResolver final : public ArResolver
{
public:
    OpenUsdTestResolver() = default;

protected:
    std::string _CreateIdentifier(
        const std::string& assetPath,
        const ArResolvedPath& anchorAssetPath) const override
    {
        (void)anchorAssetPath;
        if (!TfStringStartsWith(assetPath, TestResolverScheme))
        {
            return std::string();
        }
        if (TfStringStartsWith(_GetAssetName(assetPath), TestResolverUnidentifiedPrefix))
        {
            return std::string();
        }
        return assetPath;
    }

    std::string _CreateIdentifierForNewAsset(
        const std::string& assetPath,
        const ArResolvedPath& anchorAssetPath) const override
    {
        return _CreateIdentifier(assetPath, anchorAssetPath);
    }

    ArResolvedPath _Resolve(const std::string& assetPath) const override
    {
        const std::string name = _GetAssetName(assetPath);
        if (name.empty())
        {
            return ArResolvedPath();
        }

        const OpenUsdTestResolverContext* context =
            _GetCurrentContextObject<OpenUsdTestResolverContext>();
        if (context == nullptr || context->GetRoot().empty())
        {
            return ArResolvedPath();
        }

        std::error_code code;
        const std::filesystem::path candidate =
            std::filesystem::path(context->GetRoot()) / name;
        if (!std::filesystem::exists(candidate, code) || code)
        {
            return ArResolvedPath();
        }
        return ArResolvedPath(candidate.lexically_normal().string());
    }

    ArResolvedPath _ResolveForNewAsset(const std::string& assetPath) const override
    {
        return _Resolve(assetPath);
    }

    ArResolverContext _CreateContextFromString(const std::string& contextStr) const override
    {
        return ArResolverContext(OpenUsdTestResolverContext(contextStr));
    }

    bool _IsContextDependentPath(const std::string& assetPath) const override
    {
        return TfStringStartsWith(assetPath, TestResolverScheme);
    }

    ArAssetInfo _GetAssetInfo(
        const std::string& assetPath,
        const ArResolvedPath& resolvedPath) const override
    {
        (void)resolvedPath;
        ArAssetInfo info;
        info.version = "test-1";
        info.assetName = _GetAssetName(assetPath);
        return info;
    }

    ArTimestamp _GetModificationTimestamp(
        const std::string& assetPath,
        const ArResolvedPath& resolvedPath) const override
    {
        (void)assetPath;
        return resolvedPath.IsEmpty()
            ? ArTimestamp()
            : ArTimestamp(TestResolverTimestamp);
    }

    std::shared_ptr<ArAsset> _OpenAsset(const ArResolvedPath& resolvedPath) const override
    {
        return ArFilesystemAsset::Open(resolvedPath);
    }

    std::shared_ptr<ArWritableAsset> _OpenAssetForWrite(
        const ArResolvedPath& resolvedPath,
        WriteMode writeMode) const override
    {
        // The test resolver is deliberately read-only: a vendor resolver that cannot write is a
        // supported shape, and proving it stays read-only is more useful than a writable stub.
        (void)resolvedPath;
        (void)writeMode;
        return nullptr;
    }

private:
    static std::string _GetAssetName(const std::string& assetPath)
    {
        if (!TfStringStartsWith(assetPath, TestResolverScheme))
        {
            return std::string();
        }
        return assetPath.substr(std::char_traits<char>::length(TestResolverScheme));
    }
};

AR_DEFINE_RESOLVER(OpenUsdTestResolver, ArResolver);

PXR_NAMESPACE_CLOSE_SCOPE
