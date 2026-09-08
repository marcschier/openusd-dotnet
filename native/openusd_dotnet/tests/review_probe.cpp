// Copyright (c) marcschier. Licensed under the MIT License.
#include "openusd_review_document.h"
#include "layer_edit_probe_support.h"
#include "review_probe_support.h"
#include "pxr/base/plug/registry.h"

#include <filesystem>
#include <fstream>
#include <iostream>

using namespace EditProbe;
int main(int argc, char** argv)
{
    try
    {
        Require(argc == 3 || argc == 4, "Expected plugins, owned output directory, and optional process mode.");
        pxr::PlugRegistry::GetInstance().RegisterPlugins(argv[1]);
        if (argc == 4)
        {
            ReviewProbe::ProcessMode(std::filesystem::absolute(argv[2]), argv[3]);
            return 0;
        }
        const auto rootPath = std::filesystem::path(argv[2]) / "review-source.usda";
        const auto documentPath = std::filesystem::path(argv[2]) / "saved-review.urd";
        const auto path = rootPath.u8string();
        const auto target = documentPath.u8string();
        const std::string original = "#usda 1.0\ndef \"World\"\n{\n double weight = 42\n}\n";
        { std::ofstream file(rootPath, std::ios::binary); file << original; }
        Api api;
        openusd_stage* stage = nullptr;
        api.Ok(openusd_stage_open_for_review(path.c_str(), &stage, &api.error));
        openusd_edit_buffer* owner = nullptr;
        openusd_edit_buffer_view view{};
        api.Ok(openusd_stage_review_source_binding(stage, &owner, &view, &api.error));
        const auto binding = Api::Copy(owner, view);
        Require(ReadU32(binding, 0) == 0x31425352, "Return RSB1 binding.");
        openusd_layer* review = nullptr;
        api.Ok(openusd_stage_edit_get_user_layer(stage, &review, &api.error));
        const Address weight{"/World.weight"};
        const auto after = api.Apply(review, api.Capture(review, {weight}), {SetDouble(weight, 47)});
        api.Ok(openusd_layer_review_capture(review, binding.data(), binding.size(),
            target.c_str(), target.size(), &owner, &view, &api.error));
        const auto envelope = Api::Copy(owner, view);
        Require(ReadU32(envelope, 0) == 0x31454452, "Capture RDE1 envelope.");
        const size_t documentSize = ReadU32(envelope, 8);
        Require(documentSize <= envelope.size() - 12, "Document envelope length is bounded.");
        Bytes document(envelope.begin() + 12, envelope.begin() + 12 + documentSize);
        Require(ReadU32(document, 0) == 0x31445255, "Portable bytes use URD1, not process-local UED1.");
        const size_t receiptOffset = 12 + documentSize;
        const size_t receiptSize = ReadU32(envelope, receiptOffset);
        Require(receiptSize <= envelope.size() - receiptOffset - 4, "Receipt envelope length is bounded.");
        Bytes receipt(envelope.begin() + receiptOffset + 4,
            envelope.begin() + receiptOffset + 4 + receiptSize);
        int32_t acknowledged = 0;
        api.Ok(openusd_layer_review_acknowledge_saved(review, receipt.data(), receipt.size(),
            &acknowledged, &api.error));
        Require(acknowledged == 1, "Captured current receipt acknowledges the logical save.");
        openusd_layer_release(review);
        openusd_stage_release(stage);
        api.Ok(openusd_review_document_read(document.data(), document.size(), path.c_str(),
            path.size(), &owner, &view, &api.error));
        const auto read = Api::Copy(owner, view);
        Require(ReadU32(read, 12 + ReadU32(read, 8)) == 0, "Read cannot reconstruct a process-local receipt.");
        api.Ok(openusd_stage_open_for_review(path.c_str(), &stage, &api.error));
        api.Ok(openusd_stage_review_source_binding(stage, &owner, &view, &api.error));
        const auto newBinding = Api::Copy(owner, view);
        int32_t outcome = -1;
        api.Ok(openusd_stage_review_import(stage, document.data(), document.size(),
            newBinding.data(), newBinding.size(), &outcome, &owner, &view, &api.error));
        const auto state = Api::Copy(owner, view);
        Require(outcome == OPENUSD_EDIT_APPLIED && ReadU32(state, 8) == 5,
            "Import publishes new target editing state.");
        Require(ReadU64(after, 12) != ReadU64(state, 12)
            && ReadU64(after, 20) != ReadU64(state, 20), "Import never patches old history IDs.");
        api.Ok(openusd_stage_edit_get_user_layer(stage, &review, &api.error));
        const auto imported = api.Capture(review, {weight});
        Require(Bytes(imported.begin() + 44, imported.end()) == Bytes(after.begin() + 44, after.end()),
            "New session reproduces exact review opinion, not weaker source.");
        std::ifstream file(rootPath, std::ios::binary);
        const std::string unchanged((std::istreambuf_iterator<char>(file)), {});
        Require(unchanged == original, "Review round trip preserves byte-for-byte source.");
        Require(!std::filesystem::exists(documentPath), "Native seam never publishes the user's file.");
        openusd_layer_release(review);
        openusd_stage_release(stage);
        ReviewProbe::FilesystemDomain(std::filesystem::path(argv[2]));
        ReviewProbe::AdditionalDependencies(std::filesystem::path(argv[2]));
        ReviewProbe::ResolverAnchors(std::filesystem::path(argv[2]));
        ReviewProbe::TypedExactness(std::filesystem::path(argv[2]));
        ReviewProbe::SourceFreshness(std::filesystem::path(argv[2]));
        ReviewProbe::ImportIsolation(std::filesystem::path(argv[2]));
        ReviewProbe::ImportRollback(std::filesystem::path(argv[2]));
        ReviewProbe::MalformedAndBounds(std::filesystem::path(argv[2]));
        ReviewProbe::InspectionOnly(std::filesystem::path(argv[2]));
        std::cout << "PASS portable review: verified filesystem open, CAS, capture/read, fresh-session import,"
            " exact authored value, receipt, unchanged source\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << "FAIL portable review: " << error.what() << '\n';
        return 1;
    }
}
