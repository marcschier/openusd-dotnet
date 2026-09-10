// Copyright (c) marcschier. Licensed under the MIT License.

#include "warehouse_localization.h"

#include <pxr/base/js/json.h>
#include <pxr/base/plug/registry.h>
#include <pxr/base/tf/errorMark.h>
#include <pxr/usd/pcp/errors.h>
#include <pxr/usd/sdf/assetPath.h>
#include <pxr/usd/sdf/layer.h>
#include <pxr/usd/sdf/listOp.h>
#include <pxr/usd/usd/primRange.h>
#include <pxr/usd/usd/stage.h>
#include <pxr/usd/usdGeom/bboxCache.h>
#include <pxr/usd/usdGeom/mesh.h>
#include <pxr/usd/usdGeom/metrics.h>
#include <pxr/usd/usdGeom/pointInstancer.h>
#include <pxr/usd/usdGeom/tokens.h>
#include <pxr/usd/usdLux/lightAPI.h>
#include <pxr/usd/usdPhysics/collisionAPI.h>
#include <pxr/usd/usdPhysics/meshCollisionAPI.h>
#include <pxr/usd/usdPhysics/metrics.h>
#include <pxr/usd/usdPhysics/rigidBodyAPI.h>
#include <pxr/usd/usdShade/material.h>
#include <pxr/usd/usdShade/shader.h>
#include <pxr/usd/usdUtils/dependencies.h>

#include <chrono>
#include <cmath>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <map>
#include <set>
#include <stdexcept>
#include <string>
#include <vector>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
constexpr size_t MaximumPrims = 1000000;
constexpr size_t MaximumRecords = 200000;
constexpr size_t MaximumTextBytes = 64u * 1024u * 1024u;

struct Audit
{
    std::map<std::string, uint64_t> counts{
        {"rigidBodyPrims", 0}, {"colliderPrims", 0}, {"pointInstances", 0},
        {"timeSampledAttributes", 0}, {"nonzeroVisibleLights", 0}};
    std::map<std::string, uint64_t> types;
    std::map<std::string, uint64_t> schemas;
    std::map<std::string, uint64_t> physics_properties;
    JsArray colliders;
    JsArray shaders;
    JsArray materials;
    JsArray lights;
    JsArray animated;
    JsArray instances;
    JsArray errors;
    size_t text_bytes = 0;

    JsValue Text(const std::string& text)
    {
        if (text.size() > MaximumTextBytes - text_bytes)
        {
            throw std::runtime_error("Warehouse audit exceeded its 64 MiB report-text bound.");
        }
        text_bytes += text.size();
        return JsValue(text);
    }

    void Add(JsArray& values, JsObject value)
    {
        if (values.size() >= MaximumRecords)
        {
            throw std::runtime_error("Warehouse audit exceeded its bounded record count.");
        }
        values.emplace_back(std::move(value));
    }
};

JsValue Counts(const std::map<std::string, uint64_t>& values)
{
    JsObject result;
    for (const auto& item : values)
    {
        result[item.first] = JsValue(item.second);
    }
    return JsValue(std::move(result));
}

JsValue Vector(const GfVec3d& value)
{
    JsArray result;
    for (size_t index = 0; index < 3; ++index)
    {
        if (!std::isfinite(value[index]))
        {
            throw std::runtime_error("Warehouse bounds contain non-finite coordinates.");
        }
        result.emplace_back(value[index]);
    }
    return JsValue(std::move(result));
}

void InspectProperties(const UsdPrim& prim, Audit& result)
{
    const auto attributes = prim.GetAuthoredAttributes();
    if (attributes.size() > MaximumRecords)
    {
        throw std::runtime_error("Warehouse prim exceeds the audit property-count bound.");
    }
    for (const UsdAttribute& attribute : attributes)
    {
        const std::string name = attribute.GetName().GetString();
        if (name.find("physics:") != std::string::npos ||
            name.compare(0, 5, "physx") == 0 ||
            name.compare(0, 15, "openUsdPhysics:") == 0)
        {
            ++result.physics_properties[name];
        }
        const size_t samples = attribute.GetNumTimeSamples();
        if (samples != 0)
        {
            ++result.counts["timeSampledAttributes"];
            result.Add(result.animated, {
                {"path", result.Text(attribute.GetPath().GetString())},
                {"type", result.Text(attribute.GetTypeName().GetAsToken().GetString())},
                {"samples", JsValue(static_cast<uint64_t>(samples))}
            });
        }
    }
}

