// Copyright (c) marcschier. Licensed under the MIT License.

#include "../src/ptexFaceDecoder.h"

#include <algorithm>
#include <array>
#include <cstdint>
#include <iostream>
#include <stdexcept>
#include <string>

namespace
{

void Require(bool value, const char* message)
{
    if (!value)
    {
        throw std::runtime_error(message);
    }
}

template<typename Action>
void RequireFailure(Action action, const char* expected, const char* message)
{
    try
    {
        action();
    }
    catch (const std::exception& error)
    {
        Require(std::string(error.what()).find(expected) != std::string::npos, message);
        return;
    }
    throw std::runtime_error(message);
}

void DecodesSelectedFacesWithinLimits(const std::string& path)
{
    constexpr std::array<uint32_t, 4> faceIds = {0, 1, 2, 3};
    const HdSilkPtexDecodedFaces decoded = HdSilkDecodePtexFaces(
        path, faceIds.data(), faceIds.size(), 8, 192);

    Require(decoded.meshType == 1, "quad Ptex mesh type was not preserved");
    Require(decoded.dataType == 0, "Ptex data type was not preserved");
    Require(decoded.channelCount == 3, "Ptex channel count was not preserved");
    Require(decoded.alphaChannel == -1, "Ptex alpha channel was not preserved");
    Require(decoded.fileFaceCount == 4, "Ptex file face count was not preserved");
    Require(decoded.faces.size() == faceIds.size(), "wrong number of faces decoded");
    Require(decoded.pixels.size() == 192, "wrong decoded byte count");

    constexpr std::array<std::array<uint8_t, 3>, 4> colors = {{
        {{255, 255, 255}},
        {{255, 0, 0}},
        {{0, 255, 0}},
        {{0, 0, 255}},
    }};
    for (size_t index = 0; index < decoded.faces.size(); ++index)
    {
        const HdSilkPtexFaceData& face = decoded.faces[index];
        Require(face.faceId == faceIds[index], "face order changed during decode");
        Require(face.width == 4 && face.height == 4, "wrong face dimensions");
        Require(face.rowStrideBytes == 12, "wrong face row stride");
        Require(face.dataOffsetBytes == index * 48, "wrong face data offset");
        Require(face.adjacentFaceIds == std::array<int32_t, 4>{-1, -1, -1, -1},
            "Ptex boundary adjacency was not preserved");
        for (size_t pixel = 0; pixel < 16; ++pixel)
        {
            for (size_t channel = 0; channel < colors[index].size(); ++channel)
            {
                Require(
                    decoded.pixels[static_cast<size_t>(face.dataOffsetBytes) +
                        pixel * colors[index].size() + channel] == colors[index][channel],
                    "decoded face pixels differ from the fixture");
            }
        }
    }
}

void RefusesRequestsBeforeReturningPartialData(const std::string& path)
{
    constexpr std::array<uint32_t, 2> faceIds = {0, 1};
    RequireFailure(
        [&]()
        {
            HdSilkDecodePtexFaces(path, faceIds.data(), faceIds.size(), 2, 96);
        },
        "dimension limit",
        "oversized face was not refused");
    RequireFailure(
        [&]()
        {
            HdSilkDecodePtexFaces(path, faceIds.data(), faceIds.size(), 8, 95);
        },
        "byte limit",
        "output exceeding the byte limit was not refused");

    constexpr uint32_t invalidFace = 4;
    RequireFailure(
        [&]()
        {
            HdSilkDecodePtexFaces(path, &invalidFace, 1, 8, 48);
        },
        "outside the texture face range",
        "invalid face ID was not refused");
    RequireFailure(
        [&]()
        {
            HdSilkDecodePtexFaces(path + ".missing", faceIds.data(), 1, 8, 48);
        },
        "Could not open Ptex texture",
        "missing Ptex file was not refused");
}

void DuplicateFaceIdsHaveIndependentOutputSpans(const std::string& path)
{
    constexpr std::array<uint32_t, 2> faceIds = {2, 2};
    const HdSilkPtexDecodedFaces decoded = HdSilkDecodePtexFaces(
        path, faceIds.data(), faceIds.size(), 8, 96);
    Require(decoded.faces.size() == 2, "duplicate face request was collapsed");
    Require(decoded.faces[0].faceId == 2 && decoded.faces[1].faceId == 2,
        "duplicate face identity changed");
    Require(decoded.faces[0].dataOffsetBytes == 0 &&
        decoded.faces[1].dataOffsetBytes == 48,
        "duplicate faces do not have independent output spans");
    Require(
        std::equal(decoded.pixels.begin(), decoded.pixels.begin() + 48,
            decoded.pixels.begin() + 48),
        "duplicate face payloads differ");
}

} // namespace

int main(int argc, char** argv)
{
    if (argc != 2)
    {
        std::cerr << "Usage: hdsilk_ptex_decode_probe <quad-colors.ptx>\n";
        return 1;
    }

    try
    {
        DecodesSelectedFacesWithinLimits(argv[1]);
        RefusesRequestsBeforeReturningPartialData(argv[1]);
        DuplicateFaceIdsHaveIndependentOutputSpans(argv[1]);
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
