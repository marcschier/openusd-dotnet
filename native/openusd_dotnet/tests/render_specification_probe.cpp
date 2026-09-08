// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_dotnet.h"
#include "layer_edit_probe_support.h"

#include "pxr/base/vt/arrayEditBuilder.h"
#include "pxr/usd/sdf/attributeSpec.h"
#include "pxr/usd/sdf/data.h"
#include "pxr/usd/sdf/fileFormat.h"
#include "pxr/usd/sdf/listOp.h"
#include "pxr/usd/sdf/primSpec.h"
#include "pxr/usd/sdf/relationshipSpec.h"
#if __has_include("pxr/usd/sdf/storageAdmission.h")
#include "pxr/usd/sdf/storageAdmission.h"
#endif
#include "pxr/usd/usd/editContext.h"
#include "pxr/usd/usd/inherits.h"
#include "pxr/usd/usd/references.h"
#include "pxr/usd/usd/stage.h"
#include "pxr/usd/usd/variantSets.h"
#include "pxr/usd/usdGeom/camera.h"
#include "pxr/usd/usdRender/product.h"
#include "pxr/usd/usdRender/settings.h"
#include "pxr/usd/usdRender/spec.h"
#include "pxr/usd/usdRender/var.h"

#include <array>
#include <cstring>
#include <filesystem>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>

PXR_NAMESPACE_USING_DIRECTIVE

static_assert(offsetof(openusd_render_product_specification_record, string_offset) == 48);
static_assert(sizeof(openusd_render_product_specification_record) == 48 + 4 * sizeof(size_t));
static_assert(sizeof(openusd_render_variable_specification_record) == 2 * sizeof(size_t));
static_assert(sizeof(openusd_render_specification_view) == 16 + 17 * sizeof(size_t));

namespace
{
void Require(bool value, const char* message)
{
    if (!value)
    {
        throw std::runtime_error(message);
    }
}

struct Snapshot
{
    openusd_render_specification* owner = nullptr;
    openusd_render_specification_view view{};
    std::string errorMessage;

    Snapshot()
    {
        view.struct_size = sizeof(view);
        view.version = OPENUSD_RENDER_SPECIFICATION_VIEW_VERSION;
    }
    Snapshot(const Snapshot&) = delete;
    Snapshot& operator=(const Snapshot&) = delete;
    ~Snapshot() { openusd_render_specification_release(owner); }