void InspectShader(const UsdPrim& prim, Audit& result)
{
    UsdShadeShader shader(prim);
    if (!shader)
    {
        return;
    }
    TfToken identifier;
    TfToken implementation;
    shader.GetIdAttr().Get(&identifier);
    shader.GetImplementationSourceAttr().Get(&implementation);
    SdfAssetPath asset;
    TfToken subidentifier;
    const bool mdl = shader.GetSourceAsset(&asset, TfToken("mdl"));
    shader.GetSourceAssetSubIdentifier(&subidentifier, TfToken("mdl"));
    JsArray inputs;
    for (const UsdShadeInput& input : shader.GetInputs())
    {
        JsObject value{
            {"name", result.Text(input.GetBaseName().GetString())},
            {"type", result.Text(input.GetTypeName().GetAsToken().GetString())},
            {"authoredValue", JsValue(input.GetAttr().HasAuthoredValueOpinion())},
            {"connected", JsValue(input.HasConnectedSource())}
        };
        if (input.GetTypeName() == SdfValueTypeNames->Asset)
        {
            SdfAssetPath path;
            if (input.Get(&path))
            {
                value["asset"] = result.Text(path.GetAssetPath());
                value["resolvedAsset"] = result.Text(path.GetResolvedPath());
            }
        }
        inputs.emplace_back(std::move(value));
    }
    result.Add(result.shaders, {
        {"path", result.Text(prim.GetPath().GetString())},
        {"id", result.Text(identifier.GetString())},
        {"implementationSource", result.Text(implementation.GetString())},
        {"hasMdlSource", JsValue(mdl)},
        {"mdlAsset", result.Text(asset.GetAssetPath())},
        {"mdlResolvedAsset", result.Text(asset.GetResolvedPath())},
        {"mdlSubidentifier", result.Text(subidentifier.GetString())},
        {"inputs", JsValue(std::move(inputs))}
    });
}

void InspectLight(const UsdPrim& prim, Audit& result)
{
    const UsdLuxLightAPI light(prim);
    if (!light)
    {
        return;
    }
    float intensity = 0;
    float exposure = 0;
    bool normalized = false;
    GfVec3f color;
    const bool resolved = light.GetIntensityAttr().Get(&intensity) &&
        light.GetExposureAttr().Get(&exposure) && light.GetColorAttr().Get(&color) &&
        light.GetNormalizeAttr().Get(&normalized);
    const UsdGeomImageable imageable(prim);
    const TfToken visibility = imageable ? imageable.ComputeVisibility() : TfToken();
    const double scale = static_cast<double>(intensity) * std::exp2(static_cast<double>(exposure));
    if (!resolved || !std::isfinite(scale) || !std::isfinite(color[0]) ||
        !std::isfinite(color[1]) || !std::isfinite(color[2]))
    {
        result.errors.emplace_back(result.Text("Unresolved or non-finite light: " + prim.GetPath().GetString()));
        return;
    }
    result.counts["nonzeroVisibleLights"] += scale != 0 &&
        (color[0] != 0 || color[1] != 0 || color[2] != 0) &&
        visibility != UsdGeomTokens->invisible ? 1 : 0;
    result.Add(result.lights, {
        {"path", result.Text(prim.GetPath().GetString())},
        {"type", result.Text(prim.GetTypeName().GetString())},
        {"intensity", JsValue(static_cast<double>(intensity))},
        {"exposure", JsValue(static_cast<double>(exposure))},
        {"intensityExposureScale", JsValue(scale)},
        {"color", Vector(GfVec3d(color))},
        {"normalized", JsValue(normalized)},
        {"visibility", result.Text(visibility.GetString())}
    });
}

