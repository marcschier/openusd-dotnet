// Copyright (c) marcschier. Licensed under the MIT License.

#include "pxr/base/arch/timing.h"
#include "pxr/base/tf/functionRef.h"
#include "pxr/base/vt/streamOut.h"
#include "pxr/base/vt/types.h"

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

int main()
{
    try
    {
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
        std::cout << "SDK_RUNTIME_PATCH_PROBE_OK: ranks1-4, remainder, empty and timing\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
