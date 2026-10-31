// Copyright (c) marcschier. Licensed under the MIT License.

#include "subdivision.h"

#include "pxr/imaging/hd/meshUtil.h"
#include "pxr/imaging/pxOsd/subdivTags.h"
#include "pxr/imaging/pxOsd/tokens.h"
#include "pxr/usd/sdf/types.h"
#include "pxr/usd/usd/stage.h"
#include "pxr/usd/usdGeom/primvarsAPI.h"
#include "pxr/usd/usdGeom/tokens.h"

#if defined(_MSC_VER)
#pragma warning(push, 0)
#elif defined(__clang__)
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Weverything"
#elif defined(__GNUC__)
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-Wall"
#pragma GCC diagnostic ignored "-Wextra"
#pragma GCC diagnostic ignored "-Wpedantic"
#endif
#include <opensubdiv/far/patchTable.h>
#include <opensubdiv/far/patchTableFactory.h>
#include <opensubdiv/far/ptexIndices.h>
#include <opensubdiv/far/topologyDescriptor.h>
#include <opensubdiv/far/topologyRefiner.h>
#include <opensubdiv/sdc/types.h>
#if defined(_MSC_VER)
#pragma warning(pop)
#elif defined(__clang__)
#pragma clang diagnostic pop
#elif defined(__GNUC__)
#pragma GCC diagnostic pop
#endif

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <iostream>
#include <limits>
#include <memory>
#include <string>
#include <type_traits>
#include <vector>

PXR_NAMESPACE_USING_DIRECTIVE
namespace Far = OpenSubdiv::Far;
namespace Sdc = OpenSubdiv::Sdc;