Audit Inspect(const UsdStageRefPtr& stage)
{
    Audit result;
    std::set<SdfPath> inspected_definitions;
    for (const UsdPrim& prim : stage->TraverseAll())
    {
        if (++result.counts["hierarchyPrimsWithoutPrototypesOrProxies"] > MaximumPrims)
        {
            throw std::runtime_error("Warehouse hierarchy exceeds the audit prim bound.");
        }
        result.counts["inactivePrims"] += prim.IsActive() ? 0 : 1;
        result.counts["unloadedPayloads"] +=
            prim.IsActive() && prim.HasPayload() && !prim.IsLoaded() ? 1 : 0;
        result.counts["inactivePayloads"] += !prim.IsActive() && prim.HasPayload() ? 1 : 0;
    }
    result.counts["prototypes"] = stage->GetPrototypes().size();
    for (const UsdPrim& prim : UsdPrimRange::Stage(stage, UsdTraverseInstanceProxies()))
    {
        if (++result.counts["activeDefinedPrimsIncludingProxies"] > MaximumPrims)
        {
            throw std::runtime_error("Warehouse expanded instances exceed the audit prim bound.");
        }
        ++result.types[prim.GetTypeName().GetString()];
        result.counts["instanceProxies"] += prim.IsInstanceProxy() ? 1 : 0;
        result.counts["payloadPlacements"] += prim.HasPayload() ? 1 : 0;
        if (prim.IsInstance())
        {
            ++result.counts["instances"];
            result.Add(result.instances, {
                {"path", result.Text(prim.GetPath().GetString())},
                {"prototype", result.Text(prim.GetPrototype().GetPath().GetString())}
            });
        }
        SdfTokenListOp schemas;
        if (prim.GetMetadata(TfToken("apiSchemas"), &schemas))
        {
            for (const TfToken& schema : schemas.GetAppliedItems())
            {
                ++result.schemas[schema.GetString()];
            }
        }
        if (prim.HasAPI<UsdPhysicsRigidBodyAPI>())
        {
            ++result.counts["rigidBodyPrims"];
        }
        if (prim.HasAPI<UsdPhysicsCollisionAPI>())
        {
            ++result.counts["colliderPrims"];
            bool enabled = false;
            const bool resolved = UsdPhysicsCollisionAPI(prim).GetCollisionEnabledAttr().Get(&enabled);
            TfToken approximation;
            UsdPhysicsMeshCollisionAPI mesh(prim);
            if (mesh)
            {
                mesh.GetApproximationAttr().Get(&approximation);
            }
            result.Add(result.colliders, {
                {"path", result.Text(prim.GetPath().GetString())},
                {"type", result.Text(prim.GetTypeName().GetString())},
                {"enabledResolved", JsValue(resolved)},
                {"enabled", JsValue(enabled)},
                {"instanceProxy", JsValue(prim.IsInstanceProxy())},
                {"approximation", result.Text(approximation.GetString())}
            });
        }
        InspectLight(prim, result);
        const UsdPrim definition = prim.IsInstanceProxy() ? prim.GetPrimInPrototype() : prim;
        if (!inspected_definitions.insert(definition.GetPath()).second)
        {
            continue;
        }
        InspectProperties(definition, result);
        InspectShader(definition, result);
        if (UsdShadeMaterial material{definition})
        {
            JsArray outputs;
            for (const auto& output : material.GetSurfaceOutputs())
            {
                SdfPathVector connections;
                output.GetAttr().GetConnections(&connections);
                JsArray targets;
                for (const auto& connection : connections)
                {
                    targets.emplace_back(result.Text(connection.GetString()));
                }
                outputs.emplace_back(JsObject{
                    {"name", result.Text(output.GetFullName().GetString())},
                    {"authored", JsValue(output.GetAttr().HasAuthoredConnections())},
                    {"connections", JsValue(std::move(targets))}
                });
            }
            result.Add(result.materials, {
                {"path", result.Text(definition.GetPath().GetString())},
                {"surfaceOutputs", JsValue(std::move(outputs))}
            });
        }
        if (UsdGeomMesh mesh{definition})
        {
            VtVec3fArray points;
            VtIntArray counts;
            VtIntArray indices;
            if (!mesh.GetPointsAttr().Get(&points) ||
                !mesh.GetFaceVertexCountsAttr().Get(&counts) ||
                !mesh.GetFaceVertexIndicesAttr().Get(&indices))
            {
                result.errors.emplace_back(result.Text(
                    "Incomplete mesh topology: " + definition.GetPath().GetString()));
            }
            result.counts["definitionMeshPoints"] += points.size();
            result.counts["definitionMeshFaces"] += counts.size();
            result.counts["definitionMeshIndices"] += indices.size();
            if (definition.HasAPI<UsdPhysicsCollisionAPI>())
            {
                result.counts["definitionColliderPoints"] += points.size();
                result.counts["definitionColliderFaces"] += counts.size();
                result.counts["definitionColliderIndices"] += indices.size();
            }
        }
        if (UsdGeomPointInstancer instancer{definition})
        {
            VtIntArray prototypes;
            if (!instancer.GetProtoIndicesAttr().Get(&prototypes))
            {
                result.errors.emplace_back(result.Text(
                    "Missing point-instancer prototype indices: " + definition.GetPath().GetString()));
            }
            result.counts["pointInstances"] += prototypes.size();
        }
    }
    for (const PcpErrorBasePtr& error : stage->GetCompositionErrors())
    {
        result.errors.emplace_back(result.Text(error->ToString()));
    }
    return result;
}