    openusd_status Read(const openusd_stage* stage, const char* path)
    {
        char diagnostic[4096]{};
        openusd_error_buffer error{diagnostic, sizeof(diagnostic), 0};
        const openusd_status status = openusd_render_get_specification(stage, path, &owner, &view, &error);
        errorMessage = diagnostic;
        Require(status == OPENUSD_STATUS_OK || error.required > 1,
            "A failed render read did not produce an actionable diagnostic.");
        return status;
    }
    std::string String(size_t index) const
    {
        Require(index < view.string_count, "Native render string index is out of bounds.");
        return view.data + view.offsets[index];
    }
};

void CheckMalformedInputs(const openusd_stage* stage)
{
    char diagnostic[4096]{};
    openusd_error_buffer error{diagnostic, sizeof(diagnostic), 0};
    Snapshot wrongVersion;
    wrongVersion.view.version = 999;
    Require(wrongVersion.Read(stage, "/Settings") == OPENUSD_STATUS_INVALID_ARGUMENT &&
        wrongVersion.owner == nullptr && wrongVersion.view.has_settings == 0,
        "Unsupported view version did not fail with initialized outputs.");
    Snapshot noStage;
    Require(noStage.Read(nullptr, nullptr) == OPENUSD_STATUS_INVALID_ARGUMENT &&
        noStage.owner == nullptr && noStage.view.product_count == 0, "A null stage did not fail safely.");

    struct ShortView
    {
        uint32_t size = 8;
        uint32_t version = 1;
        uint64_t canary = UINT64_C(0x123456789abcdef0);
    } shortView;
    openusd_render_specification* owner = reinterpret_cast<openusd_render_specification*>(1);
    Require(openusd_render_get_specification(stage, "/Settings", &owner,
        reinterpret_cast<openusd_render_specification_view*>(&shortView), &error) == OPENUSD_STATUS_INVALID_ARGUMENT &&
        owner == nullptr && shortView.canary == UINT64_C(0x123456789abcdef0),
        "A short view overwrote its caller-advertised extent.");

    alignas(openusd_render_specification_view)
        std::array<unsigned char, sizeof(openusd_render_specification_view) + 1> misaligned{};
    Snapshot baseline;
    std::memcpy(misaligned.data() + 1, &baseline.view, sizeof(baseline.view));
    Require(openusd_render_get_specification(stage, "/Settings", &owner,
        reinterpret_cast<openusd_render_specification_view*>(misaligned.data() + 1), &error) ==
        OPENUSD_STATUS_INVALID_ARGUMENT && owner == nullptr, "A misaligned view was not rejected.");

    alignas(openusd_render_specification*)
        std::array<unsigned char, sizeof(openusd_render_specification*) + 1> misalignedOwner{};
    Require(openusd_render_get_specification(stage, "/Settings",
        reinterpret_cast<openusd_render_specification**>(misalignedOwner.data() + 1),
        &baseline.view, &error) == OPENUSD_STATUS_INVALID_ARGUMENT, "A misaligned owner output was not rejected.");
    Snapshot invalidUtf8;
    const char badPath[] = {'/', static_cast<char>(0xff), '\0'};
    Require(invalidUtf8.Read(stage, badPath) == OPENUSD_STATUS_NATIVE_ERROR && invalidUtf8.owner == nullptr,
        "Invalid UTF-8 input reached a render specification.");
    Snapshot oversized;
    const std::string hugePath(OPENUSD_RENDER_SPECIFICATION_MAX_STRING_BYTES, 'a');
    Require(oversized.Read(stage, hugePath.c_str()) == OPENUSD_STATUS_NATIVE_ERROR && oversized.owner == nullptr,
        "Oversized input did not fail the declared string budget.");
}

void CheckCachedForwardingDepth(const UsdStageRefPtr& source, const openusd_stage* stage)
{
    const UsdPrim forwarding = source->DefinePrim(SdfPath("/Forwarding"), TfToken("Scope"));
    for (size_t index = 0; index < 63; ++index)
    {
        const SdfPath target = index == 62 ? SdfPath("/Camera") :
            SdfPath("/Forwarding.camera" + std::to_string(index + 1));
        forwarding.CreateRelationship(TfToken("camera" + std::to_string(index))).SetTargets({target});
    }
    const UsdRenderSettings settings(source->GetPrimAtPath(SdfPath("/Settings")));
    const UsdRenderProduct product(source->GetPrimAtPath(SdfPath("/First")));
    settings.CreateCameraRel().SetTargets({SdfPath("/Forwarding.camera0")});
    product.CreateCameraRel().SetTargets({SdfPath("/Forwarding.camera0")});
    Snapshot atLimit;
    Require(atLimit.Read(stage, "/Settings") == OPENUSD_STATUS_OK,
        "A product reusing a validated 63-node tail was rejected at the 64-node depth limit.");
    product.CreateCameraRel().SetTargets({SdfPath("/Settings.camera")});
    Snapshot beyondLimit;
    Require(beyondLimit.Read(stage, "/Settings") == OPENUSD_STATUS_NATIVE_ERROR && beyondLimit.owner == nullptr,
        "A product prefix reused a cached 64-node camera tail without rejecting depth 65.");
    product.GetCameraRel().ClearTargets(true);
    settings.GetCameraRel().SetTargets({SdfPath("/Camera")});
}

void CompareWithUpstream(const Snapshot& snapshot, const UsdRenderSpec& expected)
{
    Require(snapshot.view.has_settings == 1 && snapshot.owner != nullptr, "A valid specification has no owner.");
    Require(snapshot.view.product_count == expected.products.size() &&
        snapshot.view.variable_count == expected.renderVars.size(), "Native product or variable counts changed.");
    for (size_t index = 0; index < expected.products.size(); ++index)
    {
        const auto& product = expected.products[index];
        const auto& actual = snapshot.view.products[index];
        Require(snapshot.String(actual.string_offset) == product.renderProductPath.GetString() &&
            snapshot.String(actual.string_offset + 1) == product.name.GetString() &&
            snapshot.String(actual.string_offset + 2) == product.type.GetString() &&
            snapshot.String(actual.string_offset + 3) == product.cameraPath.GetString() &&
            snapshot.String(actual.string_offset + 4) == product.aspectRatioConformPolicy.GetString(),
            "Native product identity changed.");
        Require(actual.width == product.resolution[0] && actual.height == product.resolution[1] &&
            actual.pixel_aspect_ratio == product.pixelAspectRatio &&
            actual.aperture_width == product.apertureSize[0] && actual.aperture_height == product.apertureSize[1] &&
            actual.data_window_min_x == product.dataWindowNDC.GetMin()[0] &&
            actual.data_window_min_y == product.dataWindowNDC.GetMin()[1] &&
            actual.data_window_max_x == product.dataWindowNDC.GetMax()[0] &&
            actual.data_window_max_y == product.dataWindowNDC.GetMax()[1] &&
            (actual.disable_motion_blur != 0) == product.disableMotionBlur &&
            (actual.disable_depth_of_field != 0) == product.disableDepthOfField,
            "Native conformance or inherited settings changed.");
        Require(actual.render_var_index_count == product.renderVarIndices.size(), "Ordered variables were omitted.");
        for (size_t variable = 0; variable < product.renderVarIndices.size(); ++variable)
        {
            Require(snapshot.view.render_var_indices[actual.render_var_index_offset + variable] ==
                product.renderVarIndices[variable], "Native variable ordering changed.");
        }
    }
    for (size_t index = 0; index < expected.renderVars.size(); ++index)
    {
        const auto& variable = expected.renderVars[index];
        const size_t field = snapshot.view.variables[index].string_offset;
        Require(snapshot.String(field) == variable.renderVarPath.GetString() &&
            snapshot.String(field + 1) == variable.dataType.GetString() &&
            snapshot.String(field + 2) == variable.sourceName &&
            snapshot.String(field + 3) == variable.sourceType.GetString(), "Variable metadata changed.");
    }
}

void CheckEmptyForwarding(const UsdStageRefPtr& source, const openusd_stage* stage)
{
    const UsdPrim forwarding = source->DefinePrim(SdfPath("/EmptyForwarding"), TfToken("Scope"));
    const UsdRelationship terminal = forwarding.CreateRelationship(TfToken("targetless"));
    const UsdRenderSettings settings(source->GetPrimAtPath(SdfPath("/Settings")));
    settings.GetProductsRel().SetTargets({terminal.GetPath()});
    Snapshot emptyProducts;
    Require(emptyProducts.Read(stage, "/Settings") == OPENUSD_STATUS_OK &&
        emptyProducts.view.has_settings == 1 && emptyProducts.view.product_count == 0,
        "Forwarding settings.products to an existing targetless relationship was rejected.");
    CompareWithUpstream(emptyProducts, UsdRenderComputeSpec(settings, {}));
    source->GetRootLayer()->SetField(terminal.GetPath(), SdfFieldKeys->TargetPaths, VtValue(SdfPathListOp()));
    SdfPathVector noTargets;
    Require(terminal.HasAuthoredTargets() && !terminal.GetTargets(&noTargets) && noTargets.empty(),
        "The authored no-effect forwarding fixture did not have clean false target semantics.");
    Snapshot noEffect;
    Require(noEffect.Read(stage, "/Settings") == OPENUSD_STATUS_OK &&
        noEffect.view.has_settings == 1 && noEffect.view.product_count == 0,
        "A clean authored no-effect forwarding opinion was confused with a composition error.");
    CompareWithUpstream(noEffect, UsdRenderComputeSpec(settings, {}));
    settings.GetProductsRel().SetTargets({SdfPath("/First"), SdfPath("/Second")});
    const UsdRenderProduct product(source->GetPrimAtPath(SdfPath("/First")));
    product.CreateCameraRel().SetTargets({terminal.GetPath()});
    Snapshot inheritedCamera;
    Require(inheritedCamera.Read(stage, "/Settings") == OPENUSD_STATUS_OK &&
        inheritedCamera.String(inheritedCamera.view.products[0].string_offset + 3) == "/Camera",
        "A targetless forwarded product camera did not inherit the settings camera.");
    CompareWithUpstream(inheritedCamera, UsdRenderComputeSpec(settings, {}));
    product.GetCameraRel().ClearTargets(true);
}

void CheckTargetCompositionErrors(const UsdStageRefPtr& source, const openusd_stage* stage)
{
    const UsdStageRefPtr asset = UsdStage::CreateInMemory();
    const UsdRenderSettings settings = UsdRenderSettings::Define(asset, SdfPath("/Asset/Settings"));
    UsdRenderProduct::Define(asset, SdfPath("/Asset/Product"));
    UsdGeomCamera::Define(asset, SdfPath("/Asset/Camera"));
    settings.CreateCameraRel().SetTargets({SdfPath("/Asset/Camera")});
    settings.CreateProductsRel().SetTargets({SdfPath("/Asset/Product"), SdfPath("/Outside")});
    Require(source->DefinePrim(SdfPath("/Reference")).GetReferences().AddReference(
        asset->GetRootLayer()->GetIdentifier(), SdfPath("/Asset")),
        "Could not reference the mixed valid/encapsulation-violating target fixture.");
    const UsdRenderSettings composed(source->GetPrimAtPath(SdfPath("/Reference/Settings")));
    SdfPathVector targets;
    Require(!composed.GetProductsRel().GetTargets(&targets) &&
        targets == SdfPathVector{SdfPath("/Reference/Product")},
        "The upstream reference fixture did not retain exactly one target with a composition error.");
    Snapshot rejected;
    Require(rejected.Read(stage, "/Reference/Settings") == OPENUSD_STATUS_NATIVE_ERROR &&
        rejected.owner == nullptr && rejected.view.has_settings == 0 &&
        rejected.view.products == nullptr && rejected.view.product_count == 0 &&
        rejected.view.variables == nullptr && rejected.view.variable_count == 0 &&
        rejected.view.render_var_indices == nullptr && rejected.view.render_var_index_count == 0 &&
        rejected.view.data == nullptr && rejected.view.data_size == 0 &&
        rejected.view.offsets == nullptr && rejected.view.string_count == 0,
        "A reference target composition error published a successful partial render specification.");
    Require(rejected.errorMessage.find("target-index composition") != std::string::npos,
        "The target composition failure was not distinguished from property-index errors.");
    composed.GetProductsRel().SetTargets({SdfPath("/Reference/Product")});
    Snapshot overridden;
    Require(overridden.Read(stage, "/Reference/Settings") == OPENUSD_STATUS_OK &&
        overridden.view.product_count == 1,
        "A stronger explicit target opinion did not mask the weaker encapsulation error.");
    CompareWithUpstream(overridden, UsdRenderComputeSpec(composed, {}));
    composed.GetProductsRel().SetTargets({});
    Snapshot blocked;
    Require(blocked.Read(stage, "/Reference/Settings") == OPENUSD_STATUS_OK &&
        blocked.view.has_settings == 1 && blocked.view.product_count == 0,
        "A stronger explicit empty target opinion was incorrectly rejected.");
    CompareWithUpstream(blocked, UsdRenderComputeSpec(composed, {}));
}

void CheckPropertyCompositionErrors(const UsdStageRefPtr& source, const openusd_stage* stage)
{
    const UsdPrim inherited = source->CreateClassPrim(SdfPath("/PropertyKindClass"));
    inherited.CreateAttribute(TfToken("products"), SdfValueTypeNames->Int).Set(1);
    const UsdRenderSettings settings = UsdRenderSettings::Define(source, SdfPath("/PropertyKindSettings"));
    settings.CreateCameraRel().SetTargets({SdfPath("/Camera")});
    settings.CreateProductsRel().SetTargets({SdfPath("/First")});
    Require(settings.GetPrim().GetInherits().AddInherit(inherited.GetPath()),
        "Could not author the relationship/attribute property-index conflict.");
    SdfPathVector targets;
    Require(!settings.GetProductsRel().GetTargets(&targets) && targets == SdfPathVector{SdfPath("/First")},
        "The upstream property-index fixture did not retain its target alongside the error.");
    Snapshot rejected;
    Require(rejected.Read(stage, "/PropertyKindSettings") == OPENUSD_STATUS_NATIVE_ERROR &&
        rejected.owner == nullptr && rejected.view.has_settings == 0 && rejected.view.product_count == 0 &&
        rejected.errorMessage.find("property-index composition") != std::string::npos,
        "A property-index composition error published a partial snapshot or was not classified structurally.");
}

void CheckForwardOnlyDefaults(const UsdStageRefPtr& source, const openusd_stage* stage)
{
    const UsdPrim forwarding = source->DefinePrim(SdfPath("/ForwardOnly"), TfToken("Scope"));
    Require(forwarding.CreateAttribute(TfToken("resolution"), SdfValueTypeNames->Int).Set(1),
        "Could not author the non-render forwarding prim's custom resolution.");
    const UsdRelationship terminal = forwarding.CreateRelationship(TfToken("targetless"));
    const UsdRenderSettings settings(source->GetPrimAtPath(SdfPath("/Settings")));
    settings.GetProductsRel().SetTargets({terminal.GetPath()});
    Snapshot snapshot;
    Require(snapshot.Read(stage, "/Settings") == OPENUSD_STATUS_OK &&
        snapshot.owner != nullptr && snapshot.view.has_settings == 1 && snapshot.view.product_count == 0 &&
        snapshot.view.variable_count == 0 && snapshot.view.render_var_index_count == 0,
        "A forward-only Scope's custom scalar resolution was incorrectly validated as a render default.");
    CompareWithUpstream(snapshot, UsdRenderComputeSpec(settings, {}));
    settings.GetProductsRel().SetTargets({SdfPath("/First"), SdfPath("/Second")});
}

void CheckMergedRoleRequirements(const UsdStageRefPtr& source, const openusd_stage* stage)
{
    const UsdRenderSettings settings(source->GetPrimAtPath(SdfPath("/Settings")));
    const UsdRenderProduct product(source->GetPrimAtPath(SdfPath("/First")));
    const UsdRelationship forwarding = product.GetPrim().CreateRelationship(TfToken("forwardCamera"));
    Require(forwarding.SetTargets({SdfPath("/Camera")}) &&
        settings.GetCameraRel().SetTargets({forwarding.GetPath()}) &&
        product.CreateResolutionAttr().Set(GfVec2i(320, 160)) &&
        product.GetPrim().CreateAttribute(TfToken("sourceName"), SdfValueTypeNames->Int).Set(7),
        "Could not author the forwarding/product dual-role fixture.");
    Snapshot valid;
    Require(valid.Read(stage, "/Settings") == OPENUSD_STATUS_OK && valid.view.product_count == 2 &&
        valid.view.products[0].width == 320 && valid.view.products[0].height == 160,
        "A prim first admitted for forwarding did not subsequently produce its valid render product.");
    CompareWithUpstream(valid, UsdRenderComputeSpec(settings, {}));

    source->GetRootLayer()->SetField(product.GetResolutionAttr().GetPath(), SdfFieldKeys->Default, VtValue(1));
    Snapshot invalid;
    Require(invalid.Read(stage, "/Settings") == OPENUSD_STATUS_NATIVE_ERROR &&
        invalid.owner == nullptr && invalid.view.has_settings == 0 && invalid.view.product_count == 0 &&
        invalid.errorMessage.find("concrete schema-typed default opinions") != std::string::npos,
        "Forward-only admission exempted a later product role from concrete-default admission.");
    product.GetResolutionAttr().Clear();
    settings.GetCameraRel().SetTargets({SdfPath("/Camera")});
}

void CheckVariantSourceSites(const UsdStageRefPtr& source, const openusd_stage* stage)
{
    const UsdPrim asset = source->DefinePrim(SdfPath("/VariantAsset"), TfToken("Scope"));
    UsdVariantSet preset = asset.GetVariantSets().AddVariantSet("preset");
    Require(preset.AddVariant("main") && preset.SetVariantSelection("main"),
        "Could not select the ancestor variant fixture.");
    {
        const UsdEditContext context = preset.GetVariantEditContext();
        const UsdRenderSettings settings = UsdRenderSettings::Define(source, SdfPath("/VariantAsset/Settings"));
        UsdRenderProduct::Define(source, SdfPath("/VariantAsset/Product"));
        UsdGeomCamera::Define(source, SdfPath("/VariantAsset/Camera"));
        settings.CreateCameraRel().SetTargets({SdfPath("/VariantAsset/Camera")});
        settings.CreateProductsRel().SetTargets({SdfPath("/VariantAsset/Product")});
        UsdVariantSet framing = settings.GetPrim().GetVariantSets().AddVariantSet("framing");
        Require(framing.AddVariant("wide") && framing.SetVariantSelection("wide"),
            "Could not select the nested leaf variant fixture.");
        const UsdEditContext nested = framing.GetVariantEditContext();
        settings.CreateResolutionAttr().Set(GfVec2i(960, 540));
    }
    Require(source->DefinePrim(SdfPath("/VariantReference")).GetReferences().AddInternalReference(asset.GetPath()),
        "Could not reference the variant-bearing source sites.");
    const UsdRenderSettings composed(source->GetPrimAtPath(SdfPath("/VariantReference/Settings")));
    Snapshot snapshot;
    Require(snapshot.Read(stage, "/VariantReference/Settings") == OPENUSD_STATUS_OK &&
        snapshot.view.product_count == 1 && snapshot.view.products[0].width == 960 &&
        snapshot.view.products[0].height == 540,
        "Ordinary referenced ancestor/leaf variant source sites were rejected or lost their opinions.");
    CompareWithUpstream(snapshot, UsdRenderComputeSpec(composed, {}));
    Require(source->GetPrimAtPath(SdfPath("/VariantReference")).SetInstanceable(true),
        "Could not make the variant reference instanceable.");
    const UsdPrim instance = source->DefinePrim(SdfPath("/VariantInstance"));
    Require(instance.GetReferences().AddInternalReference(asset.GetPath()) && instance.SetInstanceable(true),
        "Could not create the second instance sharing variant-bearing source sites.");
    const UsdRenderSettings instanceSettings(source->GetPrimAtPath(SdfPath("/VariantInstance/Settings")));
    Snapshot instanceSnapshot;
    Require(instanceSnapshot.Read(stage, "/VariantInstance/Settings") == OPENUSD_STATUS_OK &&
        instanceSnapshot.view.product_count == 1 &&
        instanceSnapshot.String(instanceSnapshot.view.products[0].string_offset) == "/VariantInstance/Product" &&
        instanceSnapshot.String(instanceSnapshot.view.products[0].string_offset + 3) == "/VariantInstance/Camera",
        "Structural target checks lost Usd's instance-proxy target mapping.");
    CompareWithUpstream(instanceSnapshot, UsdRenderComputeSpec(instanceSettings, {}));
}

void CheckDeferredBackingStore(const UsdStageRefPtr& source, const char* stagePath)
{
    const std::string cratePath = std::string(stagePath) + ".usdc";
    Require(!std::filesystem::exists(cratePath), "The render probe refuses to overwrite its crate fixture.");
    Require(source->GetRootLayer()->Export(cratePath), "Could not export the deferred crate fixture.");
    char diagnostic[4096]{};
    openusd_error_buffer error{diagnostic, sizeof(diagnostic), 0};
    openusd_stage* rawStage = nullptr;
    Require(openusd_stage_open(cratePath.c_str(), &rawStage, &error) == OPENUSD_STATUS_OK, diagnostic);
    std::unique_ptr<openusd_stage, decltype(&openusd_stage_release)> stage(rawStage, openusd_stage_release);
    Snapshot absent;
    Require(absent.Read(stage.get(), nullptr) == OPENUSD_STATUS_OK && absent.owner == nullptr,
        "A known crate store with no authored default did not preserve absent-default semantics.");
    Snapshot inspected;
#if defined(SDF_STORAGE_ADMISSION_VERSION)
    Require(SdfStorageAdmissionScope::GetApiVersion() == 1 &&
        inspected.Read(stage.get(), "/Settings") == OPENUSD_STATUS_OK,
        "The pinned storage-admission SDK did not prepare the actual crate specification.");
    CompareWithUpstream(inspected,
        UsdRenderComputeSpec(UsdRenderSettings::Get(source, SdfPath("/Settings")), {}));
#else
    Require(inspected.Read(stage.get(), "/Settings") == OPENUSD_STATUS_NATIVE_ERROR &&
        inspected.owner == nullptr && inspected.view.product_count == 0,
        "An uninspectable deferred payload was materialized instead of failing closed.");
#endif
    stage.reset();
    Require(std::filesystem::remove(cratePath), "Could not remove the deferred crate fixture.");
}

class UnapprovedResidentData final : public SdfData
{
};

struct ReviewStoreProbeAccess : SdfFileFormat
{
    static void Install(SdfLayer* layer, SdfAbstractDataRefPtr data)
    {
        _SetLayerData(layer, data);
    }
};

void CheckOwnedReviewStore(const UsdStageRefPtr& source, const openusd_stage* stage)
{
    EditProbe::Api edits;
    openusd_layer* rawReview = nullptr;
    openusd_layer* rawSession = nullptr;
    edits.Ok(openusd_stage_edit_get_user_layer(stage, &rawReview, &edits.error));
    edits.Ok(openusd_stage_get_session_layer(stage, &rawSession, &edits.error));
    std::unique_ptr<openusd_layer, decltype(&openusd_layer_release)> review(rawReview, openusd_layer_release);
    std::unique_ptr<openusd_layer, decltype(&openusd_layer_release)> session(rawSession, openusd_layer_release);
    const auto reviewLayer = edits.Native(review.get());
    const auto sessionLayer = edits.Native(session.get());
    const auto composed = UsdStage::Open(source->GetRootLayer(), sessionLayer);
    const UsdRenderSettings settings(composed->GetPrimAtPath(SdfPath("/Settings")));
    Snapshot empty;
    Require(empty.Read(stage, "/Settings") == OPENUSD_STATUS_OK,
        "An empty owned resident review store was rejected by render admission.");
    CompareWithUpstream(empty, UsdRenderComputeSpec(settings, {}));

    const EditProbe::Address aperture{"/Camera.horizontalAperture"};
    const auto before = edits.Capture(review.get(), {aperture});
    EditProbe::Bytes value;
    EditProbe::U32(value, 5);
    float width = 80;
    uint32_t bits = 0;
    std::memcpy(&bits, &width, sizeof(bits));
    EditProbe::U32(value, bits);
    const auto after = edits.Apply(review.get(), before, {{aperture, 1, "float", 0, 0, value}});
    Snapshot edited;
    Require(edited.Read(stage, "/Settings") == OPENUSD_STATUS_OK &&
        edited.view.products[0].aperture_width == 80,
        "A native CAS-authored review aperture was not admitted/evaluated.");
    CompareWithUpstream(edited, UsdRenderComputeSpec(settings, {}));
    Require(source->GetRootLayer()->GetField(SdfPath(aperture.path), SdfFieldKeys->Default) == VtValue(40.0f),
        "Review-camera admission changed the source aperture opinion.");
    edits.Restore(review.get(), after, before);

    const auto reviewSettings = SdfCreatePrimInLayer(reviewLayer, SdfPath("/Settings"));
    const auto purpose = SdfAttributeSpec::New(
        reviewSettings, "includedPurposes", SdfValueTypeNames->TokenArray, SdfVariabilityUniform);
    VtArrayEditBuilder<TfToken> builder;
    builder.SetSize(8388608);
    auto edit = builder.FinalizeAndReset();
    reviewLayer->SetField(purpose->GetPath(), SdfFieldKeys->Default, VtValue::Take(edit));
    Snapshot deferred;
    Require(deferred.Read(stage, "/Settings") == OPENUSD_STATUS_NATIVE_ERROR &&
        deferred.owner == nullptr && deferred.errorMessage.find("budget") != std::string::npos,
        "Owned review identity bypassed bounded array-edit admission.");
    reviewLayer->GetPseudoRoot()->RemoveNameChild(reviewSettings);

    const auto scope = SdfCreatePrimInLayer(reviewLayer, SdfPath("/ReviewForward"));
    scope->SetTypeName("Scope");
    SdfAttributeSpec::New(scope, "resolution", SdfValueTypeNames->Int)->SetDefaultValue(VtValue(1));
    const auto targetless = SdfRelationshipSpec::New(scope, "targetless");
    const auto forwardedSettings = SdfCreatePrimInLayer(reviewLayer, SdfPath("/Settings"));
    const auto products = SdfRelationshipSpec::New(forwardedSettings, "products");
    reviewLayer->SetField(products->GetPath(), SdfFieldKeys->TargetPaths,
        VtValue(SdfPathListOp::CreateExplicit({targetless->GetPath()})));
    Snapshot forwarding;
    Require(forwarding.Read(stage, "/Settings") == OPENUSD_STATUS_OK &&
        forwarding.view.has_settings == 1 && forwarding.view.product_count == 0,
        "Owned review-store admission changed role-specific targetless forwarding.");
    CompareWithUpstream(forwarding, UsdRenderComputeSpec(settings, {}));
    reviewLayer->GetPseudoRoot()->RemoveNameChild(forwardedSettings);
    reviewLayer->GetPseudoRoot()->RemoveNameChild(scope);

    const auto foreign = SdfLayer::CreateAnonymous("unapproved-store.usda");
    SdfAbstractDataRefPtr foreignData = TfCreateRefPtr(new UnapprovedResidentData);
    foreignData->CreateSpec(SdfPath::AbsoluteRootPath(), SdfSpecTypePseudoRoot);
    ReviewStoreProbeAccess::Install(get_pointer(foreign), foreignData);
    SdfCreatePrimInLayer(foreign, SdfPath("/Camera"));
    sessionLayer->InsertSubLayerPath(foreign->GetIdentifier(), 0);
    Snapshot unapproved;
    Require(unapproved.Read(stage, "/Settings") == OPENUSD_STATUS_NATIVE_ERROR &&
        unapproved.owner == nullptr &&
        unapproved.errorMessage.find("UnapprovedResidentData") != std::string::npos,
        "Render admission broadened to an arbitrary SdfData subclass.");
    sessionLayer->RemoveSubLayerPath(0);
}

void Run(const char* pluginPath, const char* stagePath, const std::string& mode)
{
    Require(openusd_get_abi_version() == OPENUSD_DATA_ABI_VERSION,
        "The render probe loaded a DLL with a different public data ABI.");
    char diagnostic[4096]{};
    openusd_error_buffer error{diagnostic, sizeof(diagnostic), 0};
    size_t plugins = 0;
    Require(openusd_register_plugins(pluginPath, &plugins, &error) == OPENUSD_STATUS_OK, diagnostic);
    Require(!std::filesystem::exists(stagePath), "The render probe refuses to overwrite an existing stage.");
    UsdStageRefPtr source = UsdStage::CreateNew(stagePath);
    Require(static_cast<bool>(source), "Could not create the render probe stage.");
    const UsdGeomCamera camera = UsdGeomCamera::Define(source, SdfPath("/Camera"));
    camera.CreateHorizontalApertureAttr().Set(40.0f);
    camera.CreateVerticalApertureAttr().Set(20.0f);
    const UsdRenderSettings settings = UsdRenderSettings::Define(source, SdfPath("/Settings"));
    settings.CreateResolutionAttr().Set(GfVec2i(800, 400));
    settings.CreateCameraRel().SetTargets({SdfPath("/Camera")});
    settings.CreateDisableMotionBlurAttr().Set(true);
    settings.CreateDataWindowNDCAttr().Set(GfVec4f(-0.25f, 0.125f, 1.25f, 0.875f));
    UsdRenderSettings::Define(source, SdfPath("/Empty"));
    const UsdRenderProduct first = UsdRenderProduct::Define(source, SdfPath("/First"));
    const UsdRenderProduct second = UsdRenderProduct::Define(source, SdfPath("/Second"));
    first.CreateProductNameAttr().Set(TfToken("first.exr"));
    second.CreateProductNameAttr().Set(TfToken("second.exr"));
    second.CreateResolutionAttr().Set(GfVec2i(400, 400));
    second.CreateAspectRatioConformPolicyAttr().Set(UsdRenderTokens->cropAperture);
    settings.CreateProductsRel().SetTargets({first.GetPath(), second.GetPath()});
    const UsdRenderVar beauty = UsdRenderVar::Define(source, SdfPath("/Beauty"));
    beauty.CreateSourceNameAttr().Set(std::string("Ci"));
    const UsdRenderVar unsupported = UsdRenderVar::Define(source, SdfPath("/Unsupported"));
    unsupported.CreateSourceTypeAttr().Set(TfToken("lpe"));
    unsupported.CreateSourceNameAttr().Set(std::string("C<RD>L"));
    first.CreateOrderedVarsRel().SetTargets({beauty.GetPath(), unsupported.GetPath()});
    second.CreateOrderedVarsRel().SetTargets({unsupported.GetPath(), beauty.GetPath()});
    source->GetRootLayer()->Save();

    openusd_stage* rawStage = nullptr;
    Require(openusd_stage_open(stagePath, &rawStage, &error) == OPENUSD_STATUS_OK, diagnostic);
    std::unique_ptr<openusd_stage, decltype(&openusd_stage_release)> stage(rawStage, openusd_stage_release);
    if (mode != "all")
    {
        if (mode == "target-composition")
        {
            CheckTargetCompositionErrors(source, stage.get());
        }
        else if (mode == "property-composition")
        {
            CheckPropertyCompositionErrors(source, stage.get());
        }
        else if (mode == "forward-only")
        {
            CheckForwardOnlyDefaults(source, stage.get());
        }
        else if (mode == "dual-role")
        {
            CheckMergedRoleRequirements(source, stage.get());
        }
        else if (mode == "variant-sites")
        {
            CheckVariantSourceSites(source, stage.get());
        }
        else if (mode == "review-store")
        {
            CheckOwnedReviewStore(source, stage.get());
        }
        else
        {
            throw std::invalid_argument("Unknown render specification probe case.");
        }
        return;
    }
    Snapshot absent;
    Require(absent.Read(stage.get(), nullptr) == OPENUSD_STATUS_OK && absent.owner == nullptr &&
        absent.view.has_settings == 0, "An unauthored default was not absent.");
    Snapshot empty;
    Require(empty.Read(stage.get(), "/Empty") == OPENUSD_STATUS_OK && empty.view.has_settings == 1 &&
        empty.view.product_count == 0, "Empty settings fabricated an output.");
    CheckMalformedInputs(stage.get());
    CheckCachedForwardingDepth(source, stage.get());
    CheckEmptyForwarding(source, stage.get());
    CheckForwardOnlyDefaults(source, stage.get());
    CheckMergedRoleRequirements(source, stage.get());
    CheckVariantSourceSites(source, stage.get());
    CheckTargetCompositionErrors(source, stage.get());
    CheckPropertyCompositionErrors(source, stage.get());
    CheckDeferredBackingStore(source, stagePath);
    CheckOwnedReviewStore(source, stage.get());
    source->SetMetadata(TfToken("renderSettingsPrimPath"), std::string("/Settings"));
    Snapshot actual;
    Require(actual.Read(stage.get(), nullptr) == OPENUSD_STATUS_OK, "The authored default failed.");
    CompareWithUpstream(actual, UsdRenderComputeSpec(settings, {}));
    Require(actual.view.products[1].aperture_width == 20 && actual.view.products[1].aperture_height == 20,
        "The worked cropAperture example was not conformed by native USD.");
    source->SetMetadata(TfToken("renderSettingsPrimPath"), std::string("/Missing"));
    Snapshot badDefault;
    Require(badDefault.Read(stage.get(), nullptr) == OPENUSD_STATUS_NOT_FOUND && badDefault.owner == nullptr,
        "An invalid authored default was reported as absent success.");
    stage.reset();
    source.Reset();
    Require(actual.String(0) == "/Settings" && actual.String(actual.view.variables[1].string_offset + 2) == "C<RD>L",
        "A snapshot did not survive stage release.");
}
}

int main(int argc, char** argv)
{
    if (argc != 3 && argc != 4)
    {
        std::cerr << "Usage: openusd_render_specification_probe <plugin-path> <new-stage-path> [case]\n";
        return 2;
    }
    try
    {
        const std::string mode = argc == 4 ? argv[3] : "all";
        Run(argv[1], argv[2], mode);
        Require(std::filesystem::remove(argv[2]), "The render probe could not remove its stage.");
        std::cout << "RENDER_SPECIFICATION_NATIVE_OK: ABI " << openusd_get_abi_version()
            << ", view " << OPENUSD_RENDER_SPECIFICATION_VIEW_VERSION
            << ", case " << mode;
        if (mode == "all")
        {
            std::cout << ", upstream equivalence, ownership, malformed input, cached depth, "
                         "structured composition errors, clean no-effect targets, role merging, variant/instance sources";
        }
        std::cout << '\n';
        return 0;
    }
    catch (const std::exception& exception)
    {
        std::cerr << exception.what() << '\n';
        return 1;
    }
}
