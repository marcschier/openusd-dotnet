// Copyright (c) marcschier. Licensed under the MIT License.

#include "pxr/base/arch/timing.h"
#include "pxr/base/gf/matrix2d.h"
#include "pxr/base/gf/vec3d.h"
#include "pxr/base/plug/registry.h"
#include "pxr/base/tf/functionRef.h"
#include "pxr/base/tf/smallVector.h"
#include "pxr/base/tf/stringUtils.h"
#include "pxr/base/vt/streamOut.h"
#include "pxr/base/vt/types.h"
#include "pxr/usd/sdf/schema.h"
#include "pxr/usd/usd/attribute.h"
#include "pxr/usd/usd/stage.h"

#include <iostream>
#include <sstream>
#include <stdexcept>
#include <string>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
void Check(const Vt_ShapeData& shape, const char* expected)
{
    std::ostringstream stream;
    size_t element = 0;
    auto next = [&element](std::ostream& output) { output << element++; };
    VtStreamOutArray(stream, &shape, next);
    if (stream.str() != expected || element != shape.totalSize)
    {
        throw std::runtime_error("SDK array streaming changed its shape, element count or order.");
    }
}
}

int main(int argc, char** argv)
{
    try
    {
        if (argc != 2)
        {
            throw std::runtime_error("Expected the SDK metadata fixture path.");
        }
        TfDictionaryLessThan less;
        if (!less("item2", "item10") || less("item10", "item2") ||
            less("item2", "item2") || !less("mesh009", "mesh10") || !less("a1", "ab"))
        {
            throw std::runtime_error("SDK dictionary order changed its digit or equality semantics.");
        }
        TfSmallVector<GfVec3d, 0> empty;
        TfSmallVector<GfVec3d, 0> movedEmpty(std::move(empty));
        TfSmallVector<GfVec3d, 0> remote{GfVec3d(1, 2, 3), GfVec3d(4, 5, 6)};
        TfSmallVector<GfVec3d, 0> movedRemote(std::move(remote));
        if (!movedEmpty.empty() || !remote.empty() || movedRemote.size() != 2 ||
            movedRemote[0] != GfVec3d(1, 2, 3) || movedRemote[1] != GfVec3d(4, 5, 6))
        {
            throw std::runtime_error("SDK zero-inline-capacity vector move lost or changed values.");
        }
        if (PlugRegistry::GetInstance().RegisterPlugins(argv[1]).empty())
        {
            throw std::runtime_error("SDK metadata fixture was not registered.");
        }
        const SdfSchema& schema = SdfSchema::GetInstance();
        const VtValue& vector = schema.GetFallback(TfToken("openusdProbeVector"));
        const VtValue& matrix = schema.GetFallback(TfToken("openusdProbeMatrix"));
        if (!vector.IsHolding<GfVec3d>() || vector.Get<GfVec3d>() != GfVec3d(1, 2, 3) ||
            !matrix.IsHolding<GfMatrix2d>() ||
            matrix.Get<GfMatrix2d>()[0][0] != 1 || matrix.Get<GfMatrix2d>()[0][1] != 2 ||
            matrix.Get<GfMatrix2d>()[1][0] != 3 || matrix.Get<GfMatrix2d>()[1][1] != 4)
        {
            throw std::runtime_error("SDK schema tuple parsing changed vector/matrix shape or values.");
        }
        UsdStageRefPtr stage = UsdStage::Open(std::string(argv[1]) + "/clip-root.usda");
        if (!stage)
        {
            throw std::runtime_error("SDK clip fixture could not be opened.");
        }
        UsdAttribute sampled = stage->GetAttributeAtPath(SdfPath("/Root.sampled"));
        double lower = 0, upper = 0;
        bool hasSamples = false;
        if (!sampled.GetBracketingTimeSamples(2.5, &lower, &upper, &hasSamples) ||
            !hasSamples || lower != 0 || upper != 5 ||
            !sampled.GetBracketingTimeSamples(7.5, &lower, &upper, &hasSamples) ||
            !hasSamples || lower != 5 || upper != 10)
        {
            throw std::runtime_error("SDK clip sample ordering or bracketing changed.");
        }
        Check({3, {0, 0, 0}}, "[0, 1, 2]");
        Check({6, {2, 0, 0}}, "[[0, 1, 2], [3, 4, 5]]");
        Check({8, {2, 2, 0}}, "[[[0, 1], [2, 3]], [[4, 5], [6, 7]]]");
        Check({16, {2, 2, 2}},
            "[[[[0, 1], [2, 3]], [[4, 5], [6, 7]]], [[[8, 9], [10, 11]], [[12, 13], [14, 15]]]]");
        Check({7, {2, 2, 0}}, "[0, 1, 2, 3, 4, 5, 6]");
        Check({0, {0, 0, 0}}, "[]");
        if (!(ArchGetNanosecondsPerTick() > 0))
        {
            throw std::runtime_error("SDK timing must provide a positive conversion.");
        }
        (void)ArchGetTickQuantum();
        std::cout << "SDK_RUNTIME_PATCH_PROBE_OK: arrays, timing, dictionary, tuples, moves and clip samples\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