void SelfTest()
{
    auto layer = SdfLayer::CreateAnonymous(".usda");
    if (!layer->ImportFromString(R"USD(#usda 1.0
(
    defaultPrim = "World"
    metersPerUnit = 1
    upAxis = "Y"
)
class Xform "Asset" {
    def Cube "Collider" (prepend apiSchemas = ["PhysicsCollisionAPI", "UnregisteredPhysicsAPI"]) {
        bool physics:collisionEnabled = true
    }
}
def Xform "World" {
    def Xform "A" (
        prepend inherits = </Asset>
        instanceable = true
    ) {}
    def Xform "B" (
        prepend inherits = </Asset>
        instanceable = true
    ) {}
    def Camera "Camera" {
        float focalLength.timeSamples = {0: 35, 1: 50}
    }
    def Shader "Shader" {
        uniform token info:id = "UsdPreviewSurface"
        color3f inputs:diffuseColor = (1, 0, 0)
    }
    def Xform "DisabledPayload" (
        active = false
        prepend payload = </Asset>
    ) {}
    def RectLight "Bright" {
        float inputs:intensity = 3
        float inputs:exposure = 1
    }
    def RectLight "Dark" {
        float inputs:intensity = 0
    }
    def RectLight "Hidden" {
        float inputs:intensity = 3
        token visibility = "invisible"
    }
}
)USD"))
    {
        throw std::runtime_error("The warehouse audit fixture did not parse.");
    }
    const auto stage = UsdStage::Open(layer);
    Audit value = Inspect(stage);
    if (value.counts["instances"] != 2 || value.counts["colliderPrims"] != 2 ||
        value.counts["rigidBodyPrims"] != 0 || value.schemas["UnregisteredPhysicsAPI"] != 2 ||
        value.counts["timeSampledAttributes"] != 1 || value.shaders.size() != 1 ||
        value.colliders.size() != 2 || value.counts["unloadedPayloads"] != 0 ||
        value.counts["inactivePayloads"] != 1 || value.lights.size() != 3 ||
        value.counts["nonzeroVisibleLights"] != 1 || !value.errors.empty())
    {
        throw std::runtime_error("The audit lost instance, unknown-schema, shader or animation evidence.");
    }
    std::cout << "WAREHOUSE_AUDIT_SELF_TEST=passed\n";
}

void LocalizeCommand(const char* source, const char* destination, const char* profile_path)
{
    std::ifstream input(profile_path);
    JsParseError parse_error;
    const JsValue profile = JsParseStream(input, &parse_error);
    if (!profile.IsObject())
    {
        throw std::runtime_error("A valid repository-owned dataset localization profile is required.");
    }
    const JsObject& object = profile.GetJsObject();
    const std::string root_scene = object.at("rootScene").GetString();
    std::map<std::string, std::string> mappings;
    for (const auto& mapping : object.at("localAssetMappings").GetJsArray())
    {
        const auto& item = mapping.GetJsObject();
        if (!mappings.emplace(item.at("asset").GetString(), item.at("path").GetString()).second)
        {
            throw std::runtime_error("The localization profile repeats an asset URI.");
        }
    }
    auto result = openusd_warehouse::Localize(
        std::filesystem::u8path(source), std::filesystem::u8path(destination), root_scene, mappings);
    JsArray unresolved;
    for (const auto& path : result.unresolved)
    {
        unresolved.emplace_back(path);
    }
    JsWriteToStream(JsValue(JsObject{
        {"schemaVersion", JsValue(1)},
        {"scope", JsValue("Exact local dependency remapping only; unresolved module imports remain blockers.")},
        {"root", JsValue(result.root)},
        {"layerCount", JsValue(result.layer_count)},
        {"bytes", JsValue(result.bytes)},
        {"mappedOpinions", JsValue(result.mapped_opinions)},
        {"dependenciesComplete", JsValue(result.unresolved.empty())},
        {"unresolvedAssets", JsValue(std::move(unresolved))}
    }), std::cout);
    std::cout << '\n';
}

void LocalizationSelfTest()
{
    namespace fs = std::filesystem;
    const fs::path work = fs::temp_directory_path() / ("openusd-localize-" +
        std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()));
    const fs::path source = work / "source";
    const fs::path destination = work / "cache";
    fs::create_directories(source);
    try
    {
        const std::string root_text = R"USD(#usda 1.0
(defaultPrim = "World")
def Xform "World" {
    def Xform "A" (
        prepend payload = @asset.usda@
        instanceable = true
    ) {}
    def Xform "B" (
        prepend payload = @asset.usda@
        instanceable = true
    ) {}
}
)USD";
        const std::string asset_text = R"USD(#usda 1.0
(defaultPrim = "Asset")
def Xform "Asset" {
    def Cube "Geometry" {
        double size = 3
    }
    def Material "Physics" (
        prepend references = @omniverse://fixture/physics.usda@
    ) {}
    def Shader "Shader" {
        uniform token info:id = "UsdUVTexture"
        asset inputs:file = @texture.png@
        asset inputs:tiles = @texture.<UDIM>.png@
    }
}
)USD";
        {
            std::ofstream root_file(source / "root.usda", std::ios::binary);
            root_file << root_text;
            std::ofstream asset_file(source / "asset.usda", std::ios::binary);
            asset_file << asset_text;
            std::ofstream material_file(source / "material.usda", std::ios::binary);
            material_file << "#usda 1.0\n(defaultPrim = \"Material\")\n"
                "def Material \"Material\" (prepend apiSchemas = [\"PhysicsMaterialAPI\"]) {\n"
                "float physics:dynamicFriction = 0.25\n}\n";
            std::ofstream texture_file(source / "texture.png", std::ios::binary);
            texture_file << "asset identity fixture";
            std::ofstream first_tile(source / "texture.1001.png", std::ios::binary);
            first_tile << "first tile identity fixture";
            std::ofstream second_tile(source / "texture.1002.png", std::ios::binary);
            second_tile << "second tile identity fixture";
        }
        const auto result = openusd_warehouse::Localize(source, destination, "root.usda",
            {{"omniverse://fixture/physics.usda", "material.usda"}});
        auto stage = UsdStage::Open(result.root);
        float friction = 0;
        double size = 0;
        SdfAssetPath texture;
        SdfAssetPath tile_pattern;
        const bool values = stage &&
            stage->GetPrimAtPath(SdfPath("/World/A/Physics"))
                .GetAttribute(TfToken("physics:dynamicFriction")).Get(&friction) &&
            stage->GetPrimAtPath(SdfPath("/World/B/Geometry"))
                .GetAttribute(TfToken("size")).Get(&size) &&
            stage->GetPrimAtPath(SdfPath("/World/A/Shader"))
                .GetAttribute(TfToken("inputs:file")).Get(&texture) &&
            stage->GetPrimAtPath(SdfPath("/World/A/Shader"))
                .GetAttribute(TfToken("inputs:tiles")).Get(&tile_pattern);
        if (!values || friction != 0.25f || size != 3 ||
            result.layer_count != 3 || result.mapped_opinions != 1 || !result.unresolved.empty() ||
            !stage->GetCompositionErrors().empty() ||
            !stage->GetPrimAtPath(SdfPath("/World/A")).IsInstance() ||
            stage->GetPrimAtPath(SdfPath("/World/A")).GetPrototype() !=
                stage->GetPrimAtPath(SdfPath("/World/B")).GetPrototype() ||
            openusd_warehouse::PathKey(fs::u8path(texture.GetResolvedPath())) !=
                openusd_warehouse::PathKey(source / "texture.png"))
        {
            throw std::runtime_error("Localization lost values, instancing or original texture identity.");
        }
        if (tile_pattern.GetAssetPath() != (source / "texture.<UDIM>.png").generic_u8string())
        {
            throw std::runtime_error("Localization detached a UDIM pattern from its original source tiles: " +
                tile_pattern.GetAssetPath() + " expected " + (source / "texture.<UDIM>.png").generic_u8string());
        }
        std::ifstream original(source / "asset.usda", std::ios::binary);
        const std::string unchanged{std::istreambuf_iterator<char>(original),
            std::istreambuf_iterator<char>()};
        if (unchanged != asset_text)
        {
            throw std::runtime_error("Localization modified an original layer.");
        }
        bool refused = false;
        try
        {
            (void)openusd_warehouse::Localize(source, destination, "root.usda", {});
        }
        catch (const std::runtime_error&)
        {
            refused = true;
        }
        if (!refused)
        {
            throw std::runtime_error("Localization overwrote a previous cache.");
        }
        {
            std::ofstream bad_root(source / "bad-root.usda", std::ios::binary);
            bad_root << "#usda 1.0\n(defaultPrim = \"Root\")\n"
                "def Xform \"Root\" (prepend payload = @broken.usda@) {}\n";
            std::ofstream broken(source / "broken.usda", std::ios::binary);
            broken << "#usda 1.0\ndef Xform \"Broken\" { THIS IS NOT USD }\n";
        }
        refused = false;
        try
        {
            (void)openusd_warehouse::Localize(source, work / "bad-cache", "bad-root.usda", {});
        }
        catch (const std::runtime_error&)
        {
            refused = true;
        }
        if (!refused || fs::exists(work / "bad-cache"))
        {
            throw std::runtime_error("Localization published a cache after a dependency parse error.");
        }
        stage.Reset();
        original.close();
        fs::remove_all(work);
        std::cout << "WAREHOUSE_LOCALIZATION_SELF_TEST=passed\n";
    }
    catch (...)
    {
        fs::remove_all(work);
        throw;
    }
}
}