namespace
{
static_assert(std::is_same_v<decltype(HdSilkPtexTriangle::faceIndex), uint32_t>);

struct TestTopology
{
    const char* name;
    Sdc::SchemeType scheme;
    int vertices;
    std::vector<int> counts;
    std::vector<int> indices;
    std::vector<int> holes;
    bool leftHanded;
    bool boundaryNone = false;
    bool edgeOnly = false;
};

VtIntArray ToVtArray(const std::vector<int>& values)
{
    VtIntArray result;
    result.reserve(values.size());
    for (int value : values)
    {
        result.push_back(value);
    }
    return result;
}

HdMeshTopology MakeMeshTopology(const TestTopology& topology)
{
    const TfToken scheme = topology.scheme == Sdc::SCHEME_LOOP
        ? PxOsdOpenSubdivTokens->loop
        : topology.scheme == Sdc::SCHEME_BILINEAR
            ? PxOsdOpenSubdivTokens->bilinear
            : PxOsdOpenSubdivTokens->catmullClark;
    const TfToken orientation = topology.leftHanded
        ? PxOsdOpenSubdivTokens->leftHanded
        : PxOsdOpenSubdivTokens->rightHanded;
    HdMeshTopology result(
        scheme,
        orientation,
        ToVtArray(topology.counts),
        ToVtArray(topology.indices),
        ToVtArray(topology.holes));
    if (topology.boundaryNone || topology.edgeOnly)
    {
        PxOsdSubdivTags tags;
        tags.SetVertexInterpolationRule(
            topology.boundaryNone
                ? PxOsdOpenSubdivTokens->none
                : PxOsdOpenSubdivTokens->edgeOnly);
        result.SetSubdivTags(tags);
    }
    return result;
}

std::unique_ptr<Far::TopologyRefiner> MakeOracleRefiner(
    const TestTopology& topology,
    int level)
{
    Far::TopologyDescriptor descriptor;
    descriptor.numVertices = topology.vertices;
    descriptor.numFaces = static_cast<int>(topology.counts.size());
    descriptor.numVertsPerFace = topology.counts.data();
    descriptor.vertIndicesPerFace = topology.indices.data();
    descriptor.numHoles = static_cast<int>(topology.holes.size());
    descriptor.holeIndices = topology.holes.empty()
        ? nullptr
        : topology.holes.data();
    descriptor.isLeftHanded = topology.leftHanded;

    using Factory = Far::TopologyRefinerFactory<Far::TopologyDescriptor>;
    Factory::Options options(topology.scheme);
    options.schemeOptions.SetVtxBoundaryInterpolation(
        topology.boundaryNone
            ? Sdc::Options::VTX_BOUNDARY_NONE
            : topology.edgeOnly
                ? Sdc::Options::VTX_BOUNDARY_EDGE_ONLY
                : Sdc::Options::VTX_BOUNDARY_EDGE_AND_CORNER);
    options.validateFullTopology = true;
    std::unique_ptr<Far::TopologyRefiner> refiner(
        Factory::Create(descriptor, options));
    if (refiner)
    {
        Far::TopologyRefiner::UniformOptions refine(level);
        refine.fullTopologyInLastLevel = true;
        refiner->RefineUniform(refine);
    }
    return refiner;
}

std::unique_ptr<Far::PatchTable> MakeOraclePatchTable(
    const Far::TopologyRefiner& refiner)
{
    Far::PatchTableFactory::Options options;
    options.includeBaseLevelIndices = false;
    options.generateVaryingTables = false;
    options.generateFVarTables = false;
    return std::unique_ptr<Far::PatchTable>(
        Far::PatchTableFactory::Create(refiner, options));
}

bool Fail(const std::string& context, const std::string& message)
{
    std::cerr << context << ": " << message << '\n';
    return false;
}

bool SameMappings(
    const HdSilkPtexTriangle* first,
    const HdSilkPtexTriangle* second,
    size_t count)
{
    for (size_t index = 0; index < count; ++index)
    {
        if (first[index].faceIndex != second[index].faceIndex ||
            first[index].cornerCoordinates != second[index].cornerCoordinates)
        {
            return false;
        }
    }
    return true;
}

bool TestPtexFaceOffsets()
{
    std::array<HdSilkPtexTriangle, 6> mappings{};
    const std::array<uint32_t, 6> faceIds{{0, 0, 1, 1, 3, 4}};
    const std::array<uint32_t, 6> sourceFaces{{0, 0, 1, 1, 2, 2}};
    for (size_t triangle = 0; triangle < mappings.size(); ++triangle)
    {
        mappings[triangle].faceIndex = faceIds[triangle];
        mappings[triangle].cornerCoordinates.fill(static_cast<float>(triangle) * 0.125F);
    }
    const auto original = mappings;
    HdSilkPtexTriangle sentinel;
    sentinel.faceIndex = 99;
    sentinel.cornerCoordinates.fill(-1.0F);
    std::array<HdSilkPtexTriangle, 7> output;
    const auto expect = [&](
        HdSilkPtexFaceOffsets offsets,
        size_t textureFaceCount,
        const std::array<uint32_t, 6>& expected)
    {
        output.fill(sentinel);
        if (HdSilkWritePtexFaceOffsets(
                mappings.data(), mappings.size(), sourceFaces.data(), sourceFaces.size(),
                3, offsets, textureFaceCount, output.data(), output.size()) !=
            HdSilkPtexMappingStatus::Mapped)
        {
            return false;
        }
        for (size_t triangle = 0; triangle < mappings.size(); ++triangle)
        {
            if (output[triangle].faceIndex != expected[triangle] ||
                output[triangle].cornerCoordinates != original[triangle].cornerCoordinates)
            {
                return false;
            }
        }
        return output.back().faceIndex == sentinel.faceIndex &&
            output.back().cornerCoordinates == sentinel.cornerCoordinates &&
            SameMappings(mappings.data(), original.data(), mappings.size());
    };

    const int32_t constant = 2;
    const std::array<int32_t, 3> uniform{{3, 0, -2}};
    const std::array<int32_t, 3> indexedValues{{-2, 3, 0}};
    const std::array<int32_t, 3> indices{{1, 2, 0}};
    const int32_t constantIndex = 1;
    if (!expect({}, 5, {{0, 0, 1, 1, 3, 4}}) ||
        !expect({HdSilkPtexOffsetInterpolation::Constant, &constant, 1, nullptr, 0},
            7, {{2, 2, 3, 3, 5, 6}}) ||
        !expect({HdSilkPtexOffsetInterpolation::Uniform, uniform.data(), uniform.size(),
                nullptr, 0},
            4, {{3, 3, 1, 1, 1, 2}}) ||
        !expect({HdSilkPtexOffsetInterpolation::Uniform, indexedValues.data(),
                indexedValues.size(), indices.data(), indices.size()},
            4, {{3, 3, 1, 1, 1, 2}}) ||
        !expect({HdSilkPtexOffsetInterpolation::Constant, indexedValues.data(),
                indexedValues.size(), &constantIndex, 1},
            8, {{3, 3, 4, 4, 6, 7}}))
    {
        return Fail("Ptex face offsets", "constant, uniform or indexed mapping differs");
    }
    if (HdSilkWritePtexFaceOffsets(
            nullptr, 0, nullptr, 0, 0, {}, 0, nullptr, 0) !=
        HdSilkPtexMappingStatus::Mapped)
    {
        return Fail("Ptex face offsets", "empty mappings were refused");
    }
    return true;
}

bool TestPtexFaceOffsetPrimvars()
{
    UsdStageRefPtr stage = UsdStage::CreateInMemory();
    if (!stage)
    {
        return Fail("Ptex primvars", "could not create the stage");
    }
    const UsdPrim mesh = stage->DefinePrim(SdfPath("/Mesh"), TfToken("Mesh"));
    const UsdGeomPrimvarsAPI primvars(mesh);
    const UsdGeomPrimvar constant = primvars.CreatePrimvar(
        TfToken("ptexFaceOffset"), SdfValueTypeNames->Int, UsdGeomTokens->constant);
    const UsdGeomPrimvar uniform = primvars.CreatePrimvar(
        TfToken("uniformOffset"), SdfValueTypeNames->IntArray, UsdGeomTokens->uniform);
    const UsdGeomPrimvar indexed = primvars.CreatePrimvar(
        TfToken("indexedOffset"), SdfValueTypeNames->IntArray, UsdGeomTokens->uniform);
    const UsdGeomPrimvar grouped = primvars.CreatePrimvar(
        TfToken("groupedOffset"), SdfValueTypeNames->IntArray, UsdGeomTokens->uniform, 2);
    if (!mesh || !constant || !uniform || !indexed || !grouped ||
        !constant.Set(3) ||
        !uniform.Set(VtIntArray{3, 0, -2}) ||
        !indexed.Set(VtIntArray{-2, 3, 0}) ||
        !indexed.SetIndices(VtIntArray{1, 2, 0}) ||
        !grouped.Set(VtIntArray{3, 31, 0, 32, -2, 33}) ||
        !grouped.SetIndices(VtIntArray{0, 1, 2}))
    {
        return Fail("Ptex primvars", "could not author the primvars");
    }
    VtValue constantValue;
    VtValue uniformValue;
    VtValue indexedValue;
    VtValue groupedValue;
    VtIntArray indexedValues;
    VtIntArray groupedIndices;
    if (!constant.Get(&constantValue) || !uniform.Get(&uniformValue) ||
        !indexed.Get(&indexedValue) || !indexed.GetIndices(&indexedValues) ||
        !grouped.Get(&groupedValue) || !grouped.GetIndices(&groupedIndices) ||
        constant.GetInterpolation() != UsdGeomTokens->constant ||
        uniform.GetInterpolation() != UsdGeomTokens->uniform ||
        indexed.GetInterpolation() != UsdGeomTokens->uniform ||
        constant.GetElementSize() != 1 || uniform.GetElementSize() != 1 ||
        indexed.GetElementSize() != 1 || grouped.GetElementSize() != 2 ||
        !constantValue.IsHolding<int>() ||
        !uniformValue.IsHolding<VtIntArray>() ||
        !indexedValue.IsHolding<VtIntArray>())
    {
        return Fail("Ptex primvars", "USD returned unexpected authored values");
    }

    std::array<HdSilkPtexTriangle, 3> mappings{};
    mappings[0].faceIndex = 0;
    mappings[1].faceIndex = 1;
    mappings[2].faceIndex = 4;
    for (size_t triangle = 0; triangle < mappings.size(); ++triangle)
    {
        mappings[triangle].cornerCoordinates.fill(static_cast<float>(triangle));
    }
    const auto original = mappings;
    const std::array<uint32_t, 3> sourceFaces{{0, 1, 2}};
    HdSilkPtexTriangle sentinel;
    sentinel.faceIndex = 99;
    sentinel.cornerCoordinates.fill(-1.0F);
    std::array<HdSilkPtexTriangle, 4> output;
    const auto expectMapped = [&](
        HdInterpolation interpolation,
        const VtValue& value,
        const VtIntArray* indices,
        const std::array<uint32_t, 3>& expected,
        int elementSize = 1)
    {
        output.fill(sentinel);
        if (HdSilkWritePtexFaceOffsetPrimvar(
                mappings.data(), mappings.size(), sourceFaces.data(), sourceFaces.size(),
                3, interpolation, elementSize, value, indices, 8, output.data(), output.size()) !=
            HdSilkPtexMappingStatus::Mapped)
        {
            return false;
        }
        for (size_t triangle = 0; triangle < mappings.size(); ++triangle)
        {
            if (output[triangle].faceIndex != expected[triangle] ||
                output[triangle].cornerCoordinates != mappings[triangle].cornerCoordinates)
            {
                return false;
            }
        }
        return output.back().faceIndex == sentinel.faceIndex &&
            output.back().cornerCoordinates == sentinel.cornerCoordinates &&
            SameMappings(mappings.data(), original.data(), mappings.size());
    };
    const VtIntArray constantIndex{1};
    if (!expectMapped(HdInterpolationConstant, constantValue, nullptr, {{3, 4, 7}}) ||
        !expectMapped(HdInterpolationConstant, VtValue(VtIntArray{3}), nullptr, {{3, 4, 7}}) ||
        !expectMapped(HdInterpolationConstant, VtValue(VtIntArray{-2, 3}),
            &constantIndex, {{3, 4, 7}}) ||
        !expectMapped(HdInterpolationUniform, uniformValue, nullptr, {{3, 1, 2}}) ||
        !expectMapped(HdInterpolationUniform, indexedValue, &indexedValues, {{3, 1, 2}}))
    {
        return Fail("Ptex primvars", "authored scalar, array or indexed mapping differs");
    }

    const auto expectRefused = [&](
        HdInterpolation interpolation,
        const VtValue& value,
        const VtIntArray* indices,
        HdSilkPtexMappingStatus expected,
        int elementSize = 1)
    {
        output.fill(sentinel);
        return HdSilkWritePtexFaceOffsetPrimvar(
                mappings.data(), mappings.size(), sourceFaces.data(), sourceFaces.size(),
                3, interpolation, elementSize, value, indices, 8, output.data(), output.size()) ==
                expected &&
            std::all_of(output.begin(), output.end(), [&](const HdSilkPtexTriangle& entry)
            {
                return entry.faceIndex == sentinel.faceIndex &&
                    entry.cornerCoordinates == sentinel.cornerCoordinates;
            }) &&
            SameMappings(mappings.data(), original.data(), mappings.size());
    };
    const VtIntArray emptyIndices;
    const VtIntArray invalidIndex{1, -1, 0};
    const VtIntArray zeroIndex{0};
    if (!expectRefused(HdInterpolationUniform, constantValue, nullptr,
            HdSilkPtexMappingStatus::InvalidFaceOffsets) ||
        !expectRefused(HdInterpolationConstant, constantValue, &zeroIndex,
            HdSilkPtexMappingStatus::InvalidFaceOffsets) ||
        !expectRefused(HdInterpolationInstance, uniformValue, nullptr,
            HdSilkPtexMappingStatus::InvalidFaceOffsets) ||
        !expectRefused(HdInterpolationVertex, uniformValue, nullptr,
            HdSilkPtexMappingStatus::InvalidFaceOffsets) ||
        !expectRefused(HdInterpolationConstant, VtValue(3.0F), nullptr,
            HdSilkPtexMappingStatus::InvalidFaceOffsets) ||
        !expectRefused(HdInterpolationUniform, VtValue(VtFloatArray{3, 0, -2}), nullptr,
            HdSilkPtexMappingStatus::InvalidFaceOffsets) ||
        !expectRefused(HdInterpolationConstant, VtValue(), nullptr,
            HdSilkPtexMappingStatus::InvalidFaceOffsets) ||
        !expectRefused(HdInterpolationConstant, VtValue(VtIntArray{}), nullptr,
            HdSilkPtexMappingStatus::InvalidFaceOffsets) ||
        !expectRefused(HdInterpolationUniform, uniformValue, &emptyIndices,
            HdSilkPtexMappingStatus::InvalidFaceOffsets) ||
        !expectRefused(HdInterpolationUniform, indexedValue, &invalidIndex,
            HdSilkPtexMappingStatus::InvalidFaceOffsets) ||
        !expectRefused(HdInterpolationUniform, groupedValue, &groupedIndices,
            HdSilkPtexMappingStatus::InvalidFaceOffsets, grouped.GetElementSize()) ||
        !expectRefused(HdInterpolationUniform, indexedValue, &indexedValues,
            HdSilkPtexMappingStatus::InvalidFaceOffsets, 0) ||
        !expectRefused(HdInterpolationConstant, VtValue(-1), nullptr,
            HdSilkPtexMappingStatus::TextureFaceOutOfRange) ||
        !expectMapped(HdInterpolationUniform, indexedValue, &indexedValues, {{3, 1, 2}}))
    {
        return Fail("Ptex primvars", "invalid primvar or retry changed the output");
    }
    const UsdTimeCode editedTime(1.0);
    if (!indexed.Set(VtIntArray{1, 0, -1}, editedTime) ||
        !indexed.SetIndices(VtIntArray{0, 1, 2}, editedTime))
    {
        return Fail("Ptex primvars", "could not author time-sampled values");
    }
    VtValue editedValue;
    VtIntArray editedIndices;
    if (!indexed.Get(&editedValue, editedTime) ||
        !indexed.GetIndices(&editedIndices, editedTime) ||
        !expectMapped(HdInterpolationUniform, editedValue, &editedIndices, {{1, 1, 3}}) ||
        !expectMapped(HdInterpolationUniform, indexedValue, &indexedValues, {{3, 1, 2}}))
    {
        return Fail("Ptex primvars", "time-sampled edit or retained mapping differs");
    }
    const VtValue savedIndexedValue = indexedValue;
    const VtIntArray savedIndexedValues = indexedValues;
    if (HdSilkWritePtexFaceOffsetPrimvar(
            mappings.data(), mappings.size(), sourceFaces.data(), sourceFaces.size(),
            3, HdInterpolationUniform, 1, indexedValue, &indexedValues, 8,
            reinterpret_cast<HdSilkPtexTriangle*>(&indexedValue), mappings.size()) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        HdSilkWritePtexFaceOffsetPrimvar(
            mappings.data(), mappings.size(), sourceFaces.data(), sourceFaces.size(),
            3, HdInterpolationUniform, 1, indexedValue, &indexedValues, 8,
            reinterpret_cast<HdSilkPtexTriangle*>(&indexedValues), mappings.size()) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        indexedValue != savedIndexedValue || indexedValues != savedIndexedValues)
    {
        return Fail("Ptex primvars", "output overlapped a primvar owner");
    }
    VtIntArray aliasValues(21, 0);
    aliasValues[0] = -2;
    aliasValues[1] = 3;
    VtValue aliasValue(aliasValues);
    const VtIntArray& borrowedValues = aliasValue.UncheckedGet<VtIntArray>();
    const std::vector<int> originalValues(borrowedValues.cbegin(), borrowedValues.cend());
    if (HdSilkWritePtexFaceOffsetPrimvar(
            mappings.data(), mappings.size(), sourceFaces.data(), sourceFaces.size(),
            3, HdInterpolationUniform, 1, aliasValue, &indexedValues, 8,
            reinterpret_cast<HdSilkPtexTriangle*>(const_cast<int*>(borrowedValues.cdata())),
            mappings.size()) != HdSilkPtexMappingStatus::InvalidBuffers ||
        !std::equal(borrowedValues.cbegin(), borrowedValues.cend(), originalValues.cbegin()))
    {
        return Fail("Ptex primvars", "output overlapped primvar values");
    }
    if (HdSilkWritePtexFaceOffsetPrimvar(
            nullptr, 0, nullptr, 0, 0, HdInterpolationUniform,
            1, VtValue(VtIntArray{7}), &emptyIndices, 0, nullptr, 0) !=
        HdSilkPtexMappingStatus::Mapped)
    {
        return Fail("Ptex primvars", "empty indexed uniform mapping was refused");
    }
    if (HdSilkWritePtexFaceOffsetPrimvar(
            mappings.data(), 1, sourceFaces.data(), 1, 1, HdInterpolationUniform,
            1, constantValue, nullptr, 8, output.data(), output.size()) !=
            HdSilkPtexMappingStatus::Mapped ||
        output[0].faceIndex != 3 ||
        output[0].cornerCoordinates != mappings[0].cornerCoordinates)
    {
        return Fail("Ptex primvars", "single-face uniform scalar was refused");
    }
    return true;
}

bool TestPtexFaceOffsetRefusalsAndRetry()
{
    std::array<HdSilkPtexTriangle, 3> mappings{};
    mappings[0].faceIndex = 0;
    mappings[1].faceIndex = 2;
    mappings[2].faceIndex = 4;
    const auto original = mappings;
    std::array<uint32_t, 3> sourceFaces{{0, 2, 2}};
    const std::array<int32_t, 3> validValues{{1, 0, -1}};
    const std::array<int32_t, 3> validIndices{{0, 1, 2}};
    HdSilkPtexTriangle sentinel;
    sentinel.faceIndex = 99;
    sentinel.cornerCoordinates.fill(-1.0F);
    std::array<HdSilkPtexTriangle, 3> output;
    const auto expect = [&](
        HdSilkPtexFaceOffsets offsets,
        size_t textureFaceCount,
        HdSilkPtexMappingStatus status)
    {
        output.fill(sentinel);
        return HdSilkWritePtexFaceOffsets(
                mappings.data(), mappings.size(), sourceFaces.data(), sourceFaces.size(),
                3, offsets, textureFaceCount, output.data(), output.size()) == status &&
            std::all_of(output.begin(), output.end(), [&](const HdSilkPtexTriangle& value)
            {
                return value.faceIndex == sentinel.faceIndex &&
                    value.cornerCoordinates == sentinel.cornerCoordinates;
            }) &&
            SameMappings(mappings.data(), original.data(), mappings.size());
    };
    const int32_t negative = -1;
    const int32_t tooLarge = std::numeric_limits<int32_t>::max();
    const std::array<int32_t, 3> lateUnderflow{{1, 0, -5}};
    const std::array<int32_t, 3> badIndex{{0, -1, 2}};
    const std::array<int32_t, 3> missingIndex{{0, 3, 2}};
    if (!expect({HdSilkPtexOffsetInterpolation::None, validValues.data(), 1, nullptr, 0},
            5, HdSilkPtexMappingStatus::InvalidFaceOffsets) ||
        !expect({static_cast<HdSilkPtexOffsetInterpolation>(99), nullptr, 0, nullptr, 0},
            5, HdSilkPtexMappingStatus::InvalidFaceOffsets) ||
        !expect({HdSilkPtexOffsetInterpolation::Constant, nullptr, 0, nullptr, 0},
            5, HdSilkPtexMappingStatus::InvalidFaceOffsets) ||
        !expect({HdSilkPtexOffsetInterpolation::Constant, validValues.data(), 2, nullptr, 0},
            5, HdSilkPtexMappingStatus::InvalidFaceOffsets) ||
        !expect({HdSilkPtexOffsetInterpolation::Uniform, validValues.data(), 2, nullptr, 0},
            5, HdSilkPtexMappingStatus::InvalidFaceOffsets) ||
        !expect({HdSilkPtexOffsetInterpolation::Uniform, validValues.data(), validValues.size(),
                validIndices.data(), validIndices.size() - 1},
            5, HdSilkPtexMappingStatus::InvalidFaceOffsets) ||
        !expect({HdSilkPtexOffsetInterpolation::Uniform, validValues.data(), validValues.size(),
                badIndex.data(), badIndex.size()},
            5, HdSilkPtexMappingStatus::InvalidFaceOffsets) ||
        !expect({HdSilkPtexOffsetInterpolation::Uniform, validValues.data(), validValues.size(),
                missingIndex.data(), missingIndex.size()},
            5, HdSilkPtexMappingStatus::InvalidFaceOffsets) ||
        !expect({HdSilkPtexOffsetInterpolation::Constant, &negative, 1, nullptr, 0},
            5, HdSilkPtexMappingStatus::TextureFaceOutOfRange) ||
        !expect({HdSilkPtexOffsetInterpolation::Constant, &tooLarge, 1, nullptr, 0},
            5, HdSilkPtexMappingStatus::TextureFaceOutOfRange) ||
        !expect({HdSilkPtexOffsetInterpolation::Uniform, lateUnderflow.data(),
                lateUnderflow.size(), nullptr, 0},
            5, HdSilkPtexMappingStatus::TextureFaceOutOfRange) ||
        !expect({}, 0, HdSilkPtexMappingStatus::TextureFaceOutOfRange) ||
        !expect({}, 4, HdSilkPtexMappingStatus::TextureFaceOutOfRange))
    {
        return Fail("Ptex face offsets", "invalid offsets or face ranges were accepted");
    }
    output.fill(sentinel);
    const HdSilkPtexFaceOffsets valid{
        HdSilkPtexOffsetInterpolation::Uniform, validValues.data(), validValues.size(),
        validIndices.data(), validIndices.size()};
    sourceFaces[1] = 3;
    if (HdSilkWritePtexFaceOffsets(
            mappings.data(), mappings.size(), sourceFaces.data(), sourceFaces.size(),
            3, valid, 5, output.data(), output.size()) !=
        HdSilkPtexMappingStatus::UnsupportedTopology ||
        !std::all_of(output.begin(), output.end(), [&](const HdSilkPtexTriangle& value)
        {
            return value.faceIndex == sentinel.faceIndex &&
                value.cornerCoordinates == sentinel.cornerCoordinates;
        }))
    {
        return Fail("Ptex face offsets", "invalid source face changed the output");
    }
    sourceFaces[1] = 2;
    std::array<HdSilkPtexTriangle, 4> aliasedMappings{};
    std::copy(mappings.begin(), mappings.end(), aliasedMappings.begin());
    alignas(HdSilkPtexTriangle) std::array<uint32_t, 21> aliasedSourceFaces{};
    alignas(HdSilkPtexTriangle) std::array<int32_t, 21> aliasedValues{};
    alignas(HdSilkPtexTriangle) std::array<int32_t, 21> aliasedIndices{};
    std::copy(sourceFaces.begin(), sourceFaces.end(), aliasedSourceFaces.begin());
    std::copy(validValues.begin(), validValues.end(), aliasedValues.begin());
    std::copy(validIndices.begin(), validIndices.end(), aliasedIndices.begin());
    if (HdSilkWritePtexFaceOffsets(
            mappings.data(), mappings.size(), sourceFaces.data(), sourceFaces.size() - 1,
            3, valid, 5, output.data(), output.size()) !=
            HdSilkPtexMappingStatus::TriangleMismatch ||
        HdSilkWritePtexFaceOffsets(
            mappings.data(), mappings.size(), sourceFaces.data(), sourceFaces.size(),
            3, valid, 5, output.data(), output.size() - 1) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        HdSilkWritePtexFaceOffsets(
            mappings.data(), mappings.size(), sourceFaces.data(), sourceFaces.size(),
            3, valid, 5, mappings.data(), mappings.size()) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        HdSilkWritePtexFaceOffsets(
            aliasedMappings.data(), mappings.size(), sourceFaces.data(), sourceFaces.size(),
            3, valid, 5, aliasedMappings.data() + 1, mappings.size()) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        HdSilkWritePtexFaceOffsets(
            mappings.data(), mappings.size(), sourceFaces.data(), sourceFaces.size(),
            3, valid, 5, nullptr, output.size()) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        HdSilkWritePtexFaceOffsets(
            nullptr, mappings.size(), sourceFaces.data(), sourceFaces.size(),
            3, valid, 5, output.data(), output.size()) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        HdSilkWritePtexFaceOffsets(
            mappings.data(), mappings.size(), nullptr, sourceFaces.size(),
            3, valid, 5, output.data(), output.size()) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        HdSilkWritePtexFaceOffsets(
            mappings.data(), mappings.size(), sourceFaces.data(), sourceFaces.size(),
            3, {HdSilkPtexOffsetInterpolation::Uniform, nullptr, 3, nullptr, 0},
            5, output.data(), output.size()) != HdSilkPtexMappingStatus::InvalidBuffers ||
        HdSilkWritePtexFaceOffsets(
            mappings.data(), mappings.size(), sourceFaces.data(), sourceFaces.size(),
            3, {HdSilkPtexOffsetInterpolation::Uniform, validValues.data(),
                validValues.size(), nullptr, 3},
            5, output.data(), output.size()) != HdSilkPtexMappingStatus::InvalidBuffers ||
        HdSilkWritePtexFaceOffsets(
            mappings.data(), mappings.size(), sourceFaces.data(), sourceFaces.size(),
            3, valid, 5, output.data(), std::numeric_limits<size_t>::max()) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        HdSilkWritePtexFaceOffsets(
            mappings.data(), mappings.size(), aliasedSourceFaces.data(), sourceFaces.size(),
            3, valid, 5, reinterpret_cast<HdSilkPtexTriangle*>(aliasedSourceFaces.data()),
            output.size()) != HdSilkPtexMappingStatus::InvalidBuffers ||
        HdSilkWritePtexFaceOffsets(
            mappings.data(), mappings.size(), sourceFaces.data(), sourceFaces.size(),
            3, {HdSilkPtexOffsetInterpolation::Uniform, aliasedValues.data(),
                validValues.size(), validIndices.data(), validIndices.size()},
            5, reinterpret_cast<HdSilkPtexTriangle*>(aliasedValues.data()),
            output.size()) != HdSilkPtexMappingStatus::InvalidBuffers ||
        HdSilkWritePtexFaceOffsets(
            mappings.data(), mappings.size(), sourceFaces.data(), sourceFaces.size(),
            3, {HdSilkPtexOffsetInterpolation::Uniform, validValues.data(),
                validValues.size(), aliasedIndices.data(), validIndices.size()},
            5, reinterpret_cast<HdSilkPtexTriangle*>(aliasedIndices.data()),
            output.size()) != HdSilkPtexMappingStatus::InvalidBuffers ||
        HdSilkWritePtexFaceOffsets(
            mappings.data(), mappings.size(), sourceFaces.data(), sourceFaces.size(),
            static_cast<size_t>(std::numeric_limits<int>::max()) + 1,
            valid, 5, output.data(), output.size()) !=
            HdSilkPtexMappingStatus::ExceedsBounds ||
        HdSilkWritePtexFaceOffsets(
            mappings.data(), mappings.size(), sourceFaces.data(), sourceFaces.size(),
            3, valid, static_cast<size_t>(std::numeric_limits<int>::max()) + 1,
            output.data(), output.size()) != HdSilkPtexMappingStatus::ExceedsBounds ||
        !SameMappings(mappings.data(), original.data(), mappings.size()) ||
        !std::all_of(output.begin(), output.end(),
            [&](const HdSilkPtexTriangle& value)
            {
                return value.faceIndex == sentinel.faceIndex &&
                    value.cornerCoordinates == sentinel.cornerCoordinates;
            }))
    {
        return Fail("Ptex face offsets", "invalid buffer or count was accepted");
    }
    if (HdSilkWritePtexFaceOffsets(
            mappings.data(), mappings.size(), sourceFaces.data(), sourceFaces.size(),
            3, valid, 5, output.data(), output.size()) !=
            HdSilkPtexMappingStatus::Mapped ||
        output[0].faceIndex != 1 || output[1].faceIndex != 1 || output[2].faceIndex != 3)
    {
        return Fail("Ptex face offsets", "valid retry did not recover");
    }
    return true;
}

bool TestPtexFaceOffsetIntegerBoundaries()
{
    HdSilkPtexTriangle mapping;
    mapping.faceIndex = static_cast<uint32_t>(std::numeric_limits<int>::max()) - 1;
    mapping.cornerCoordinates.fill(0.5F);
    const uint32_t sourceFace = 0;
    int32_t offset = 2;
    const HdSilkPtexFaceOffsets constant{
        HdSilkPtexOffsetInterpolation::Constant, &offset, 1, nullptr, 0};
    HdSilkPtexTriangle output;
    output.faceIndex = 99;
    output.cornerCoordinates.fill(-1.0F);
    const HdSilkPtexTriangle sentinel = output;
    const size_t maxTextureFaces = static_cast<size_t>(std::numeric_limits<int>::max());
    if (HdSilkWritePtexFaceOffsets(
            &mapping, 1, &sourceFace, 1, 1, constant, maxTextureFaces, &output, 1) !=
            HdSilkPtexMappingStatus::TextureFaceOutOfRange ||
        !SameMappings(&output, &sentinel, 1))
    {
        return Fail("Ptex face offsets", "large signed addition was not refused atomically");
    }
    offset = 0;
    if (HdSilkWritePtexFaceOffsets(
            &mapping, 1, &sourceFace, 1, 1, constant, maxTextureFaces, &output, 1) !=
            HdSilkPtexMappingStatus::Mapped ||
        !SameMappings(&output, &mapping, 1))
    {
        return Fail("Ptex face offsets", "last valid texture face was refused");
    }
    mapping.faceIndex = std::numeric_limits<uint32_t>::max();
    offset = std::numeric_limits<int32_t>::min();
    output = sentinel;
    if (HdSilkWritePtexFaceOffsets(
            &mapping, 1, &sourceFace, 1, 1, constant, maxTextureFaces, &output, 1) !=
            HdSilkPtexMappingStatus::TextureFaceOutOfRange ||
        !SameMappings(&output, &sentinel, 1))
    {
        return Fail("Ptex face offsets", "unsigned face boundary was not refused atomically");
    }
    return true;
}

bool TestPtexOffsetsFollowSourceFaces(
    const std::string& context,
    const std::vector<HdSilkPtexTriangle>& mappings,
    const uint32_t* sourceFaces,
    size_t sourceFaceCount,
    size_t authoredFaceCount)
{
    std::vector<int32_t> offsets(authoredFaceCount);
    for (size_t face = 0; face < authoredFaceCount; ++face)
    {
        offsets[face] = static_cast<int32_t>(face + 1);
    }
    std::vector<HdSilkPtexTriangle> result(mappings.size());
    const HdSilkPtexFaceOffsets uniform{
        HdSilkPtexOffsetInterpolation::Uniform, offsets.data(), offsets.size(), nullptr, 0};
    if (HdSilkWritePtexFaceOffsets(
            mappings.data(), mappings.size(), sourceFaces, sourceFaceCount,
            authoredFaceCount, uniform, static_cast<size_t>(std::numeric_limits<int>::max()),
            result.data(), result.size()) != HdSilkPtexMappingStatus::Mapped)
    {
        return Fail(context, "uniform Ptex offsets were refused");
    }
    for (size_t triangle = 0; triangle < mappings.size(); ++triangle)
    {
        const uint32_t expected =
            mappings[triangle].faceIndex + static_cast<uint32_t>(offsets[sourceFaces[triangle]]);
        if (result[triangle].faceIndex != expected ||
            result[triangle].cornerCoordinates != mappings[triangle].cornerCoordinates)
        {
            return Fail(context, "Ptex offset did not follow the authored source face");
        }
    }
    return true;
}

bool SameVertices(Far::ConstIndexArray first, Far::ConstIndexArray second)
{
    if (first.size() != second.size())
    {
        return false;
    }
    for (int index = 0; index < first.size(); ++index)
    {
        if (first[index] != second[index])
        {
            return false;
        }
    }
    return true;
}

bool FindPatch(
    const Far::PatchTable& table,
    Far::ConstIndexArray vertices,
    Far::PatchParam* result)
{
    int matches = 0;
    for (int array = 0; array < table.GetNumPatchArrays(); ++array)
    {
        for (int patch = 0; patch < table.GetNumPatches(array); ++patch)
        {
            if (SameVertices(vertices, table.GetPatchVertices(array, patch)))
            {
                *result = table.GetPatchParam(array, patch);
                ++matches;
            }
        }
    }
    return matches == 1;
}

struct CoarseTriangles
{
    VtArray<uint32_t> indices;
    VtArray<uint32_t> subprims;
};

CoarseTriangles TriangulateCoarse(const HdMeshTopology& topology)
{
    HdMeshUtil util(&topology, SdfPath("/PtexCoarseProbe"));
    VtVec3iArray indices;
    VtIntArray params;
    util.ComputeTriangleIndices(&indices, &params);
    CoarseTriangles result;
    result.indices.reserve(indices.size() * 3);
    result.subprims.reserve(params.size());
    for (size_t triangle = 0; triangle < indices.size(); ++triangle)
    {
        for (int corner = 0; corner < 3; ++corner)
        {
            result.indices.push_back(
                static_cast<uint32_t>(indices[triangle][corner]));
        }
        result.subprims.push_back(static_cast<uint32_t>(
            HdMeshUtil::DecodeFaceIndexFromCoarseFaceParam(params[triangle])));
    }
    return result;
}

bool TestCoarseCoordinatesMatchOpenSubdiv(const TestTopology& topology)
{
    const HdMeshTopology mesh = MakeMeshTopology(topology);
    const CoarseTriangles emitted = TriangulateCoarse(mesh);
    std::vector<HdSilkPtexTriangle> triangles(emitted.subprims.size());
    std::vector<uint32_t> scratch(HdSilkCoarsePtexScratchCount(mesh));
    const HdSilkPtexMappingStatus status = HdSilkWriteDefaultCoarsePtexTriangles(
        mesh, emitted.indices, emitted.subprims,
        triangles.data(), triangles.size(), scratch.data(), scratch.size());
    if (status != HdSilkPtexMappingStatus::Mapped)
    {
        return Fail(
            topology.name,
            std::string("coarse Ptex mapping was refused: ") +
                HdSilkPtexMappingStatusName(status));
    }

    const std::unique_ptr<Far::TopologyRefiner> oracle =
        MakeOracleRefiner(topology, 1);
    if (!oracle)
    {
        return Fail(topology.name, "coarse oracle refiner could not be created");
    }
    const Far::PtexIndices ptexIndices(*oracle);
    const Far::TopologyLevel& coarse = oracle->GetLevel(0);
    for (size_t triangle = 0; triangle < triangles.size(); ++triangle)
    {
        const int face = static_cast<int>(emitted.subprims[triangle]);
        const Far::ConstIndexArray vertices = coarse.GetFaceVertices(face);
        std::array<std::array<float, 2>, 4> expected{{
            {{0.0F, 0.0F}}, {{1.0F, 0.0F}},
            {{1.0F, 1.0F}}, {{0.0F, 1.0F}}}};
        if (topology.scheme == Sdc::SCHEME_LOOP)
        {
            expected[2] = {{0.0F, 1.0F}};
        }
        if (triangles[triangle].faceIndex !=
            static_cast<uint32_t>(ptexIndices.GetFaceId(face)))
        {
            return Fail(topology.name, "coarse Ptex face ID differs from OpenSubdiv");
        }
        for (int corner = 0; corner < 3; ++corner)
        {
            const uint32_t vertex =
                emitted.indices[triangle * 3 + static_cast<size_t>(corner)];
            int oracleCorner = -1;
            for (int index = 0; index < vertices.size(); ++index)
            {
                if (vertices[index] == static_cast<int>(vertex))
                {
                    if (oracleCorner >= 0)
                    {
                        return Fail(topology.name, "oracle face repeats a vertex");
                    }
                    oracleCorner = index;
                }
            }
            if (oracleCorner < 0 ||
                triangles[triangle].cornerCoordinates[static_cast<size_t>(corner) * 2] !=
                    expected[static_cast<size_t>(oracleCorner)][0] ||
                triangles[triangle].cornerCoordinates[static_cast<size_t>(corner) * 2 + 1] !=
                    expected[static_cast<size_t>(oracleCorner)][1])
            {
                return Fail(topology.name, "coarse corner differs from OpenSubdiv");
            }
        }
    }
    return true;
}

bool TestCoarseTriQuadsMatchHydraAndOpenSubdiv(const TestTopology& topology)
{
    const HdMeshTopology mesh = MakeMeshTopology(topology);
    VtVec3fArray sourcePoints;
    for (int point = 0; point < topology.vertices + 2; ++point)
    {
        sourcePoints.push_back(GfVec3f(
            static_cast<float>(point % 7) * 0.25F,
            static_cast<float>((point * point + 2) % 11) * 0.125F,
            static_cast<float>(point) * 0.0625F));
    }

    HdMeshUtil util(&mesh, SdfPath("/PtexTriQuadProbe"));
    HdQuadInfo quadInfo;
    util.ComputeQuadInfo(&quadInfo);
    VtIntArray expectedIndices;
    VtIntArray expectedParams;
    util.ComputeTriQuadIndices(&expectedIndices, &expectedParams);
    VtValue expectedPointsValue;
    if (!util.ComputeQuadrangulatedPrimvar(
            &quadInfo, sourcePoints.cdata(), static_cast<int>(sourcePoints.size()),
            HdTypeFloatVec3, &expectedPointsValue) ||
        !expectedPointsValue.IsHolding<VtVec3fArray>())
    {
        return Fail(topology.name, "Hydra could not quadrangulate the source points");
    }
    const VtVec3fArray& expectedPoints = expectedPointsValue.UncheckedGet<VtVec3fArray>();

    HdSilkPtexCoarseTriQuadPlan plan;
    if (HdSilkPlanDefaultCoarsePtexTriQuads(mesh, sourcePoints.size(), &plan) !=
            HdSilkPtexMappingStatus::Mapped ||
        plan.inputPointCount != static_cast<size_t>(quadInfo.pointsOffset) ||
        plan.outputPointCount != expectedPoints.size() ||
        plan.triangleCount * 3 != expectedIndices.size() ||
        plan.triangleCount != expectedParams.size() * 2 ||
        plan.scratchCount != plan.inputPointCount)
    {
        return Fail(topology.name, "coarse Ptex triangulation plan differs from Hydra");
    }
    std::vector<GfVec3f> points(plan.outputPointCount);
    std::vector<uint32_t> indices(plan.triangleCount * 3);
    std::vector<uint32_t> subprims(plan.triangleCount);
    std::vector<HdSilkPtexTriangle> mappings(plan.triangleCount);
    std::vector<uint32_t> scratch(plan.scratchCount);
    if (HdSilkWriteDefaultCoarsePtexTriQuads(
            mesh, sourcePoints, points.data(), points.size(),
            indices.data(), indices.size(), subprims.data(), subprims.size(),
            mappings.data(), mappings.size(), scratch.data(), scratch.size()) !=
        HdSilkPtexMappingStatus::Mapped)
    {
        return Fail(topology.name, "coarse Ptex triangulation was refused");
    }
    for (size_t point = 0; point < points.size(); ++point)
    {
        for (int component = 0; component < 3; ++component)
        {
            if (!std::isfinite(points[point][component]) ||
                std::abs(points[point][component] - expectedPoints[point][component]) >
                    0.00001F)
            {
                return Fail(topology.name, "generated point differs from Hydra");
            }
        }
    }
    for (size_t index = 0; index < indices.size(); ++index)
    {
        if (indices[index] != static_cast<uint32_t>(expectedIndices[index]))
        {
            return Fail(topology.name, "triangle index differs from Hydra");
        }
    }

    const std::unique_ptr<Far::TopologyRefiner> oracle =
        MakeOracleRefiner(topology, 1);
    if (!oracle)
    {
        return Fail(topology.name, "tri-quad oracle refiner could not be created");
    }
    const std::unique_ptr<Far::PatchTable> patches = MakeOraclePatchTable(*oracle);
    if (!patches)
    {
        return Fail(topology.name, "tri-quad oracle patch table could not be created");
    }
    const Far::PtexIndices ptexIndices(*oracle);
    const Far::TopologyLevel& coarse = oracle->GetLevel(0);
    const Far::TopologyLevel& first = oracle->GetLevel(1);
    const std::array<std::array<float, 2>, 4> corners{{
        {{0.0F, 0.0F}}, {{1.0F, 0.0F}}, {{1.0F, 1.0F}}, {{0.0F, 1.0F}}}};
    constexpr std::array<int, 6> triangleCorners{{0, 1, 2, 2, 3, 0}};
    for (size_t quad = 0; quad < expectedParams.size(); ++quad)
    {
        const int face = HdMeshUtil::DecodeFaceIndexFromCoarseFaceParam(expectedParams[quad]);
        const Far::ConstIndexArray coarseVertices = coarse.GetFaceVertices(face);
        Far::PatchParam patch;
        uint32_t expectedFace = static_cast<uint32_t>(ptexIndices.GetFaceId(face));
        if (coarseVertices.size() != 4)
        {
            const Far::Index sourceVertex = static_cast<Far::Index>(indices[quad * 6]);
            const Far::Index childVertex = coarse.GetVertexChildVertex(sourceVertex);
            Far::Index childFace = -1;
            const Far::ConstIndexArray children = coarse.GetFaceChildFaces(face);
            for (int child = 0; child < children.size(); ++child)
            {
                const Far::ConstIndexArray childVertices =
                    first.GetFaceVertices(children[child]);
                if (childVertices.size() == 4 && childVertices[0] == childVertex)
                {
                    if (childFace >= 0)
                    {
                        return Fail(topology.name, "two Ptex subfaces share a source corner");
                    }
                    childFace = children[child];
                }
            }
            if (childFace < 0 ||
                !FindPatch(*patches, first.GetFaceVertices(childFace), &patch))
            {
                return Fail(topology.name, "Ptex subface is absent from the patch table");
            }
            expectedFace = static_cast<uint32_t>(patch.GetFaceId());
        }

        for (size_t triangle = 0; triangle < 2; ++triangle)
        {
            const size_t triangleIndex = quad * 2 + triangle;
            if (subprims[triangleIndex] != static_cast<uint32_t>(face) ||
                mappings[triangleIndex].faceIndex != expectedFace)
            {
                return Fail(topology.name, "source or Ptex face identity differs");
            }
            for (size_t corner = 0; corner < 3; ++corner)
            {
                const size_t local = triangle * 3 + corner;
                int expectedCorner = triangleCorners[local];
                if (coarseVertices.size() == 4)
                {
                    expectedCorner = -1;
                    const Far::Index vertex =
                        static_cast<Far::Index>(indices[quad * 6 + local]);
                    for (int candidate = 0; candidate < 4; ++candidate)
                    {
                        if (coarseVertices[candidate] == vertex)
                        {
                            expectedCorner = candidate;
                        }
                    }
                    if (expectedCorner < 0)
                    {
                        return Fail(topology.name, "quad vertex is absent from the coarse face");
                    }
                }
                float u = corners[static_cast<size_t>(expectedCorner)][0];
                float v = corners[static_cast<size_t>(expectedCorner)][1];
                if (coarseVertices.size() != 4)
                {
                    patch.Unnormalize(u, v);
                }
                if (!std::isfinite(mappings[triangleIndex].cornerCoordinates[corner * 2]) ||
                    !std::isfinite(mappings[triangleIndex].cornerCoordinates[corner * 2 + 1]) ||
                    std::abs(mappings[triangleIndex].cornerCoordinates[corner * 2] - u) >
                        0.000001F ||
                    std::abs(mappings[triangleIndex].cornerCoordinates[corner * 2 + 1] - v) >
                        0.000001F)
                {
                    return Fail(topology.name, "Ptex corner differs from the patch table");
                }
            }
        }
    }
    return TestPtexOffsetsFollowSourceFaces(
        topology.name, mappings, subprims.data(), subprims.size(), topology.counts.size());
}

bool TestCoarseTriQuadRefusalsAndRetry()
{
    const TestTopology topology{
        "tri-quad admission", Sdc::SCHEME_CATMARK, 5,
        {5}, {0, 1, 2, 3, 4}, {}, false};
    const HdMeshTopology mesh = MakeMeshTopology(topology);
    VtVec3fArray source(5, GfVec3f(1.0F, 2.0F, 3.0F));
    HdSilkPtexCoarseTriQuadPlan plan;
    if (HdSilkPlanDefaultCoarsePtexTriQuads(mesh, source.size(), &plan) !=
            HdSilkPtexMappingStatus::Mapped ||
        plan.inputPointCount != 5 || plan.outputPointCount != 11 ||
        plan.triangleCount != 10 || plan.scratchCount != 5)
    {
        return Fail(topology.name, "pentagon storage plan is incorrect");
    }
    const GfVec3f sentinel(-9.0F);
    std::vector<GfVec3f> points(plan.outputPointCount, sentinel);
    std::vector<uint32_t> indices(plan.triangleCount * 3, 99);
    std::vector<uint32_t> subprims(plan.triangleCount, 99);
    HdSilkPtexTriangle mappingSentinel;
    mappingSentinel.faceIndex = 99;
    mappingSentinel.cornerCoordinates.fill(-9.0F);
    std::vector<HdSilkPtexTriangle> mappings(plan.triangleCount, mappingSentinel);
    std::vector<uint32_t> scratch(plan.scratchCount, 99);
    const auto write = [&](
        const HdMeshTopology& input, const VtVec3fArray& positions,
        GfVec3f* pointOutput, size_t pointCapacity,
        uint32_t* indexOutput, size_t indexCapacity,
        uint32_t* subprimOutput, size_t subprimCapacity,
        HdSilkPtexTriangle* mappingOutput, size_t mappingCapacity,
        uint32_t* scratchOutput, size_t scratchCapacity)
    {
        return HdSilkWriteDefaultCoarsePtexTriQuads(
            input, positions, pointOutput, pointCapacity,
            indexOutput, indexCapacity, subprimOutput, subprimCapacity,
            mappingOutput, mappingCapacity, scratchOutput, scratchCapacity);
    };
    const auto defaultWrite = [&](const HdMeshTopology& input, const VtVec3fArray& positions)
    {
        return write(
            input, positions, points.data(), points.size(), indices.data(), indices.size(),
            subprims.data(), subprims.size(), mappings.data(), mappings.size(),
            scratch.data(), scratch.size());
    };
    if (defaultWrite(mesh, source) != HdSilkPtexMappingStatus::Mapped)
    {
        return Fail(topology.name, "exact planned capacities were refused");
    }
    std::fill(points.begin(), points.end(), sentinel);
    std::fill(indices.begin(), indices.end(), 99);
    std::fill(subprims.begin(), subprims.end(), 99);
    std::fill(mappings.begin(), mappings.end(), mappingSentinel);
    std::fill(scratch.begin(), scratch.end(), 99);
    if (HdSilkPlanDefaultCoarsePtexTriQuads(mesh, source.size(), nullptr) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        write(mesh, source, nullptr, points.size(), indices.data(), indices.size(),
            subprims.data(), subprims.size(), mappings.data(), mappings.size(),
            scratch.data(), scratch.size()) != HdSilkPtexMappingStatus::InvalidBuffers ||
        write(mesh, source, points.data(), points.size(), nullptr, indices.size(),
            subprims.data(), subprims.size(), mappings.data(), mappings.size(),
            scratch.data(), scratch.size()) != HdSilkPtexMappingStatus::InvalidBuffers ||
        write(mesh, source, points.data(), points.size(), indices.data(), indices.size(),
            nullptr, subprims.size(), mappings.data(), mappings.size(),
            scratch.data(), scratch.size()) != HdSilkPtexMappingStatus::InvalidBuffers ||
        write(mesh, source, points.data(), points.size(), indices.data(), indices.size(),
            subprims.data(), subprims.size(), nullptr, mappings.size(),
            scratch.data(), scratch.size()) != HdSilkPtexMappingStatus::InvalidBuffers ||
        write(mesh, source, points.data(), points.size(), indices.data(), indices.size(),
            subprims.data(), subprims.size(), mappings.data(), mappings.size(),
            nullptr, scratch.size()) != HdSilkPtexMappingStatus::InvalidBuffers ||
        write(mesh, source, points.data(), points.size() - 1, indices.data(), indices.size(),
            subprims.data(), subprims.size(), mappings.data(), mappings.size(),
            scratch.data(), scratch.size()) != HdSilkPtexMappingStatus::InvalidBuffers ||
        write(mesh, source, points.data(), points.size(), indices.data(), indices.size() - 1,
            subprims.data(), subprims.size(), mappings.data(), mappings.size(),
            scratch.data(), scratch.size()) != HdSilkPtexMappingStatus::InvalidBuffers ||
        write(mesh, source, points.data(), points.size(), indices.data(), indices.size(),
            subprims.data(), subprims.size() - 1, mappings.data(), mappings.size(),
            scratch.data(), scratch.size()) != HdSilkPtexMappingStatus::InvalidBuffers ||
        write(mesh, source, points.data(), points.size(), indices.data(), indices.size(),
            subprims.data(), subprims.size(), mappings.data(), mappings.size() - 1,
            scratch.data(), scratch.size()) != HdSilkPtexMappingStatus::InvalidBuffers ||
        write(mesh, source, points.data(), points.size(), indices.data(), indices.size(),
            subprims.data(), subprims.size(), mappings.data(), mappings.size(),
            scratch.data(), scratch.size() - 1) != HdSilkPtexMappingStatus::InvalidBuffers ||
        write(mesh, source, points.data(), points.size(), indices.data(), indices.size(),
            subprims.data(), subprims.size(), mappings.data(), mappings.size(),
            indices.data(), scratch.size()) != HdSilkPtexMappingStatus::InvalidBuffers ||
        write(mesh, source, points.data(), points.size(), indices.data(), indices.size(),
            indices.data(), subprims.size(), mappings.data(), mappings.size(),
            scratch.data(), scratch.size()) != HdSilkPtexMappingStatus::InvalidBuffers ||
        write(mesh, source, points.data(), points.size(),
            reinterpret_cast<uint32_t*>(points.data()), indices.size(),
            subprims.data(), subprims.size(), mappings.data(), mappings.size(),
            scratch.data(), scratch.size()) != HdSilkPtexMappingStatus::InvalidBuffers ||
        write(mesh, source, const_cast<GfVec3f*>(source.cdata()), points.size(),
            indices.data(), indices.size(), subprims.data(), subprims.size(),
            mappings.data(), mappings.size(), scratch.data(), scratch.size()) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        write(mesh, source, points.data(), points.size(), indices.data(), indices.size(),
            subprims.data(), subprims.size(), mappings.data(), mappings.size(),
            reinterpret_cast<uint32_t*>(const_cast<GfVec3f*>(source.cdata())), scratch.size()) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        write(mesh, source, points.data(), points.size(), indices.data(), indices.size(),
            subprims.data(), subprims.size(), mappings.data(), mappings.size(),
            reinterpret_cast<uint32_t*>(
                const_cast<int*>(mesh.GetFaceVertexIndices().cdata())), scratch.size()) !=
            HdSilkPtexMappingStatus::InvalidBuffers)
    {
        return Fail(topology.name, "invalid or overlapping buffers were accepted");
    }
    const auto outputsUnchanged = [&]()
    {
        return std::all_of(points.begin(), points.end(),
            [&](const GfVec3f& value) { return value == sentinel; }) &&
            std::all_of(indices.begin(), indices.end(), [](uint32_t value) { return value == 99; }) &&
            std::all_of(subprims.begin(), subprims.end(), [](uint32_t value) { return value == 99; }) &&
            std::all_of(mappings.begin(), mappings.end(), [&](const HdSilkPtexTriangle& value)
            {
                return value.faceIndex == mappingSentinel.faceIndex &&
                    value.cornerCoordinates == mappingSentinel.cornerCoordinates;
            });
    };
    if (!outputsUnchanged() ||
        !std::all_of(scratch.begin(), scratch.end(), [](uint32_t value) { return value == 99; }))
    {
        return Fail(topology.name, "buffer refusal changed an output");
    }
    VtVec3fArray invalidPoints = source;
    invalidPoints[2][1] = std::numeric_limits<float>::quiet_NaN();
    if (defaultWrite(mesh, invalidPoints) != HdSilkPtexMappingStatus::InvalidPoints ||
        !outputsUnchanged() ||
        !std::all_of(scratch.begin(), scratch.end(), [](uint32_t value) { return value == 99; }))
    {
        return Fail(topology.name, "non-finite source points were accepted");
    }
    invalidPoints[2][1] = std::numeric_limits<float>::infinity();
    if (defaultWrite(mesh, invalidPoints) != HdSilkPtexMappingStatus::InvalidPoints ||
        !outputsUnchanged())
    {
        return Fail(topology.name, "infinite source points were accepted");
    }
    const TestTopology duplicate{
        "duplicate corner", Sdc::SCHEME_CATMARK, 5,
        {5}, {0, 1, 2, 1, 4}, {}, false};
    TestTopology duplicateHole = duplicate;
    duplicateHole.holes = {0};
    if (defaultWrite(MakeMeshTopology(duplicate), source) !=
            HdSilkPtexMappingStatus::UnsupportedTopology ||
        !outputsUnchanged() ||
        defaultWrite(MakeMeshTopology(duplicateHole), source) !=
            HdSilkPtexMappingStatus::UnsupportedTopology ||
        !outputsUnchanged() ||
        defaultWrite(mesh, source) != HdSilkPtexMappingStatus::Mapped)
    {
        return Fail(topology.name, "invalid topology or corrected retry was mishandled");
    }
    for (const GfVec3f& point : points)
    {
        if (point != source[0])
        {
            return Fail(topology.name, "successful retry did not generate the expected points");
        }
    }
    return true;
}

bool TestCoarseTriQuadPlanningRefusals()
{
    const TfToken scheme = PxOsdOpenSubdivTokens->catmullClark;
    const TfToken orientation = PxOsdOpenSubdivTokens->rightHanded;
    const HdMeshTopology valid(
        scheme, orientation, VtIntArray{5}, VtIntArray{0, 1, 2, 3, 4}, VtIntArray{});
    const HdMeshTopology polygonal(
        PxOsdOpenSubdivTokens->none, orientation,
        VtIntArray{3}, VtIntArray{0, 1, 2}, VtIntArray{});
    const HdMeshTopology loop(
        PxOsdOpenSubdivTokens->loop, orientation,
        VtIntArray{3}, VtIntArray{0, 1, 2}, VtIntArray{});
    const HdMeshTopology unknownOrientation(
        scheme, TfToken("unknown"), VtIntArray{3}, VtIntArray{0, 1, 2}, VtIntArray{});
    const HdMeshTopology shortFace(
        scheme, orientation, VtIntArray{2}, VtIntArray{0, 1}, VtIntArray{});
    const HdMeshTopology incomplete(
        scheme, orientation, VtIntArray{4}, VtIntArray{0, 1, 2}, VtIntArray{});
    const HdMeshTopology extra(
        scheme, orientation, VtIntArray{3}, VtIntArray{0, 1, 2, 3}, VtIntArray{});
    const HdMeshTopology negative(
        scheme, orientation, VtIntArray{3}, VtIntArray{0, -1, 2}, VtIntArray{});
    const HdMeshTopology unsortedHoles(
        scheme, orientation, VtIntArray{3, 3}, VtIntArray{0, 1, 2, 3, 4, 5},
        VtIntArray{1, 0});
    const HdMeshTopology duplicateHoles(
        scheme, orientation, VtIntArray{3, 3}, VtIntArray{0, 1, 2, 3, 4, 5},
        VtIntArray{1, 1});
    const HdMeshTopology missingHole(
        scheme, orientation, VtIntArray{3}, VtIntArray{0, 1, 2}, VtIntArray{1});
    const HdMeshTopology negativeHole(
        scheme, orientation, VtIntArray{3}, VtIntArray{0, 1, 2}, VtIntArray{-1});
    const HdMeshTopology sparseOverflow(
        scheme, orientation, VtIntArray{3},
        VtIntArray{std::numeric_limits<int>::max() - 1, 0, 1}, VtIntArray{});
    const HdMeshTopology indexOverflow(
        scheme, orientation, VtIntArray{3},
        VtIntArray{std::numeric_limits<int>::max(), 0, 1}, VtIntArray{});

    const auto expect = [](
        const HdMeshTopology& mesh, size_t pointCount, HdSilkPtexMappingStatus status)
    {
        HdSilkPtexCoarseTriQuadPlan plan{17, 18, 19, 20};
        return HdSilkPlanDefaultCoarsePtexTriQuads(mesh, pointCount, &plan) == status &&
            plan.inputPointCount == 17 && plan.outputPointCount == 18 &&
            plan.triangleCount == 19 && plan.scratchCount == 20;
    };
    for (const HdMeshTopology* unsupported :
         {&polygonal, &loop, &unknownOrientation, &shortFace, &incomplete, &extra,
             &negative, &unsortedHoles, &duplicateHoles, &missingHole, &negativeHole})
    {
        if (!expect(*unsupported, 8, HdSilkPtexMappingStatus::UnsupportedTopology))
        {
            return Fail("tri-quad planning", "invalid topology or holes were accepted");
        }
    }
    const size_t maxIndex = static_cast<size_t>(std::numeric_limits<int>::max());
    if (!expect(valid, 4, HdSilkPtexMappingStatus::InvalidPoints) ||
        !expect(sparseOverflow, maxIndex, HdSilkPtexMappingStatus::ExceedsBounds) ||
        !expect(indexOverflow, maxIndex + 1, HdSilkPtexMappingStatus::ExceedsBounds))
    {
        return Fail("tri-quad planning", "point or count bounds were not enforced");
    }
    return true;
}

bool TestCoarseTriQuadEmptyHolesAndExtremePoints()
{
    const TfToken scheme = PxOsdOpenSubdivTokens->catmullClark;
    const TfToken orientation = PxOsdOpenSubdivTokens->rightHanded;
    const HdMeshTopology empty(scheme, orientation, VtIntArray{}, VtIntArray{}, VtIntArray{});
    HdSilkPtexCoarseTriQuadPlan plan;
    if (HdSilkPlanDefaultCoarsePtexTriQuads(empty, 0, &plan) !=
            HdSilkPtexMappingStatus::Mapped ||
        plan.inputPointCount != 0 || plan.outputPointCount != 0 ||
        plan.triangleCount != 0 || plan.scratchCount != 0 ||
        HdSilkWriteDefaultCoarsePtexTriQuads(
            empty, VtVec3fArray{}, nullptr, 0, nullptr, 0, nullptr, 0,
            nullptr, 0, nullptr, 0) != HdSilkPtexMappingStatus::Mapped)
    {
        return Fail("empty tri-quad", "an empty mesh did not produce an empty plan");
    }

    const HdMeshTopology hole(
        scheme, orientation, VtIntArray{4}, VtIntArray{0, 1, 2, 3}, VtIntArray{0});
    const VtVec3fArray source(4, GfVec3f(2.0F, 3.0F, 4.0F));
    if (HdSilkPlanDefaultCoarsePtexTriQuads(hole, source.size(), &plan) !=
            HdSilkPtexMappingStatus::Mapped ||
        plan.inputPointCount != 4 || plan.outputPointCount != 4 ||
        plan.triangleCount != 0 || plan.scratchCount != 4)
    {
        return Fail("holed tri-quad", "a fully holed mesh produced geometry");
    }
    std::vector<GfVec3f> copied(plan.outputPointCount);
    std::vector<uint32_t> scratch(plan.scratchCount);
    if (HdSilkWriteDefaultCoarsePtexTriQuads(
            hole, source, copied.data(), copied.size(), nullptr, 0, nullptr, 0,
            nullptr, 0, scratch.data(), scratch.size()) !=
            HdSilkPtexMappingStatus::Mapped ||
        !std::all_of(copied.begin(), copied.end(),
            [&](const GfVec3f& point) { return point == source[0]; }))
    {
        return Fail("holed tri-quad", "a fully holed mesh did not copy its base points");
    }

    const HdMeshTopology pentagon(
        scheme, orientation, VtIntArray{5}, VtIntArray{0, 1, 2, 3, 4}, VtIntArray{});
    VtVec3fArray extreme(6, GfVec3f(std::numeric_limits<float>::max()));
    extreme[5][0] = std::numeric_limits<float>::quiet_NaN();
    if (HdSilkPlanDefaultCoarsePtexTriQuads(pentagon, extreme.size(), &plan) !=
        HdSilkPtexMappingStatus::Mapped)
    {
        return Fail("extreme tri-quad", "finite referenced points were refused");
    }
    std::vector<GfVec3f> points(plan.outputPointCount);
    std::vector<uint32_t> indices(plan.triangleCount * 3);
    std::vector<uint32_t> subprims(plan.triangleCount);
    std::vector<HdSilkPtexTriangle> mappings(plan.triangleCount);
    scratch.resize(plan.scratchCount);
    if (HdSilkWriteDefaultCoarsePtexTriQuads(
            pentagon, extreme, points.data(), points.size(), indices.data(), indices.size(),
            subprims.data(), subprims.size(), mappings.data(), mappings.size(),
            scratch.data(), scratch.size()) != HdSilkPtexMappingStatus::Mapped ||
        !std::all_of(points.begin(), points.end(),
            [&](const GfVec3f& point) { return point == extreme[0]; }))
    {
        return Fail("extreme tri-quad", "finite midpoint or centre overflowed");
    }
    return true;
}

bool TestLargeCoarseTriQuads()
{
    constexpr int cornerCount = 1024;
    for (bool leftHanded : {false, true})
    {
        TestTopology polygon{
            leftHanded ? "large left tri-quad" : "large tri-quad",
            Sdc::SCHEME_CATMARK, cornerCount, {cornerCount}, {}, {}, leftHanded};
        polygon.indices.reserve(cornerCount);
        for (int corner = 0; corner < cornerCount; ++corner)
        {
            polygon.indices.push_back(corner);
        }
        if (!TestCoarseTriQuadsMatchHydraAndOpenSubdiv(polygon))
        {
            return false;
        }
    }
    return true;
}

bool TestCoarseAnalyticOrientation()
{
    const TestTopology topology{
        "coarse left-handed quad", Sdc::SCHEME_CATMARK, 4,
        {4}, {0, 1, 2, 3}, {}, true};
    const HdMeshTopology mesh = MakeMeshTopology(topology);
    const CoarseTriangles emitted = TriangulateCoarse(mesh);
    std::vector<HdSilkPtexTriangle> triangles(emitted.subprims.size());
    std::vector<uint32_t> scratch(HdSilkCoarsePtexScratchCount(mesh));
    if (HdSilkWriteDefaultCoarsePtexTriangles(
            mesh, emitted.indices, emitted.subprims,
            triangles.data(), triangles.size(), scratch.data(), scratch.size()) !=
        HdSilkPtexMappingStatus::Mapped)
    {
        return Fail(topology.name, "mapping failed");
    }
    const std::array<float, 6> first{{1.0F, 1.0F, 0.0F, 1.0F, 0.0F, 0.0F}};
    const std::array<float, 6> second{{1.0F, 1.0F, 0.0F, 0.0F, 1.0F, 0.0F}};
    if (triangles.size() != 2 || triangles[0].faceIndex != 0 ||
        triangles[0].cornerCoordinates != first ||
        triangles[1].cornerCoordinates != second)
    {
        return Fail(topology.name, "left-handed coordinates are incorrectly oriented");
    }
    return true;
}

bool TestCoarseRefusalAndRetry()
{
    const TestTopology quad{
        "coarse buffer admission", Sdc::SCHEME_CATMARK, 4,
        {4}, {0, 1, 2, 3}, {}, false};
    const HdMeshTopology mesh = MakeMeshTopology(quad);
    const CoarseTriangles emitted = TriangulateCoarse(mesh);
    HdSilkPtexTriangle sentinel;
    sentinel.faceIndex = 99;
    sentinel.cornerCoordinates.fill(-1.0F);
    std::vector<HdSilkPtexTriangle> triangles(emitted.subprims.size(), sentinel);
    std::vector<uint32_t> scratch(HdSilkCoarsePtexScratchCount(mesh), 99);
    const auto write = [&](const CoarseTriangles& source)
    {
        return HdSilkWriteDefaultCoarsePtexTriangles(
            mesh, source.indices, source.subprims,
            triangles.data(), triangles.size(), scratch.data(), scratch.size());
    };
    if (HdSilkWriteDefaultCoarsePtexTriangles(
            mesh, emitted.indices, emitted.subprims,
            triangles.data(), triangles.size() - 1, scratch.data(), scratch.size()) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        HdSilkWriteDefaultCoarsePtexTriangles(
            mesh, emitted.indices, emitted.subprims,
            triangles.data(), triangles.size(), scratch.data(), scratch.size() - 1) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        HdSilkWriteDefaultCoarsePtexTriangles(
            mesh, emitted.indices, emitted.subprims,
            nullptr, triangles.size(), scratch.data(), scratch.size()) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        HdSilkWriteDefaultCoarsePtexTriangles(
            mesh, emitted.indices, emitted.subprims,
            triangles.data(), triangles.size(),
            reinterpret_cast<uint32_t*>(triangles.data()), scratch.size()) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        HdSilkWriteDefaultCoarsePtexTriangles(
            mesh, emitted.indices, emitted.subprims,
            triangles.data(), triangles.size(),
            const_cast<uint32_t*>(emitted.indices.cdata()), scratch.size()) !=
            HdSilkPtexMappingStatus::InvalidBuffers)
    {
        return Fail(quad.name, "invalid or overlapping buffers were accepted");
    }
    if (std::any_of(
            triangles.begin(), triangles.end(),
            [&](const HdSilkPtexTriangle& value)
            {
                return value.faceIndex != sentinel.faceIndex ||
                    value.cornerCoordinates != sentinel.cornerCoordinates;
            }) ||
        std::any_of(scratch.begin(), scratch.end(), [](uint32_t value)
            {
                return value != 99;
            }))
    {
        return Fail(quad.name, "buffer refusal modified caller storage");
    }

    CoarseTriangles changed = emitted;
    std::swap(changed.indices[0], changed.indices[1]);
    if (write(changed) != HdSilkPtexMappingStatus::TriangleMismatch)
    {
        return Fail(quad.name, "changed triangle winding was accepted");
    }
    changed = emitted;
    changed.subprims[0] = 1;
    if (write(changed) != HdSilkPtexMappingStatus::TriangleMismatch)
    {
        return Fail(quad.name, "unknown face identity was accepted");
    }
    changed = emitted;
    changed.indices.resize(3);
    changed.subprims.resize(1);
    if (write(changed) != HdSilkPtexMappingStatus::TriangleMismatch)
    {
        return Fail(quad.name, "missing triangle was accepted");
    }
    if (write(emitted) != HdSilkPtexMappingStatus::Mapped ||
        triangles[0].faceIndex != 0 ||
        triangles[0].cornerCoordinates !=
            std::array<float, 6>{{0.0F, 0.0F, 1.0F, 0.0F, 1.0F, 1.0F}})
    {
        return Fail(quad.name, "retry after refusal did not succeed");
    }

    const TestTopology pentagon{
        "coarse non-quad", Sdc::SCHEME_CATMARK, 5,
        {5}, {0, 1, 2, 3, 4}, {}, false};
    const HdMeshTopology nonQuad = MakeMeshTopology(pentagon);
    const CoarseTriangles fan = TriangulateCoarse(nonQuad);
    std::vector<HdSilkPtexTriangle> nonQuadTriangles(fan.subprims.size());
    std::vector<uint32_t> nonQuadScratch(HdSilkCoarsePtexScratchCount(nonQuad));
    if (HdSilkWriteDefaultCoarsePtexTriangles(
            nonQuad, fan.indices, fan.subprims,
            nonQuadTriangles.data(), nonQuadTriangles.size(),
            nonQuadScratch.data(), nonQuadScratch.size()) !=
        HdSilkPtexMappingStatus::UnsupportedTopology)
    {
        return Fail(pentagon.name, "an unsplit non-quad was accepted");
    }

    const HdMeshTopology polygonal(
        PxOsdOpenSubdivTokens->none, PxOsdOpenSubdivTokens->rightHanded,
        VtIntArray{4}, VtIntArray{0, 1, 2, 3}, VtIntArray{});
    const HdMeshTopology unknownOrientation(
        PxOsdOpenSubdivTokens->catmullClark, TfToken("unknown"),
        VtIntArray{4}, VtIntArray{0, 1, 2, 3}, VtIntArray{});
    const HdMeshTopology incomplete(
        PxOsdOpenSubdivTokens->catmullClark, PxOsdOpenSubdivTokens->rightHanded,
        VtIntArray{4}, VtIntArray{0, 1, 2}, VtIntArray{});
    const HdMeshTopology repeated(
        PxOsdOpenSubdivTokens->catmullClark, PxOsdOpenSubdivTokens->rightHanded,
        VtIntArray{4}, VtIntArray{0, 1, 1, 3}, VtIntArray{});
    for (const HdMeshTopology* unsupported :
         {&polygonal, &unknownOrientation, &incomplete, &repeated})
    {
        if (HdSilkWriteDefaultCoarsePtexTriangles(
                *unsupported, emitted.indices, emitted.subprims,
                triangles.data(), triangles.size(), scratch.data(), scratch.size()) !=
            HdSilkPtexMappingStatus::UnsupportedTopology)
        {
            return Fail(quad.name, "invalid or unsupported topology was accepted");
        }
    }

    const HdMeshTopology unsortedHoles(
        PxOsdOpenSubdivTokens->catmullClark, PxOsdOpenSubdivTokens->rightHanded,
        VtIntArray{4, 4}, VtIntArray{0, 1, 2, 3, 4, 5, 6, 7}, VtIntArray{1, 0});
    std::vector<uint32_t> unsortedScratch(HdSilkCoarsePtexScratchCount(unsortedHoles));
    if (HdSilkWriteDefaultCoarsePtexTriangles(
            unsortedHoles, emitted.indices, emitted.subprims,
            triangles.data(), triangles.size(), unsortedScratch.data(), unsortedScratch.size()) !=
        HdSilkPtexMappingStatus::UnsupportedTopology)
    {
        return Fail(quad.name, "unsorted hole indices were accepted");
    }
    if (write(emitted) != HdSilkPtexMappingStatus::Mapped)
    {
        return Fail(quad.name, "valid topology did not recover after refusal");
    }

    const TestTopology holed{
        "fully holed coarse quad", Sdc::SCHEME_CATMARK, 4,
        {4}, {0, 1, 2, 3}, {0}, false};
    const HdMeshTopology hole = MakeMeshTopology(holed);
    const CoarseTriangles empty = TriangulateCoarse(hole);
    std::vector<uint32_t> holeScratch(HdSilkCoarsePtexScratchCount(hole));
    if (!empty.indices.empty() || !empty.subprims.empty() ||
        HdSilkWriteDefaultCoarsePtexTriangles(
            hole, empty.indices, empty.subprims, nullptr, 0,
            holeScratch.data(), holeScratch.size()) !=
            HdSilkPtexMappingStatus::Mapped)
    {
        return Fail(holed.name, "a fully holed cage did not produce an empty mapping");
    }
    return true;
}

bool TestPtexCoordinatesMatchOpenSubdiv(
    const TestTopology& topology,
    int level)
{
    const std::string context = std::string(topology.name) +
        " level " + std::to_string(level);
    HdSilkMeshSubdivision subdivision;
    std::string diagnostic;
    if (subdivision.Rebuild(MakeMeshTopology(topology), level, {}, &diagnostic) !=
        HdSilkSubdivisionStatus::Refined)
    {
        return Fail(context, "subdivision refused: " + diagnostic);
    }

    std::vector<HdSilkPtexTriangle> triangles(
        subdivision.GetTriangleSubprims().size());
    std::vector<uint32_t> offsets(subdivision.GetPtexScratchCount());
    const HdSilkPtexMappingStatus status = subdivision.WriteDefaultPtexTriangles(
        triangles.data(), triangles.size(), offsets.data(), offsets.size());
    if (status != HdSilkPtexMappingStatus::Mapped)
    {
        return Fail(
            context,
            std::string("Ptex coordinate mapping was refused: ") +
                HdSilkPtexMappingStatusName(status));
    }

    const std::unique_ptr<Far::TopologyRefiner> oracle =
        MakeOracleRefiner(topology, level);
    if (!oracle)
    {
        return Fail(context, "oracle refiner could not be created");
    }
    const std::unique_ptr<Far::PatchTable> patches = MakeOraclePatchTable(*oracle);
    if (!patches)
    {
        return Fail(context, "oracle patch table could not be created");
    }
    const Far::PtexIndices ptexIndices(*oracle);
    if (offsets.size() != topology.counts.size() +
            static_cast<size_t>(oracle->GetLevel(1).GetNumFaces()))
    {
        return Fail(context, "incorrect scratch face count");
    }
    for (size_t face = 0; face < topology.counts.size(); ++face)
    {
        if (offsets[face] !=
            static_cast<uint32_t>(ptexIndices.GetFaceId(static_cast<int>(face))))
        {
            return Fail(context, "coarse face offset differs from OpenSubdiv");
        }
    }

    const Far::TopologyLevel& last = oracle->GetLevel(level);
    size_t triangleIndex = 0;
    int matchedPatches = 0;
    for (int face = 0; face < last.GetNumFaces(); ++face)
    {
        if (last.IsFaceHole(face))
        {
            continue;
        }
        const Far::ConstIndexArray vertices = last.GetFaceVertices(face);
        Far::PatchParam param;
        if (!FindPatch(*patches, vertices, &param))
        {
            return Fail(context, "last-level face does not have one matching patch");
        }
        ++matchedPatches;
        std::array<std::array<float, 2>, 4> expected{{
            {{0.0F, 0.0F}}, {{1.0F, 0.0F}},
            {{1.0F, 1.0F}}, {{0.0F, 1.0F}}}};
        if (topology.scheme == Sdc::SCHEME_LOOP)
        {
            expected[2] = {{0.0F, 1.0F}};
        }
        for (int corner = 0; corner < vertices.size(); ++corner)
        {
            float& u = expected[static_cast<size_t>(corner)][0];
            float& v = expected[static_cast<size_t>(corner)][1];
            if (topology.scheme == Sdc::SCHEME_LOOP)
            {
                param.UnnormalizeTriangle(u, v);
            }
            else
            {
                param.Unnormalize(u, v);
            }
        }

        for (int corner = 1; corner + 1 < vertices.size(); ++corner)
        {
            if (triangleIndex >= triangles.size())
            {
                return Fail(context, "triangle output was truncated");
            }
            const HdSilkPtexTriangle& triangle = triangles[triangleIndex];
            const std::array<float, 6> expectedCorners{{
                expected[0][0], expected[0][1],
                expected[static_cast<size_t>(corner)][0],
                expected[static_cast<size_t>(corner)][1],
                expected[static_cast<size_t>(corner + 1)][0],
                expected[static_cast<size_t>(corner + 1)][1]}};
            if (triangle.faceIndex != static_cast<uint32_t>(param.GetFaceId()) ||
                triangle.cornerCoordinates != expectedCorners)
            {
                return Fail(
                    context,
                    "Ptex face or corner coordinates differ at triangle " +
                        std::to_string(triangleIndex));
            }
            ++triangleIndex;
        }
    }
    if (triangleIndex != triangles.size() ||
        matchedPatches != patches->GetNumPatchesTotal())
    {
        return Fail(context, "triangle or patch counts do not reconcile");
    }
    const VtArray<uint32_t>& sourceFaces = subdivision.GetTriangleSubprims();
    return TestPtexOffsetsFollowSourceFaces(
        context, triangles, sourceFaces.cdata(), sourceFaces.size(), topology.counts.size());
}

bool TestAnalyticQuadAndRotatedLoop()
{
    const TestTopology quad{
        "analytic quad", Sdc::SCHEME_CATMARK, 4,
        {4}, {0, 1, 2, 3}, {}, false};
    HdSilkMeshSubdivision subdivision;
    std::string diagnostic;
    if (subdivision.Rebuild(MakeMeshTopology(quad), 1, {}, &diagnostic) !=
        HdSilkSubdivisionStatus::Refined)
    {
        return Fail(quad.name, diagnostic);
    }
    std::vector<HdSilkPtexTriangle> triangles(
        subdivision.GetTriangleSubprims().size());
    std::vector<uint32_t> offsets(subdivision.GetPtexScratchCount());
    if (subdivision.WriteDefaultPtexTriangles(
            triangles.data(), triangles.size(), offsets.data(), offsets.size()) !=
        HdSilkPtexMappingStatus::Mapped)
    {
        return Fail(quad.name, "mapping failed");
    }
    const std::array<float, 6> first{{0.0F, 0.0F, 0.5F, 0.0F, 0.5F, 0.5F}};
    const std::array<float, 6> second{{0.0F, 0.0F, 0.5F, 0.5F, 0.0F, 0.5F}};
    if (triangles.size() != 8 || triangles[0].faceIndex != 0 ||
        triangles[0].cornerCoordinates != first ||
        triangles[1].cornerCoordinates != second)
    {
        return Fail(quad.name, "first refined quad has incorrect corner coordinates");
    }

    const TestTopology loop{
        "analytic loop", Sdc::SCHEME_LOOP, 3,
        {3}, {0, 1, 2}, {}, false};
    if (subdivision.Rebuild(MakeMeshTopology(loop), 2, {}, &diagnostic) !=
        HdSilkSubdivisionStatus::Refined)
    {
        return Fail(loop.name, diagnostic);
    }
    triangles.assign(subdivision.GetTriangleSubprims().size(), {});
    offsets.assign(subdivision.GetPtexScratchCount(), 0);
    if (subdivision.WriteDefaultPtexTriangles(
            triangles.data(), triangles.size(), offsets.data(), offsets.size()) !=
        HdSilkPtexMappingStatus::Mapped)
    {
        return Fail(loop.name, "mapping failed");
    }
    const std::array<float, 6> rotated{{
        0.25F, 0.25F, 0.0F, 0.25F, 0.25F, 0.0F}};
    if (triangles.size() != 16 || triangles[3].faceIndex != 0 ||
        triangles[3].cornerCoordinates != rotated)
    {
        return Fail(loop.name, "rotated triangular patch is incorrectly oriented");
    }
    return true;
}

bool TestBufferRefusalAndRetry()
{
    const TestTopology quad{
        "buffer admission", Sdc::SCHEME_CATMARK, 4,
        {4}, {0, 1, 2, 3}, {}, false};
    HdSilkMeshSubdivision subdivision;
    std::string diagnostic;
    if (subdivision.Rebuild(MakeMeshTopology(quad), 2, {}, &diagnostic) !=
        HdSilkSubdivisionStatus::Refined)
    {
        return Fail(quad.name, diagnostic);
    }
    HdSilkPtexTriangle sentinel;
    sentinel.faceIndex = 99;
    sentinel.cornerCoordinates.fill(-1.0F);
    std::vector<HdSilkPtexTriangle> triangles(
        subdivision.GetTriangleSubprims().size(), sentinel);
    std::vector<uint32_t> offsets(subdivision.GetPtexScratchCount(), 99);
    if (subdivision.WriteDefaultPtexTriangles(
            triangles.data(), triangles.size() - 1, offsets.data(), offsets.size()) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        subdivision.WriteDefaultPtexTriangles(
            triangles.data(), triangles.size(), offsets.data(), offsets.size() - 1) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        subdivision.WriteDefaultPtexTriangles(
            nullptr, triangles.size(), offsets.data(), offsets.size()) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        subdivision.WriteDefaultPtexTriangles(
            triangles.data(), triangles.size(), nullptr, offsets.size()) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        subdivision.WriteDefaultPtexTriangles(
            triangles.data(), triangles.size(),
            reinterpret_cast<uint32_t*>(triangles.data()), offsets.size()) !=
            HdSilkPtexMappingStatus::InvalidBuffers)
    {
        return Fail(quad.name, "invalid buffers were not refused");
    }
    if (std::any_of(
            triangles.begin(), triangles.end(),
            [&](const HdSilkPtexTriangle& value)
            {
                return value.faceIndex != sentinel.faceIndex ||
                    value.cornerCoordinates != sentinel.cornerCoordinates;
            }) ||
        std::any_of(offsets.begin(), offsets.end(), [](uint32_t value)
            {
                return value != 99;
            }))
    {
        return Fail(quad.name, "buffer refusal modified caller storage");
    }
    if (subdivision.WriteDefaultPtexTriangles(
            triangles.data(), triangles.size(), offsets.data(), offsets.size()) !=
            HdSilkPtexMappingStatus::Mapped ||
        offsets[0] != 0 || triangles[0].faceIndex != 0 ||
        triangles[0].cornerCoordinates[0] != 0.0F)
    {
        return Fail(quad.name, "retry after buffer refusal did not succeed");
    }

    subdivision.Clear();
    if (subdivision.GetPtexScratchCount() != 0 ||
        subdivision.WriteDefaultPtexTriangles(
            triangles.data(), triangles.size(), offsets.data(), offsets.size()) !=
            HdSilkPtexMappingStatus::RefinerUnavailable)
    {
        return Fail(quad.name, "cleared refiner accepted a mapping");
    }
    return true;
}

bool TestLargePolygon()
{
    constexpr int cornerCount = 1024;
    TestTopology polygon{
        "large polygon", Sdc::SCHEME_CATMARK, cornerCount,
        {cornerCount}, {}, {}, false};
    polygon.indices.reserve(cornerCount);
    for (int corner = 0; corner < cornerCount; ++corner)
    {
        polygon.indices.push_back(corner);
    }
    HdSilkMeshSubdivision subdivision;
    std::string diagnostic;
    if (subdivision.Rebuild(MakeMeshTopology(polygon), 2, {}, &diagnostic) !=
        HdSilkSubdivisionStatus::Refined)
    {
        return Fail(polygon.name, diagnostic);
    }
    std::vector<HdSilkPtexTriangle> triangles(
        subdivision.GetTriangleSubprims().size());
    std::vector<uint32_t> scratch(subdivision.GetPtexScratchCount());
    if (subdivision.WriteDefaultPtexTriangles(
            triangles.data(), triangles.size(), scratch.data(), scratch.size()) !=
            HdSilkPtexMappingStatus::Mapped ||
        triangles.size() != static_cast<size_t>(cornerCount) * 8)
    {
        return Fail(polygon.name, "mapping or triangle count is incorrect");
    }
    const std::array<float, 6> first{{0.0F, 0.0F, 0.5F, 0.0F, 0.5F, 0.5F}};
    for (int corner = 0; corner < cornerCount; ++corner)
    {
        const HdSilkPtexTriangle& triangle =
            triangles[static_cast<size_t>(corner) * 8];
        if (triangle.faceIndex != static_cast<uint32_t>(corner) ||
            triangle.cornerCoordinates != first)
        {
            return Fail(polygon.name, "a Ptex subface was misidentified");
        }
    }
    return true;
}

bool TestUnrenderableBoundaryRefusal()
{
    const TestTopology quad{
        "open boundary none", Sdc::SCHEME_CATMARK, 4,
        {4}, {0, 1, 2, 3}, {}, false, true};
    HdSilkMeshSubdivision subdivision;
    std::string diagnostic;
    if (subdivision.Rebuild(MakeMeshTopology(quad), 1, {}, &diagnostic) !=
            HdSilkSubdivisionStatus::InvalidTopology ||
        diagnostic.empty())
    {
        return Fail(quad.name, "unrenderable surface was not refused");
    }
    HdSilkPtexTriangle triangle;
    uint32_t offset = 0;
    if (subdivision.WriteDefaultPtexTriangles(
            &triangle, 1, &offset, 1) !=
        HdSilkPtexMappingStatus::RefinerUnavailable)
    {
        return Fail(quad.name, "unrenderable surface accepted a Ptex mapping");
    }
    return true;
}
}

