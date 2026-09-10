// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_WAREHOUSE_LOCALIZATION_H
#define OPENUSD_WAREHOUSE_LOCALIZATION_H

#include <pxr/base/js/json.h>
#include <pxr/base/tf/errorMark.h>
#include <pxr/usd/ar/resolver.h>
#include <pxr/usd/sdf/layer.h>
#include <pxr/usd/sdf/layerUtils.h>
#include <pxr/usd/usdShade/udimUtils.h>
#include <pxr/usd/usdUtils/dependencies.h>

#include <algorithm>
#include <chrono>
#include <cctype>
#include <filesystem>
#include <fstream>
#include <map>
#include <stdexcept>
#include <string>

PXR_NAMESPACE_OPEN_SCOPE

namespace openusd_warehouse
{
namespace fs = std::filesystem;

inline std::string PathKey(const fs::path& path)
{
    std::string value = fs::weakly_canonical(path).generic_u8string();
#if defined(_WIN32)
    std::transform(value.begin(), value.end(), value.begin(),
        [](unsigned char item) { return static_cast<char>(std::tolower(item)); });
#endif
    return value;
}

inline fs::path RelativeSource(const fs::path& path, const fs::path& root)
{
    const std::string key = PathKey(path);
    const std::string prefix = PathKey(root) + "/";
    if (key.compare(0, prefix.size(), prefix) != 0)
    {
        throw std::runtime_error("A localized dependency is outside the verified source snapshot.");
    }
    return fs::relative(path, root);
}

struct LocalizedLayers
{
    std::string root;
    uint64_t layer_count = 0;
    uint64_t bytes = 0;
    uint64_t mapped_opinions = 0;
    std::vector<std::string> unresolved;
};

inline LocalizedLayers Localize(
    const fs::path& source_root,
    const fs::path& destination,
    const std::string& root_scene,
    const std::map<std::string, std::string>& mappings)
{
    if (!source_root.is_absolute() || !destination.is_absolute() ||
        fs::exists(destination) || PathKey(destination).compare(0, PathKey(source_root).size(),
            PathKey(source_root)) == 0)
    {
        throw std::runtime_error("Localization requires a new absolute cache outside the source snapshot.");
    }
    const fs::path root = source_root / fs::u8path(root_scene);
    (void)RelativeSource(root, source_root);
    std::map<std::string, std::string> redirects;
    for (const auto& entry : mappings)
    {
        if (entry.first.compare(0, 12, "omniverse://") != 0)
        {
            throw std::runtime_error("Only explicitly inventoried Omniverse asset URIs may be remapped.");
        }
        const fs::path target = source_root / fs::u8path(entry.second);
        (void)RelativeSource(target, source_root);
        if (!fs::is_regular_file(target))
        {
            throw std::runtime_error("An exact local asset mapping names no verified source file.");
        }
        redirects.emplace(entry.first, fs::weakly_canonical(target).generic_u8string());
    }
    std::vector<SdfLayerRefPtr> layers;
    std::vector<std::string> assets;
    std::vector<std::string> unresolved;
    TfErrorMark errors;
    const auto require_clean = [&]
    {
        if (!errors.IsClean())
        {
            const std::string message = errors.GetBegin()->GetCommentary();
            errors.Clear();
            throw std::runtime_error("Localization failed on native layer data: " + message);
        }
    };
    size_t visits = 0;
    if (!UsdUtilsComputeAllDependencies(SdfAssetPath(root.generic_u8string()),
            &layers, &assets, &unresolved,
            [&](const SdfLayerHandle&, const UsdUtilsDependencyInfo& info)
            {
                if (++visits > 1000000)
                {
                    throw std::runtime_error("Localization exceeded the dependency-visit bound.");
                }
                const auto found = redirects.find(info.GetAssetPath());
                return found == redirects.end() ? info : UsdUtilsDependencyInfo(found->second);
            }))
    {
        throw std::runtime_error("The localization root could not be resolved.");
    }
    require_clean();
    if (layers.empty() || layers.size() > 4096)
    {
        throw std::runtime_error("Localization requires between one and 4096 source layers.");
    }
    std::map<std::string, fs::path> localized;
    for (const auto& layer : layers)
    {
        const fs::path path = fs::u8path(layer->GetRealPath());
        localized.emplace(PathKey(path), destination / RelativeSource(path, source_root));
    }
    const fs::path staging = fs::path(destination.u8string() + ".staging-" +
        std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()));
    if (!fs::create_directories(staging))
    {
        throw std::runtime_error("Could not create a new private localization directory.");
    }
    LocalizedLayers result;
    result.root = (destination / RelativeSource(root, source_root)).generic_u8string();
    result.unresolved = std::move(unresolved);
    bool published = false;
    try
    {
        for (const auto& layer : layers)
        {
            const fs::path relative = RelativeSource(fs::u8path(layer->GetRealPath()), source_root);
            auto copy = SdfLayer::CreateAnonymous(".usdc");
            copy->TransferContent(layer);
            UsdUtilsModifyAssetPaths(copy, [&](const std::string& authored)
            {
                if (authored.empty())
                {
                    return authored;
                }
                if (UsdShadeUdimUtils::IsUdimIdentifier(authored))
                {
                    const std::string pattern = UsdShadeUdimUtils::ResolveUdimPath(authored, layer);
                    if (pattern.empty())
                    {
                        return authored;
                    }
                    for (const auto& tile : UsdShadeUdimUtils::ResolveUdimTilePaths(authored, layer))
                    {
                        (void)RelativeSource(fs::u8path(tile.first), source_root);
                    }
                    return fs::u8path(pattern).generic_u8string();
                }
                const auto mapped = redirects.find(authored);
                const std::string anchored = mapped == redirects.end()
                    ? SdfComputeAssetPathRelativeToLayer(layer, authored) : mapped->second;
                const std::string resolved = ArGetResolver().Resolve(anchored).GetPathString();
                if (resolved.empty())
                {
                    return authored;
                }
                (void)RelativeSource(fs::u8path(resolved), source_root);
                if (mapped != redirects.end())
                {
                    ++result.mapped_opinions;
                }
                const auto local_layer = localized.find(PathKey(fs::u8path(resolved)));
                return local_layer == localized.end()
                    ? fs::u8path(resolved).generic_u8string() : local_layer->second.generic_u8string();
            });
            const fs::path path = staging / relative;
            fs::create_directories(path.parent_path());
            if (!copy->Export(path.generic_u8string(), std::string(), {{"format", "usdc"}}))
            {
                throw std::runtime_error("A localized layer could not be exported.");
            }
            require_clean();
            result.bytes += fs::file_size(path);
            if (result.bytes > UINT64_C(8589934592))
            {
                throw std::runtime_error("The derived layer cache exceeds its 8 GiB allowance.");
            }
            ++result.layer_count;
        }
        fs::rename(staging, destination);
        published = true;
    }
    catch (...)
    {
        if (!published)
        {
            fs::remove_all(staging);
        }
        throw;
    }
    return result;
}
}

PXR_NAMESPACE_CLOSE_SCOPE

#endif
