// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef HDSILK_DIRECT_LIGHTS_TEST_DECODE_H
#define HDSILK_DIRECT_LIGHTS_TEST_DECODE_H

#include <array>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <initializer_list>
#include <stdexcept>

namespace hdsilk_direct_lights_test
{
// Offsets include the eight-byte command header. Views borrow the page bytes;
// decoded FRAME values do not. Keep non-FRAME command readers in their probe.
struct CommandView
{
    const uint8_t* data = nullptr;
    size_t size = 0;
};

inline uint32_t ReadU32Le(CommandView bytes, size_t offset)
{
    if (bytes.data == nullptr || offset > bytes.size || bytes.size - offset < 4)
    {
        throw std::runtime_error("direct-light decoder: truncated uint32");
    }
    return static_cast<uint32_t>(bytes.data[offset]) |
        (static_cast<uint32_t>(bytes.data[offset + 1]) << 8) |
        (static_cast<uint32_t>(bytes.data[offset + 2]) << 16) |
        (static_cast<uint32_t>(bytes.data[offset + 3]) << 24);
}

inline int32_t ReadI32Le(CommandView bytes, size_t offset)
{
    const uint32_t bits = ReadU32Le(bytes, offset);
    int32_t value = 0;
    static_assert(sizeof(value) == sizeof(bits));
    std::memcpy(&value, &bits, sizeof(value));
    return value;
}

inline float ReadF32Le(CommandView bytes, size_t offset)
{
    const uint32_t bits = ReadU32Le(bytes, offset);
    float value = 0.0f;
    static_assert(sizeof(value) == sizeof(bits));
    std::memcpy(&value, &bits, sizeof(value));
    return value;
}

inline double ReadF64Le(CommandView bytes, size_t offset)
{
    uint64_t bits = ReadU32Le(bytes, offset);
    bits |= static_cast<uint64_t>(ReadU32Le(bytes, offset + 4)) << 32;
    double value = 0.0;
    static_assert(sizeof(value) == sizeof(bits));
    std::memcpy(&value, &bits, sizeof(value));
    return value;
}

template <typename Visitor>
void VisitCommands(
    const uint8_t* data,
    size_t size,
    uint32_t commandCount,
    Visitor&& visitor)
{
    if (data == nullptr && size != 0)
    {
        throw std::runtime_error("direct-light decoder: null page bytes");
    }
    size_t cursor = 0;
    uint32_t visited = 0;
    while (cursor < size)
    {
        if (visited == commandCount)
        {
            throw std::runtime_error("direct-light decoder: extra command bytes");
        }
        const CommandView remaining{data + cursor, size - cursor};
        const uint32_t type = ReadU32Le(remaining, 0);
        const uint32_t byteSize = ReadU32Le(remaining, 4);
        if (byteSize < 8 || byteSize > remaining.size)
        {
            throw std::runtime_error("direct-light decoder: invalid command length");
        }
        visitor(type, CommandView{remaining.data, byteSize});
        cursor += byteSize;
        ++visited;
    }
    if (visited != commandCount)
    {
        throw std::runtime_error("direct-light decoder: command count mismatch");
    }
}

struct DirectSlot
{
    uint32_t type = 0;
    uint32_t shadowEnabled = 0;
    float shapeX = 0.0f;
    float shapeY = 0.0f;
    std::array<float, 3> color{};
    float intensity = 0.0f;
    std::array<double, 16> transform{};
    float exposure = 0.0f;
    float diffuse = 0.0f;
    float specular = 0.0f;
    float radius = 0.0f;
};

struct DomeSlot
{
    std::array<float, 3> ambientColor{};
    uint32_t flags = 0;
};

struct Frame
{
    uint32_t type = 0;
    uint32_t byteSize = 0;
    int32_t width = 0;
    int32_t height = 0;
    std::array<double, 16> viewMatrix{};
    std::array<double, 16> projectionMatrix{};
    uint32_t clipPlaneCount = 0;
    std::array<std::array<double, 4>, 8> clipPlanes{};
    uint32_t directLightCount = 0;
    uint32_t lightingFlags = 0;
    std::array<DirectSlot, 128> directLights{};
    std::array<float, 3> ambientColor{};
    float ambientIntensity = 0.0f;
    uint32_t domeCount = 0;
    std::array<DomeSlot, 8> domes{};
};

inline DirectSlot ReadDirectSlot(CommandView frame, size_t index)
{
    if (index >= 128)
    {
        throw std::out_of_range("direct-light decoder: direct slot exceeds 127");
    }
    const size_t offset = 552 + 176 * index;
    DirectSlot slot;
    slot.type = ReadU32Le(frame, offset);
    slot.shadowEnabled = ReadU32Le(frame, offset + 4);
    slot.shapeX = ReadF32Le(frame, offset + 8);
    slot.shapeY = ReadF32Le(frame, offset + 12);
    for (size_t channel = 0; channel < 3; ++channel)
    {
        slot.color[channel] = ReadF32Le(frame, offset + 16 + 4 * channel);
    }
    slot.intensity = ReadF32Le(frame, offset + 28);
    for (size_t element = 0; element < 16; ++element)
    {
        slot.transform[element] = ReadF64Le(frame, offset + 32 + 8 * element);
    }
    slot.exposure = ReadF32Le(frame, offset + 160);
    slot.diffuse = ReadF32Le(frame, offset + 164);
    slot.specular = ReadF32Le(frame, offset + 168);
    slot.radius = ReadF32Le(frame, offset + 172);
    return slot;
}

inline Frame ReadFrame(const uint8_t* data, size_t size, uint32_t commandCount)
{
    Frame frame;
    bool foundFrame = false;
    VisitCommands(data, size, commandCount, [&](uint32_t type, CommandView command)
    {
        if (type != 1u)
        {
            return;
        }
        if (foundFrame || command.data != data)
        {
            throw std::runtime_error("direct-light decoder: FRAME must occur once, first");
        }
        foundFrame = true;
        frame.type = type;
        frame.byteSize = ReadU32Le(command, 4);
        if (command.size != 23368 || frame.byteSize != 23368u)
        {
            throw std::runtime_error("direct-light decoder: expected full 23368-byte FRAME");
        }
        frame.width = ReadI32Le(command, 8);
        frame.height = ReadI32Le(command, 12);
        for (size_t element = 0; element < 16; ++element)
        {
            frame.viewMatrix[element] = ReadF64Le(command, 16 + 8 * element);
            frame.projectionMatrix[element] = ReadF64Le(command, 144 + 8 * element);
        }
        frame.clipPlaneCount = ReadU32Le(command, 272);
        for (size_t plane = 0; plane < 8; ++plane)
        {
            for (size_t component = 0; component < 4; ++component)
            {
                frame.clipPlanes[plane][component] =
                    ReadF64Le(command, 280 + 32 * plane + 8 * component);
            }
        }
        frame.directLightCount = ReadU32Le(command, 536);
        frame.lightingFlags = ReadU32Le(command, 540);
        for (size_t index = 0; index < 128; ++index)
        {
            frame.directLights[index] = ReadDirectSlot(command, index);
        }
        for (size_t channel = 0; channel < 3; ++channel)
        {
            frame.ambientColor[channel] = ReadF32Le(command, 23080 + 4 * channel);
        }
        frame.ambientIntensity = ReadF32Le(command, 23092);
        frame.domeCount = ReadU32Le(command, 23096);
        if (frame.clipPlaneCount > 8u || frame.directLightCount > 128u ||
            frame.domeCount > 8u || (frame.lightingFlags & ~1u) != 0u)
        {
            throw std::runtime_error("direct-light decoder: invalid FRAME count or flags");
        }
        // Read reserved floats as bits too: negative zero is not reserved zero.
        for (size_t offset : {size_t{276}, size_t{544}, size_t{548},
                 size_t{23100}, size_t{23104}, size_t{23108}})
        {
            if (ReadU32Le(command, offset) != 0u)
            {
                throw std::runtime_error("direct-light decoder: nonzero FRAME reserved word");
            }
        }
        for (size_t index = 0; index < 8; ++index)
        {
            const size_t offset = 23112 + 32 * index;
            for (size_t channel = 0; channel < 3; ++channel)
            {
                frame.domes[index].ambientColor[channel] =
                    ReadF32Le(command, offset + 4 * channel);
            }
            frame.domes[index].flags = ReadU32Le(command, offset + 16);
            for (size_t reserved : {size_t{12}, size_t{20}, size_t{24}, size_t{28}})
            {
                if (ReadU32Le(command, offset + reserved) != 0u)
                {
                    throw std::runtime_error("direct-light decoder: nonzero dome reserved word");
                }
            }
        }
    });
    if (!foundFrame)
    {
        throw std::runtime_error("direct-light decoder: missing FRAME");
    }
    return frame;
}
}

#endif