int main(int argc, char** argv)
{
    try
    {
        if (argc == 2 && std::string(argv[1]) == "--self-test")
        {
            SelfTest();
            return 0;
        }
        if (argc == 2 && std::string(argv[1]) == "--localization-self-test")
        {
            LocalizationSelfTest();
            return 0;
        }
        if (argc == 6 && std::string(argv[1]) == "--localize")
        {
            PlugRegistry::GetInstance().RegisterPlugins(argv[2]);
            LocalizeCommand(argv[3], argv[4], argv[5]);
            return 0;
        }
        if (argc != 3)
        {
            throw std::runtime_error(
                "Usage: probe <plugins> <root-usd> | --localize <plugins> <source-root> <new-cache> <profile>");
        }
        const auto start = std::chrono::steady_clock::now();
        PlugRegistry::GetInstance().RegisterPlugins(argv[1]);
        TfErrorMark mark;
        const auto stage = UsdStage::Open(argv[2], UsdStage::LoadAll);
        if (!stage)
        {
            throw std::runtime_error("The complete warehouse stage could not be opened.");
        }
        Audit audit = Inspect(stage);
        std::vector<SdfLayerRefPtr> layers;
        std::vector<std::string> assets;
        std::vector<std::string> unresolved;
        size_t dependencies = 0;
        const bool dependency_root_resolved = UsdUtilsComputeAllDependencies(
            SdfAssetPath(argv[2]), &layers, &assets, &unresolved,
            [&](const SdfLayerHandle&, const UsdUtilsDependencyInfo& dependency)
            {
                if (++dependencies > MaximumPrims)
                {
                    throw std::runtime_error("The dependency audit exceeded its visit bound.");
                }
                return dependency;
            });
        JsArray layer_paths;
        JsArray asset_paths;
        JsArray unresolved_paths;
        for (const auto& layer : layers)
        {
            layer_paths.emplace_back(audit.Text(layer->GetRealPath()));
        }
        for (const auto& path : assets)
        {
            asset_paths.emplace_back(audit.Text(path));
        }
        for (const auto& path : unresolved)
        {
            unresolved_paths.emplace_back(audit.Text(path));
        }
        UsdGeomBBoxCache bounds(UsdTimeCode::Default(),
            {UsdGeomTokens->default_, UsdGeomTokens->render, UsdGeomTokens->proxy});
        const GfRange3d range = bounds.ComputeWorldBound(stage->GetPseudoRoot()).ComputeAlignedRange();
        if (range.IsEmpty())
        {
            throw std::runtime_error("The warehouse has no nonempty world bounds.");
        }
        for (const auto& error : mark)
        {
            audit.errors.emplace_back(audit.Text(error.GetCommentary()));
        }
        mark.Clear();
        const bool complete = dependency_root_resolved && unresolved.empty() &&
            audit.errors.empty() && audit.counts["unloadedPayloads"] == 0;
        const double seconds = std::chrono::duration<double>(
            std::chrono::steady_clock::now() - start).count();
        JsObject report{
            {"schemaVersion", JsValue(1)},
            {"complete", JsValue(complete)},
            {"scope", JsValue("Composed USD and USD asset dependencies; MDL module imports require separate audit.")},
            {"root", audit.Text(stage->GetRootLayer()->GetRealPath())},
            {"defaultPrim", audit.Text(stage->GetDefaultPrim().GetPath().GetString())},
            {"openUsdVersion", JsValue(PXR_VERSION)},
            {"metersPerUnit", JsValue(UsdGeomGetStageMetersPerUnit(stage))},
            {"kilogramsPerUnit", JsValue(UsdPhysicsGetStageKilogramsPerUnit(stage))},
            {"upAxis", audit.Text(UsdGeomGetStageUpAxis(stage).GetString())},
            {"startTimeCode", JsValue(stage->GetStartTimeCode())},
            {"endTimeCode", JsValue(stage->GetEndTimeCode())},
            {"timeCodesPerSecond", JsValue(stage->GetTimeCodesPerSecond())},
            {"boundsMinimum", Vector(range.GetMin())},
            {"boundsMaximum", Vector(range.GetMax())},
            {"elapsedSeconds", JsValue(seconds)},
            {"counts", Counts(audit.counts)},
            {"typesIncludingProxies", Counts(audit.types)},
            {"appliedSchemasIncludingProxies", Counts(audit.schemas)},
            {"authoredPhysicsPropertyDefinitions", Counts(audit.physics_properties)},
            {"instances", JsValue(std::move(audit.instances))},
            {"colliders", JsValue(std::move(audit.colliders))},
            {"shaderDefinitions", JsValue(std::move(audit.shaders))},
            {"materialDefinitions", JsValue(std::move(audit.materials))},
            {"lights", JsValue(std::move(audit.lights))},
            {"timeSampledDefinitions", JsValue(std::move(audit.animated))},
            {"layers", JsValue(std::move(layer_paths))},
            {"nonLayerAssets", JsValue(std::move(asset_paths))},
            {"unresolvedAssets", JsValue(std::move(unresolved_paths))},
            {"errors", JsValue(std::move(audit.errors))}
        };
        JsWriteToStream(JsValue(std::move(report)), std::cout);
        std::cout << '\n';
        return complete ? 0 : 1;
    }
    catch (const std::exception& error)
    {
        std::cerr << "WAREHOUSE_AUDIT_FAILED: " << error.what() << '\n';
        return 2;
    }
}
