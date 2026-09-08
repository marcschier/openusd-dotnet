// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_dotnet.h"
#include "pxr/base/vt/arrayEditBuilder.h"
#include "pxr/usd/sdf/layer.h"
#include "pxr/usd/sdf/namespaceEdit.h"
#include "pxr/usd/usd/inherits.h"
#include "pxr/usd/usd/references.h"
#include "pxr/usd/usd/stage.h"
#include "pxr/usd/usdGeom/camera.h"
#include "pxr/usd/usdRender/settings.h"
#include "pxr/usd/usdRender/product.h"
#include "pxr/usd/usdRender/spec.h"
#include "pxr/usd/usdRender/var.h"

#include <filesystem>
#include <fstream>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
void Require(bool value, const char* message)
{
    if (!value) throw std::runtime_error(message);
}

std::string Bytes(const std::filesystem::path& path)
{
    std::ifstream input(path, std::ios::binary);
    return {std::istreambuf_iterator<char>(input), std::istreambuf_iterator<char>()};
}

void Run(const char* plugins, const std::filesystem::path& directory, const std::string& mode)
{
    std::filesystem::create_directories(directory);
    const bool crate = mode.rfind("usdc", 0) == 0;
    const std::string operation = mode.rfind("usdc-", 0) == 0 ? mode.substr(5) : mode;
    const auto path = directory / ("render-deferred-" + mode + (crate ? ".usdc" : ".usda"));
    Require(!std::filesystem::exists(path), "Refusing to overwrite a render input fixture.");
    char message[4096]{};
    openusd_error_buffer error{message, sizeof(message), 0};
    size_t registered = 0;
    Require(openusd_register_plugins(plugins, &registered, &error) == OPENUSD_STATUS_OK, message);
    {
        UsdStageRefPtr source = UsdStage::CreateNew(path.string());
        const auto camera = UsdGeomCamera::Define(source, SdfPath("/Camera"));
        camera.CreateHorizontalApertureAttr().Set(40.0f);
        camera.CreateVerticalApertureAttr().Set(20.0f);
        const auto settings = UsdRenderSettings::Define(source, SdfPath("/Settings"));
        settings.CreateResolutionAttr().Set(GfVec2i(800, 400));
        settings.GetResolutionAttr().Set(GfVec2i(1600, 900), UsdTimeCode(24));
        source->SetMetadata(TfToken("renderSettingsPrimPath"), std::string("/Settings"));
        settings.CreateCameraRel().SetTargets({camera.GetPath()});
        const auto product = UsdRenderProduct::Define(source, SdfPath("/Product"));
        product.CreateProductNameAttr().Set(TfToken("unchanged-output.exr"));
        settings.CreateProductsRel().SetTargets({product.GetPath()});
        const auto variable = UsdRenderVar::Define(source, SdfPath("/Beauty"));
        variable.CreateSourceNameAttr().Set(std::string("Ci"));
        product.CreateOrderedVarsRel().SetTargets({variable.GetPath()});
        const auto base = source->CreateClassPrim(SdfPath("/PurposeBase"));
        base.CreateAttribute(UsdRenderTokens->includedPurposes, SdfValueTypeNames->TokenArray)
            .Set(VtArray<TfToken>{TfToken("proxy"), TfToken("render")});
        settings.GetPrim().GetInherits().AddInherit(base.GetPath());
        if (operation == "array-edit" || operation.rfind("edit-", 0) == 0)
        {
            settings.CreateIncludedPurposesAttr();
            VtArrayEditBuilder<TfToken> builder;
            if (operation == "array-edit") builder.Append(TfToken("guide"));
            else if (operation == "edit-empty") builder.SetSize(0);
            else if (operation == "edit-oversized") builder.SetSize(8388608);
            else if (operation == "edit-peak") builder.SetSize(8388608).SetSize(2);
            else if (operation == "edit-out-of-range") builder.Write(TfToken("guide"), 999);
            else if (operation == "edit-modify")
                builder.Write(TfToken("proxy"), -1).Prepend(TfToken("guide")).EraseRef(1);
            else if (operation == "edit-compressed")
            {
                for (int index = 0; index < 32; ++index) builder.Write(TfToken("guide"), 0);
            }
            else if (operation == "edit-layered")
            {
                const auto middle = source->CreateClassPrim(SdfPath("/PurposeMiddle"));
                middle.GetInherits().AddInherit(base.GetPath());
                settings.GetPrim().GetInherits().SetInherits({middle.GetPath()});
                middle.CreateAttribute(UsdRenderTokens->includedPurposes, SdfValueTypeNames->TokenArray);
                VtArrayEditBuilder<TfToken> weakBuilder;
                weakBuilder.Prepend(TfToken("guide"));
                source->GetRootLayer()->SetField(SdfPath("/PurposeMiddle.includedPurposes"),
                    SdfFieldKeys->Default, VtValue(weakBuilder.FinalizeAndReset()));
                builder.Append(TfToken("default"));
            }
            else Require(operation == "edit-identity", "Unknown array-edit case.");
            auto edit = builder.FinalizeAndReset();
            source->GetRootLayer()->SetField(
                SdfPath("/Settings.includedPurposes"), SdfFieldKeys->Default, VtValue::Take(edit));
        }
        else if (operation == "ancestor-errors")
        {
            const auto ancestor = source->DefinePrim(SdfPath("/Container"));
            SdfBatchNamespaceEdit move;
            move.Add(SdfPath("/Settings"), SdfPath("/Container/Settings"));
            Require(source->GetRootLayer()->Apply(move), "Could not prepare the locally authored descendant.");
            ancestor.GetReferences().AddReference("missing-render-input-reference.usdc", SdfPath("/Absent"));
        }
        else if (operation == "absent-with-errors")
        {
            source->ClearMetadata(TfToken("renderSettingsPrimPath"));
            source->GetRootLayer()->SetSubLayerPaths({"missing-render-input-sublayer.usdc"});
        }
        else if (operation == "forwarded")
        {
            const auto relay = source->DefinePrim(SdfPath("/Relay"));
            const auto products = relay.CreateRelationship(TfToken("products"));
            const auto variables = relay.CreateRelationship(TfToken("vars"));
            const auto cameras = product.GetPrim().CreateRelationship(TfToken("forwardCamera"));
            products.SetTargets({product.GetPath()});
            variables.SetTargets({variable.GetPath()});
            cameras.SetTargets({camera.GetPath()});
            settings.GetProductsRel().SetTargets({products.GetPath()});
            product.GetOrderedVarsRel().SetTargets({variables.GetPath()});
            settings.GetCameraRel().SetTargets({cameras.GetPath()});
        }
        else Require(operation == "usda" || operation == "usdc", "Unknown deferred input case.");
        if (operation != "ancestor-errors")
            settings.GetPrim().CreateAttribute(TfToken("renderer:opaque"), SdfValueTypeNames->String)
                .Set(std::string("preserved, not evaluated"));
        source->GetRootLayer()->Save();
    }
    const auto original = Bytes(path);
    if (crate) Require(original.compare(0, 8, "PXR-USDC") == 0, "The USDC fixture is not actual crate data.");
    openusd_stage* raw = nullptr;
    Require(openusd_stage_open(path.string().c_str(), &raw, &error) == OPENUSD_STATUS_OK, message);
    std::unique_ptr<openusd_stage, decltype(&openusd_stage_release)> stage(raw, openusd_stage_release);
    uint64_t serial = 0;
    Require(openusd_stage_get_change_serial(stage.get(), &serial, &error) == OPENUSD_STATUS_OK, message);
    openusd_render_specification* owner = nullptr;
    openusd_render_specification_view view{};
    view.struct_size = sizeof(view);
    view.version = OPENUSD_RENDER_SPECIFICATION_VIEW_VERSION;
    const auto status = openusd_render_get_specification(stage.get(),
        operation == "forwarded" || operation == "absent-with-errors" ? nullptr :
            (operation == "ancestor-errors" ? "/Container/Settings" : "/Settings"), &owner, &view, &error);
    std::unique_ptr<openusd_render_specification, decltype(&openusd_render_specification_release)>
        result(owner, openusd_render_specification_release);
    if (operation == "edit-oversized" || operation == "edit-peak" || operation == "absent-with-errors" ||
        operation == "ancestor-errors")
    {
        Require(status == OPENUSD_STATUS_NATIVE_ERROR && owner == nullptr && view.has_settings == 0 &&
            view.products == nullptr && view.product_count == 0 && view.variables == nullptr &&
            view.variable_count == 0 && view.data == nullptr && view.data_size == 0,
            "Invalid or oversized native render input published partial outputs.");
        Require(std::string(message).find(operation == "absent-with-errors" || operation == "ancestor-errors" ?
            "composition" : "budget") != std::string::npos,
            "Array-edit refusal has no admission diagnostic.");
    }
    else
    {
    Require(status == OPENUSD_STATUS_OK, message);
    Require(view.has_settings == 1 && view.product_count == 1 && view.variable_count == 1 &&
        view.products[0].width == 800 && view.products[0].height == 400 &&
        view.products[0].aperture_width == 40 && view.products[0].aperture_height == 20,
        "Admitted render input lost native computation or framing.");
    const auto text = [&](size_t index) { return std::string(view.data + view.offsets[index]); };
    const size_t purposeCount = operation == "edit-empty" ? 0 :
        (operation == "array-edit" ? 3 : (operation == "edit-layered" ? 4 : 2));
    const bool guideFirst = operation == "edit-modify" || operation == "edit-compressed" ||
        operation == "edit-layered";
    Require(view.included_purpose_count == purposeCount &&
        (operation == "edit-empty" || (text(2) == (guideFirst ? "guide" : "proxy") &&
            text(3) == (operation == "edit-modify" || operation == "edit-layered" ? "proxy" : "render"))) &&
        (operation != "array-edit" || text(4) == "guide") &&
        (operation != "edit-layered" || (text(4) == "render" && text(5) == "default")),
        "Native array-edit composition did not preserve the expected purpose sequence.");
    Require(text(view.products[0].string_offset) == "/Product" &&
        text(view.products[0].string_offset + 3) == "/Camera" &&
        text(view.variables[0].string_offset) == "/Beauty" &&
        text(view.variables[0].string_offset + 2) == "Ci" &&
        view.products[0].render_var_index_count == 1 && view.render_var_indices[0] == 0,
        "Native forwarding or first-use variable identity changed.");
    const auto oracleStage = UsdStage::Open(path.string());
    const auto oracle = UsdRenderComputeSpec(UsdRenderSettings::Get(oracleStage, SdfPath("/Settings")), {TfToken()});
    Require(oracle.products.size() == 1 && oracle.renderVars.size() == 1 &&
        oracle.products[0].resolution == GfVec2i(view.products[0].width, view.products[0].height) &&
        oracle.products[0].cameraPath == SdfPath(text(view.products[0].string_offset + 3)) &&
        oracle.includedPurposes.size() == view.included_purpose_count,
        "Prepared result differs from native UsdRenderComputeSpec.");
    for (size_t index = 0; index < oracle.includedPurposes.size(); ++index)
        Require(oracle.includedPurposes[index].GetString() == text(index + 2),
            "Prepared purpose composition differs from the native oracle.");
    }
    uint64_t after = 0;
    Require(openusd_stage_get_change_serial(stage.get(), &after, &error) == OPENUSD_STATUS_OK &&
        after == serial && Bytes(path) == original, "Preparation changed stage identity/revision or source bytes.");
    stage.reset();
    result.reset();
    std::filesystem::remove(path);
    std::cout << "RENDER_DEFERRED_OK: " << mode << ", native spec, unchanged source, detached owner\n";
}
}

int main(int argc, char** argv)
{
    try
    {
        Require(argc == 4, "Expected plugin path, owned work directory and case.");
        Run(argv[1], argv[2], argv[3]);
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