int main()
{
    const std::vector<TestTopology> coarseTriQuadTopologies{
        {"tri-quad quad", Sdc::SCHEME_CATMARK, 4,
            {4}, {0, 1, 2, 3}, {}, false},
        {"tri-quad left quad", Sdc::SCHEME_CATMARK, 4,
            {4}, {0, 1, 2, 3}, {}, true},
        {"tri-quad triangle", Sdc::SCHEME_CATMARK, 3,
            {3}, {0, 1, 2}, {}, false},
        {"tri-quad left triangle", Sdc::SCHEME_CATMARK, 3,
            {3}, {0, 1, 2}, {}, true},
        {"tri-quad sparse points", Sdc::SCHEME_CATMARK, 9,
            {3}, {0, 4, 8}, {}, false},
        {"tri-quad pentagon", Sdc::SCHEME_CATMARK, 5,
            {5}, {0, 1, 2, 3, 4}, {}, false},
        {"tri-quad left pentagon", Sdc::SCHEME_CATMARK, 5,
            {5}, {0, 1, 2, 3, 4}, {}, true},
        {"tri-quad adjacent mixed", Sdc::SCHEME_CATMARK, 5,
            {3, 4}, {0, 1, 2, 2, 1, 3, 4}, {}, false},
        {"tri-quad left adjacent mixed", Sdc::SCHEME_CATMARK, 5,
            {3, 4}, {0, 1, 2, 2, 1, 3, 4}, {}, true},
        {"tri-quad mixed holes", Sdc::SCHEME_CATMARK, 17,
            {5, 3, 4, 5},
            {0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16},
            {0, 2}, false},
        {"tri-quad left mixed holes", Sdc::SCHEME_BILINEAR, 17,
            {5, 3, 4, 5},
            {0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16},
            {0, 2}, true},
        {"tri-quad bilinear mixed", Sdc::SCHEME_BILINEAR, 12,
            {3, 4, 5}, {0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11}, {}, false},
        {"tri-quad left bilinear mixed", Sdc::SCHEME_BILINEAR, 12,
            {3, 4, 5}, {0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11}, {}, true}};
    const std::vector<TestTopology> coarseTopologies{
        {"coarse quad", Sdc::SCHEME_CATMARK, 4,
            {4}, {0, 1, 2, 3}, {}, false},
        {"coarse left quad", Sdc::SCHEME_CATMARK, 4,
            {4}, {0, 1, 2, 3}, {}, true},
        {"coarse bilinear quad", Sdc::SCHEME_BILINEAR, 4,
            {4}, {0, 1, 2, 3}, {}, false},
        {"coarse adjacent quads", Sdc::SCHEME_CATMARK, 6,
            {4, 4}, {0, 1, 2, 3, 1, 4, 5, 2}, {}, false},
        {"coarse left adjacent quads", Sdc::SCHEME_CATMARK, 6,
            {4, 4}, {0, 1, 2, 3, 1, 4, 5, 2}, {}, true},
        {"coarse loop", Sdc::SCHEME_LOOP, 3,
            {3}, {0, 1, 2}, {}, false},
        {"coarse left loop", Sdc::SCHEME_LOOP, 3,
            {3}, {0, 1, 2}, {}, true},
        {"coarse loop tetrahedron", Sdc::SCHEME_LOOP, 4,
            {3, 3, 3, 3}, {0, 2, 1, 0, 1, 3, 1, 2, 3, 2, 0, 3}, {}, false},
        {"coarse first-face hole", Sdc::SCHEME_CATMARK, 6,
            {4, 4}, {0, 1, 2, 3, 1, 4, 5, 2}, {0}, false},
        {"coarse left first-face hole", Sdc::SCHEME_CATMARK, 6,
            {4, 4}, {0, 1, 2, 3, 1, 4, 5, 2}, {0}, true},
        {"coarse non-quad hole prefix", Sdc::SCHEME_CATMARK, 9,
            {5, 4}, {0, 1, 2, 3, 4, 5, 6, 7, 8}, {0}, false},
        {"coarse left non-quad hole prefix", Sdc::SCHEME_BILINEAR, 9,
            {5, 4}, {0, 1, 2, 3, 4, 5, 6, 7, 8}, {0}, true}};
    const std::vector<TestTopology> topologies{
        {"quad", Sdc::SCHEME_CATMARK, 4, {4}, {0, 1, 2, 3}, {}, false},
        {"left-handed quad", Sdc::SCHEME_CATMARK, 4,
            {4}, {0, 1, 2, 3}, {}, true},
        {"pentagon", Sdc::SCHEME_CATMARK, 5,
            {5}, {0, 1, 2, 3, 4}, {}, false},
        {"left-handed pentagon", Sdc::SCHEME_CATMARK, 5,
            {5}, {0, 1, 2, 3, 4}, {}, true},
        {"mixed polygons", Sdc::SCHEME_CATMARK, 12,
            {3, 4, 5}, {0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11}, {}, false},
        {"left-handed mixed polygons", Sdc::SCHEME_CATMARK, 12,
            {3, 4, 5}, {0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11}, {}, true},
        {"hole", Sdc::SCHEME_CATMARK, 6,
            {4, 4}, {0, 1, 2, 3, 1, 4, 5, 2}, {1}, false},
        {"left-handed first-face hole", Sdc::SCHEME_CATMARK, 6,
            {4, 4}, {0, 1, 2, 3, 1, 4, 5, 2}, {0}, true},
        {"edge-only quad", Sdc::SCHEME_CATMARK, 4,
            {4}, {0, 1, 2, 3}, {}, false, false, true},
        {"bilinear mixed polygons", Sdc::SCHEME_BILINEAR, 7,
            {3, 4}, {0, 1, 2, 3, 4, 5, 6}, {}, false},
        {"loop triangle", Sdc::SCHEME_LOOP, 3, {3}, {0, 1, 2}, {}, false},
        {"left-handed loop triangle", Sdc::SCHEME_LOOP, 3,
            {3}, {0, 1, 2}, {}, true},
        {"loop tetrahedron", Sdc::SCHEME_LOOP, 4,
            {3, 3, 3, 3}, {0, 2, 1, 0, 1, 3, 1, 2, 3, 2, 0, 3}, {}, false},
        {"closed boundary none", Sdc::SCHEME_CATMARK, 8,
            {4, 4, 4, 4, 4, 4},
            {0, 3, 2, 1, 4, 5, 6, 7, 0, 1, 5, 4,
                1, 2, 6, 5, 2, 3, 7, 6, 3, 0, 4, 7},
            {}, false, true},
        {"mixed open and closed boundary none", Sdc::SCHEME_CATMARK, 12,
            {4, 4, 4, 4, 4, 4, 4},
            {0, 3, 2, 1, 4, 5, 6, 7, 0, 1, 5, 4,
                1, 2, 6, 5, 2, 3, 7, 6, 3, 0, 4, 7, 8, 9, 10, 11},
            {}, false, true}};
    if (!TestPtexFaceOffsets() ||
        !TestPtexFaceOffsetPrimvars() ||
        !TestPtexFaceOffsetRefusalsAndRetry() ||
        !TestPtexFaceOffsetIntegerBoundaries() ||
        !TestCoarseTriQuadRefusalsAndRetry() ||
        !TestCoarseTriQuadPlanningRefusals() ||
        !TestCoarseTriQuadEmptyHolesAndExtremePoints() ||
        !TestLargeCoarseTriQuads() ||
        !TestCoarseAnalyticOrientation() ||
        !TestCoarseRefusalAndRetry() ||
        !TestAnalyticQuadAndRotatedLoop() ||
        !TestBufferRefusalAndRetry() ||
        !TestLargePolygon() ||
        !TestUnrenderableBoundaryRefusal())
    {
        return 1;
    }
    for (const TestTopology& topology : coarseTriQuadTopologies)
    {
        if (!TestCoarseTriQuadsMatchHydraAndOpenSubdiv(topology))
        {
            return 1;
        }
    }
    for (const TestTopology& topology : coarseTopologies)
    {
        if (!TestCoarseCoordinatesMatchOpenSubdiv(topology))
        {
            return 1;
        }
    }
    for (const TestTopology& topology : topologies)
    {
        for (int level = 1; level <= HdSilkMaxMeshRefineLevel; ++level)
        {
            if (!TestPtexCoordinatesMatchOpenSubdiv(topology, level))
            {
                return 1;
            }
        }
    }
    std::cout << "Ptex topology probes passed for "
              << coarseTriQuadTopologies.size() << " coarse tri-quad meshes, "
              << coarseTopologies.size()
              << " coarse meshes and "
              << topologies.size() * HdSilkMaxMeshRefineLevel
              << " refined meshes\n";
    return 0;
}
