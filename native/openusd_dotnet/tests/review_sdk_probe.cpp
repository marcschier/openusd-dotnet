// Copyright (c) marcschier. Licensed under the MIT License.
#include "pxr/base/plug/registry.h"
#include "pxr/base/tf/errorMark.h"
#include "pxr/usd/sdf/data.h"
#include "pxr/usd/sdf/fileFormat.h"
#include "pxr/usd/sdf/layer.h"
#include "pxr/usd/sdf/layerUtils.h"
#include "pxr/usd/usd/stage.h"
#include "pxr/usd/usd/attribute.h"
#include "pxr/usd/ar/resolver.h"

#include <atomic>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <thread>

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#endif

PXR_NAMESPACE_USING_DIRECTIVE
namespace
{
void Require(bool value, const char* message)
{
    if (!value) { throw std::runtime_error(message); }
}

class SeedFormat final : public SdfFileFormat
{
public:
    SeedFormat(SdfFileFormatConstPtr native, SdfAbstractDataRefPtr seed,
        std::atomic<bool>* entered, std::atomic<bool>* release)
        : SdfFileFormat(native->GetFormatId(), native->GetVersionString(),
            native->GetTarget(), native->GetPrimaryFileExtension()),
          _native(std::move(native)), _seed(std::move(seed)), _entered(entered), _release(release) {}
    bool CanRead(const std::string&) const override { return false; }
    bool Read(SdfLayer*, const std::string&, bool) const override { return false; }
    static SdfAbstractDataRefPtr Copy(const SdfLayer& layer)
    {
        auto copy = TfCreateRefPtr(new SdfData);
        copy->CopyFrom(_GetLayerData(layer));
        return copy;
    }
protected:
    SdfLayer* _InstantiateNewLayer(const SdfFileFormatConstPtr&,
        const std::string& identifier, const std::string& realPath,
        const ArAssetInfo& info, const FileFormatArguments& args) const override
    {
        std::cerr << "SDK entering unpublished constructor\n";
        auto* layer = SdfFileFormat::_InstantiateNewLayer(_native, identifier, realPath, info, args);
        auto data = _seed;
        _SetLayerData(layer, data);
        std::cerr << "SDK initial exact data installed\n";
        if (_entered)
        {
            _entered->store(true);
            while (!_release->load()) { std::this_thread::yield(); }
        }
        return layer;
    }
private:
    SdfFileFormatConstPtr _native;
    SdfAbstractDataRefPtr _seed;
    std::atomic<bool>* _entered;
    std::atomic<bool>* _release;
};
}

int main(int argc, char** argv)
{
    try
    {
        Require(argc == 3, "Expected plugin and owned output paths.");
        PlugRegistry::GetInstance().RegisterPlugins(argv[1]);
        const auto directory = std::filesystem::path(argv[2]) / "sdk-origin";
        std::filesystem::create_directories(directory);
        const auto sourcePath = directory / "source.usda";
        const auto assetPath = directory / "image.bin";
        const auto reviewPath = directory / "unwritten-review.usda";
        const std::string content =
            "#usda 1.0\n(\n metersPerUnit = 0.25\n)\ndef \"World\"\n"
            "{\n double weight = 42\n asset image = @./image.bin@\n}\n";
        { std::ofstream file(sourcePath, std::ios::binary); file << content; }
        { std::ofstream file(assetPath, std::ios::binary); file << "asset bytes"; }
        TfErrorMark mark;
        const auto parsed = SdfLayer::CreateAnonymous("admitted.usda");
        Require(parsed->ImportFromString(content), "Parse actual admitted text.");
        std::cerr << "SDK parsed input\n";
        const auto native = SdfFileFormat::FindById(TfToken("usda"));
        std::atomic<bool> entered{false}, release{false}, found{false};
        const auto format = TfCreateRefPtr(new SeedFormat(
            native, SeedFormat::Copy(*parsed), &entered, &release));
        SdfLayerRefPtr root, concurrent;
        std::thread create([&]() { root = SdfLayer::New(format, sourcePath.u8string()); });
        while (!entered.load()) { std::this_thread::yield(); }
        std::thread find([&]()
        {
            concurrent = SdfLayer::FindOrOpen(sourcePath.u8string());
            found.store(true);
        });
        std::this_thread::sleep_for(std::chrono::milliseconds(50));
        const bool publishedEarly = found.load();
        release.store(true);
        create.join();
        find.join();
        std::cerr << "SDK concurrent opens finished\n";
        Require(!publishedEarly, "An initializing source must not publish an empty transient layer.");
        Require(root && root == concurrent, "Concurrent normal open gets the fully seeded actual source.");
        Require(root->GetFileFormat() == native, "Seed factory must not replace the native source file format.");
        Require(!root->IsDirty(), "Seeded source starts clean without filesystem Save.");
        const auto stage = UsdStage::Open(root);
        Require(stage && stage->GetRootLayer() == root, "Actual source remains stage root.");
        VtValue units;
        Require(stage->GetMetadata(TfToken("metersPerUnit"), &units)
            && units.IsHolding<double>() && units.UncheckedGet<double>() == 0.25,
            "Source root metadata must keep root semantics.");
        const auto reviewFormat = TfCreateRefPtr(new SeedFormat(
            native, SeedFormat::Copy(*parsed), nullptr, nullptr));
        const auto review = SdfLayer::New(reviewFormat, reviewPath.u8string());
        Require(review && !review->GetResolvedPath().empty()
            && !std::filesystem::exists(reviewPath), "New must provide a resolved anchor without creating a file.");
        stage->GetSessionLayer()->InsertSubLayerPath(review->GetIdentifier());
        SdfAssetPath asset;
        Require(stage->GetAttributeAtPath(SdfPath("/World.image")).Get(&asset),
            "Read anchored imported-review asset.");
        Require(std::filesystem::equivalent(std::filesystem::u8path(asset.GetResolvedPath()), assetPath),
            "Dot-relative assets must resolve from New's real anchor, not CWD or a SaveAs location.");
        Require(mark.IsClean(), "SDK oracle must not emit errors.");
#if defined(_WIN32)
        HANDLE lease = CreateFileW(sourcePath.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr,
            OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        Require(lease != INVALID_HANDLE_VALUE, "Acquire the stable source read lease.");
        HANDLE writer = CreateFileW(sourcePath.c_str(), GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        const bool deniedWriter = writer == INVALID_HANDLE_VALUE;
        if (!deniedWriter) { CloseHandle(writer); }
        std::error_code renameError;
        const auto moved = directory.parent_path() / "sdk-origin-moved";
        std::filesystem::rename(directory, moved, renameError);
        const bool deniedRename = static_cast<bool>(renameError);
        if (!deniedRename) { std::filesystem::rename(moved, directory); }
        CloseHandle(lease);
        Require(deniedWriter && deniedRename,
            "A retained Windows source read lease prevents writes and ancestor renames during SDK resolution.");
#endif
        std::ifstream file(sourcePath, std::ios::binary);
        const std::string unchanged((std::istreambuf_iterator<char>(file)), {});
        Require(unchanged == content, "Origin and anchor proof must not change source bytes.");
        std::cout << "PASS SDK: initial-data swap before registry publication, native format/root,"
            " unwritten New real anchor, composed ./ resolution without identifier rewriting\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << "FAIL SDK: " << error.what() << '\n';
        return 1;
    }
}
