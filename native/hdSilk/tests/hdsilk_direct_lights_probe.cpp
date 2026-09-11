// Copyright (c) marcschier. Licensed under the MIT License.

#include "hdsilk_direct_lights_test_decode.h"
#include "openusd_hdsilk.h"
#include "sceneState.h"
#include "instanceLinking.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <initializer_list>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string>
#include <typeinfo>
#include <utility>
#include <vector>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
using namespace hdsilk_direct_lights_test;

static_assert(OPENUSD_SILK_PAGE_ABI_VERSION == 24u);
static_assert(OPENUSD_SILK_SESSION_ABI_VERSION == 6u);
static_assert(OPENUSD_SILK_SCENE_INGESTION_VERSION == 1u);
static_assert(OPENUSD_SILK_MAX_FRAME_LIGHTS == 128u);
static_assert(OPENUSD_SILK_LIGHT_MASK_WORDS == 4u);
static_assert(OPENUSD_SILK_MAX_DOME_LIGHTS == 8u);
static_assert(std::numeric_limits<float>::is_iec559);

constexpr std::array<double, 16> IdentityTransform{
    1.0, 0.0, 0.0, 0.0,
    0.0, 1.0, 0.0, 0.0,
    0.0, 0.0, 1.0, 0.0,
    0.0, 0.0, 0.0, 1.0};

constexpr std::array<uint32_t, 4> BoundaryWords{
    0x80000000u, 0x80000001u, 0x80000001u, 0x80000001u};
constexpr std::array<uint32_t, 4> OtherWords{
    0x00000001u, 0x40000002u, 0x40000002u, 0x40000002u};

struct MaskCase
{
    uint32_t input;
    std::array<uint32_t, 4> words;
};

// Literal oracles, shared only as data with the accepted FRAME count matrix.
constexpr MaskCase MaskCounts[] = {
    {0u,   {0u, 0u, 0u, 0u}},
    {1u,   {0x00000001u, 0u, 0u, 0u}},
    {8u,   {0x000000ffu, 0u, 0u, 0u}},
    {9u,   {0x000001ffu, 0u, 0u, 0u}},
    {31u,  {0x7fffffffu, 0u, 0u, 0u}},
    {32u,  {0xffffffffu, 0u, 0u, 0u}},
    {33u,  {0xffffffffu, 0x00000001u, 0u, 0u}},
    {63u,  {0xffffffffu, 0x7fffffffu, 0u, 0u}},
    {64u,  {0xffffffffu, 0xffffffffu, 0u, 0u}},
    {65u,  {0xffffffffu, 0xffffffffu, 0x00000001u, 0u}},
    {95u,  {0xffffffffu, 0xffffffffu, 0x7fffffffu, 0u}},
    {96u,  {0xffffffffu, 0xffffffffu, 0xffffffffu, 0u}},
    {97u,  {0xffffffffu, 0xffffffffu, 0xffffffffu, 0x00000001u}},
    {127u, {0xffffffffu, 0xffffffffu, 0xffffffffu, 0x7fffffffu}},
    {128u, {0xffffffffu, 0xffffffffu, 0xffffffffu, 0xffffffffu}}};

void Require(bool condition, const std::string& diagnostic)
{
    if (!condition)
    {
        throw std::runtime_error(diagnostic);
    }
}

void RequireUnusedDirectSlots(
    const Frame& frame, size_t firstUnused, const std::string& context)
{
    for (size_t index = firstUnused; index < 128; ++index)
    {
        const DirectSlot& slot = frame.directLights[index];
        const std::string label = context + " unused slot " + std::to_string(index);
        Require(slot.type == 0u && slot.shadowEnabled == 0u &&
                slot.shapeX == 0.0f && slot.shapeY == 0.0f &&
                slot.color == std::array<float, 3>{0.0f, 0.0f, 0.0f} &&
                slot.intensity == 0.0f && slot.exposure == 0.0f &&
                slot.diffuse == 0.0f && slot.specular == 0.0f && slot.radius == 0.0f,
            label + ": expected zero scalar fields");
        Require(slot.transform == IdentityTransform,
            label + ": expected all sixteen identity-transform elements");
    }
}

// A fixed fixture oracle, not an expected serializer or a record round-trip:
// sorted /Lights/Lnnn identity n must occupy slot n with intensity n + 1.
void RequireMarkerDirectLights(
    const Frame& frame, size_t count, const std::string& context)
{
    for (size_t index = 0; index < count; ++index)
    {
        const DirectSlot& slot = frame.directLights[index];
        const std::string label = context + " marker slot " + std::to_string(index);
        Require(slot.type == 1u && slot.shadowEnabled == 0u &&
                slot.shapeX == 0.0f && slot.shapeY == 0.0f &&
                slot.color == std::array<float, 3>{0.25f, 0.5f, 0.75f} &&
                slot.intensity == static_cast<float>(index + 1),
            label + ": wrong sorted identity, shape, color or intensity");
        Require(slot.transform == IdentityTransform &&
                slot.exposure == 1.0f && slot.diffuse == 0.25f &&
                slot.specular == 0.75f && slot.radius == 0.375f,
            label + ": wrong transform or trailing controls");
    }
}

void RequireAmbientAndDomeSlots(
    const Frame& frame, bool markerDome, const std::string& context)
{
    const std::array<float, 3> expectedAmbient = markerDome
        ? std::array<float, 3>{1.92f, 0.96f, 0.48f}
        : std::array<float, 3>{0.0f, 0.0f, 0.0f};
    Require(frame.ambientColor == expectedAmbient &&
            frame.ambientIntensity == (markerDome ? 1.0f : 0.0f) &&
            frame.domeCount == (markerDome ? 1u : 0u),
        context + ": wrong ambient or dome header");
    for (size_t index = 0; index < 8; ++index)
    {
        const bool present = markerDome && index == 0;
        Require(frame.domes[index].flags == (present ? 1u : 0u) &&
                frame.domes[index].ambientColor == (present
                    ? std::array<float, 3>{1.92f, 0.96f, 0.48f}
                    : std::array<float, 3>{0.0f, 0.0f, 0.0f}),
            context + ": wrong dome slot " + std::to_string(index));
    }
}

void VerifyLightMaskConstructionAndEquality()
{
    const HdSilkLightMask defaultMask;
    const HdSilkLightMask explicitZero(0u);
    Require(defaultMask.words == std::array<uint32_t, 4>{0u, 0u, 0u, 0u} &&
            explicitZero.words == std::array<uint32_t, 4>{0u, 0u, 0u, 0u},
        "default and explicit-zero construction must clear all four words");
    Require(defaultMask == explicitZero && explicitZero == defaultMask &&
            !(defaultMask != explicitZero) && !(explicitZero != defaultMask),
        "independently constructed empty masks must compare equal both ways");

    const MaskCase lowWords[] = {
        {1u, {0x00000001u, 0u, 0u, 0u}},
        {0x80000000u, {0x80000000u, 0u, 0u, 0u}},
        {0xffffffffu, {0xffffffffu, 0u, 0u, 0u}}};
    for (const MaskCase& row : lowWords)
    {
        const HdSilkLightMask mask(row.input);
        Require(mask.words == row.words, "low-word constructor populated a wrong word");
        for (size_t index = 32; index < 128; ++index)
        {
            Require(!mask.Contains(index), "low-word constructor admitted a high-word light");
        }
    }

    const HdSilkLightMask lowFull(0xffffffffu);
    HdSilkLightMask allFull;
    allFull.words = {0xffffffffu, 0xffffffffu, 0xffffffffu, 0xffffffffu};
    Require(lowFull != allFull && allFull != lowFull &&
            !(lowFull == allFull) && !(allFull == lowFull),
        "a full low word is not a full 128-light mask");

    for (const std::array<uint32_t, 4>& expected : {BoundaryWords, OtherWords})
    {
        HdSilkLightMask left;
        left.words = expected;
        HdSilkLightMask right;
        right.words = expected;
        Require(left == right && right == left && !(left != right) && !(right != left) &&
                left == left && !(left != left),
            "equal independent masks and self-equality must agree");
        for (size_t word = 0; word < 4; ++word)
        {
            HdSilkLightMask different = right;
            different.words[word] ^= 0x10u;
            Require(!(left == different) && !(different == left) &&
                    left != different && different != left,
                "equality ignored changed word " + std::to_string(word));
            Require(left.words == expected && right.words == expected,
                "comparison changed an operand");
        }
    }
}

void VerifyLightMaskSetAndContainsBoundaries()
{
    const MaskCase singleBits[] = {
        {0u,   {0x00000001u, 0u, 0u, 0u}},
        {31u,  {0x80000000u, 0u, 0u, 0u}},
        {32u,  {0u, 0x00000001u, 0u, 0u}},
        {42u,  {0u, 0x00000400u, 0u, 0u}},
        {63u,  {0u, 0x80000000u, 0u, 0u}},
        {64u,  {0u, 0u, 0x00000001u, 0u}},
        {95u,  {0u, 0u, 0x80000000u, 0u}},
        {96u,  {0u, 0u, 0u, 0x00000001u}},
        {127u, {0u, 0u, 0u, 0x80000000u}}};
    for (const MaskCase& row : singleBits)
    {
        HdSilkLightMask mask;
        mask.Set(row.input);
        const std::string label = "Set(" + std::to_string(row.input) + ")";
        Require(mask.words == row.words, label + ": wrong four-word bit pattern");
        // This includes neighbors and same-position aliases in every other word:
        // in particular, 42 must not admit 10, 74 or 106.
        for (size_t index = 0; index < 128; ++index)
        {
            Require(mask.Contains(index) == (index == row.input),
                label + ": wrong Contains(" + std::to_string(index) + ")");
        }
        mask.Set(row.input);
        Require(mask.words == row.words, label + ": repeated Set was not idempotent");
    }

    HdSilkLightMask aggregate;
    for (size_t index : {size_t{127}, size_t{31}, size_t{64}, size_t{32},
             size_t{95}, size_t{63}, size_t{96}})
    {
        aggregate.Set(index);
        aggregate.Set(index);
    }
    Require(aggregate.words == BoundaryWords, "aggregate B lost or aliased a boundary bit");
    for (size_t index = 0; index < 128; ++index)
    {
        const bool expected = index == 31 || index == 32 || index == 63 ||
            index == 64 || index == 95 || index == 96 || index == 127;
        Require(aggregate.Contains(index) == expected,
            "aggregate B: wrong Contains(" + std::to_string(index) + ")");
    }
    for (size_t invalid : {size_t{128}, std::numeric_limits<size_t>::max()})
    {
        bool rejected = false;
        try
        {
            aggregate.Set(invalid);
        }
        catch (const std::out_of_range& error)
        {
            Require(typeid(error) == typeid(std::out_of_range),
                "Set must throw exactly std::out_of_range");
            rejected = true;
        }
        Require(rejected && aggregate.words == BoundaryWords,
            "out-of-range Set must reject without changing prior words");
        Require(!aggregate.Contains(invalid) && aggregate.words == BoundaryWords,
            "out-of-range Contains must return false without changing prior words");
    }
}

void VerifyLightMaskFirstBoundaries()
{
    for (const MaskCase& row : MaskCounts)
    {
        const HdSilkLightMask mask = HdSilkLightMask::First(row.input);
        const std::string label = "First(" + std::to_string(row.input) + ")";
        Require(mask.words == row.words, label + ": wrong literal four-word mask");
        for (size_t index = 0; index < 128; ++index)
        {
            Require(mask.Contains(index) == (index < row.input),
                label + ": wrong Contains(" + std::to_string(index) + ")");
        }
        Require(!mask.Contains(128) && mask.words == row.words,
            label + ": Contains admitted bit 128 or mutated the mask");
    }
    for (uint32_t invalid : {129u, std::numeric_limits<uint32_t>::max()})
    {
        bool rejected = false;
        try
        {
            (void)HdSilkLightMask::First(invalid);
        }
        catch (const std::out_of_range& error)
        {
            Require(typeid(error) == typeid(std::out_of_range),
                "First must throw exactly std::out_of_range");
            rejected = true;
        }
        Require(rejected, "First must refuse " + std::to_string(invalid));
    }
}

void VerifyNativeFrameAbiAndCounts()
{
    const HdSilkFrameState authoredFrame{
        640, 360,
        {0.0, 1.0, 0.0, 0.0, -1.0, 0.0, 0.0, 0.0,
         0.0, 0.0, 1.0, 0.0, -2.0, 3.0, -4.0, 1.0},
        {2.0, 0.0, 0.0, 0.0, 0.0, 4.0, 0.0, 0.0,
         0.0, 0.0, -1.0, -1.0, 0.0, 0.0, -2.0, 0.0},
        0u, {}};

    for (const MaskCase& row : MaskCounts)
    {
        const uint32_t count = row.input;
        HdSilkSceneState state;
        state.SetFrame(authoredFrame);

        HdSilkLightRecord dome;
        dome.path = "/Lights/MarkerDome";
        dome.ambientOnly = true;
        dome.color[0] = 1.0f;
        dome.color[1] = 0.5f;
        dome.color[2] = 0.25f;
        dome.intensity = 2.0f;
        dome.exposure = 1.0f;
        dome.diffuse = 0.5f;
        state.ReplaceLight(dome);

        for (uint32_t remaining = count; remaining > 0; --remaining)
        {
            const uint32_t index = remaining - 1;
            const std::string digits = std::to_string(index);
            HdSilkLightRecord light;
            light.path = "/Lights/L" + std::string(3 - digits.size(), '0') + digits;
            light.type = OPENUSD_SILK_LIGHT_DISTANT;
            light.color[0] = 0.25f;
            light.color[1] = 0.5f;
            light.color[2] = 0.75f;
            light.intensity = static_cast<float>(index + 1);
            light.exposure = 1.0f;
            light.diffuse = 0.25f;
            light.specular = 0.75f;
            light.radius = 0.375f;
            state.ReplaceLight(light);
        }

        uint64_t revision = 0;
        uint32_t commandCount = 0;
        const std::vector<uint8_t> bytes = state.BuildPage(&revision, &commandCount);
        const Frame frame = ReadFrame(bytes.data(), bytes.size(), commandCount);
        const std::string label = "native FRAME count " + std::to_string(count);
        Require(revision == 1u && commandCount == 1u && bytes.size() == 23368 &&
                frame.type == 1u && frame.byteSize == 23368u,
            label + ": expected one complete native FRAME, not an old short form");
        Require(frame.width == 640 && frame.height == 360 &&
                frame.viewMatrix == std::array<double, 16>{
                    0.0, 1.0, 0.0, 0.0, -1.0, 0.0, 0.0, 0.0,
                    0.0, 0.0, 1.0, 0.0, -2.0, 3.0, -4.0, 1.0} &&
                frame.projectionMatrix == std::array<double, 16>{
                    2.0, 0.0, 0.0, 0.0, 0.0, 4.0, 0.0, 0.0,
                    0.0, 0.0, -1.0, -1.0, 0.0, 0.0, -2.0, 0.0},
            label + ": dimensions or complete row-major camera matrices moved");
        Require(frame.clipPlaneCount == 0u, label + ": unexpected clip planes");
        for (const std::array<double, 4>& plane : frame.clipPlanes)
        {
            Require(plane == std::array<double, 4>{0.0, 0.0, 0.0, 0.0},
                label + ": unused clip plane is not zero");
        }
        Require(frame.directLightCount == count &&
                frame.lightingFlags == (count == 0u ? 0u : 1u),
            label + ": wrong effective count or authored-direct flag");
        RequireMarkerDirectLights(frame, count, label);
        RequireUnusedDirectSlots(frame, count, label);
        RequireAmbientAndDomeSlots(frame, true, label);
    }
}

void VerifyDirectShapeAndControlWire()
{
    struct ShapeCase
    {
        const char* name;
        uint32_t type;
        uint32_t wireType;
        float expectedShapeX;
        float expectedShapeY;
    };
    const ShapeCase shapes[] = {
        {"Distant", OPENUSD_SILK_LIGHT_DISTANT, 1u, 0.0f, 0.0f},
        {"Sphere", OPENUSD_SILK_LIGHT_SPHERE, 2u, 0.0f, 0.0f},
        {"Rect", OPENUSD_SILK_LIGHT_RECT, 3u, 4.0f, 6.0f},
        {"Disk", OPENUSD_SILK_LIGHT_DISK, 4u, 0.0f, 0.0f},
        {"Cylinder", OPENUSD_SILK_LIGHT_CYLINDER, 5u, 8.0f, 0.0f}};
    const std::array<double, 16> orientedTransform{
        0.0, 1.0, 0.0, 0.0,
        -1.0, 0.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        3.0, 4.0, 5.0, 1.0};

    for (const ShapeCase& shape : shapes)
    {
        for (uint32_t shadowEnabled : {0u, 1u})
        {
            HdSilkSceneState state;
            HdSilkLightRecord light;
            light.path = "/Lights/Shape";
            light.type = shape.type;
            light.shadowEnabled = shadowEnabled;
            if (shape.type == OPENUSD_SILK_LIGHT_RECT)
            {
                light.shapeX = 4.0f;
                light.shapeY = 6.0f;
            }
            else if (shape.type == OPENUSD_SILK_LIGHT_CYLINDER)
            {
                light.shapeX = 8.0f;
            }
            light.color[0] = 0.25f;
            light.color[1] = 0.5f;
            light.color[2] = 0.75f;
            light.intensity = 2.0f;
            light.exposure = 1.0f;
            light.diffuse = 0.25f;
            light.specular = 0.75f;
            light.radius = 0.375f;
            std::copy(orientedTransform.begin(), orientedTransform.end(), light.transform);
            state.ReplaceLight(light);

            uint32_t commandCount = 0;
            const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
            const Frame frame = ReadFrame(bytes.data(), bytes.size(), commandCount);
            const std::string label =
                std::string(shape.name) + " shadowEnabled=" + std::to_string(shadowEnabled);
            Require(frame.directLightCount == 1u && frame.lightingFlags == 1u,
                label + ": supported direct light was not selected");
            const DirectSlot& slot = frame.directLights[0];
            Require(slot.type == shape.wireType && slot.shadowEnabled == shadowEnabled &&
                    slot.shapeX == shape.expectedShapeX && slot.shapeY == shape.expectedShapeY,
                label + ": wrong type, shadow bit or area dimensions");
            Require(slot.color == std::array<float, 3>{0.25f, 0.5f, 0.75f} &&
                    slot.intensity == 2.0f && slot.exposure == 1.0f &&
                    slot.diffuse == 0.25f && slot.specular == 0.75f && slot.radius == 0.375f,
                label + ": RGB, intensity or trailing controls were changed or pre-exposed");
            Require(slot.transform == std::array<double, 16>{
                    0.0, 1.0, 0.0, 0.0, -1.0, 0.0, 0.0, 0.0,
                    0.0, 0.0, 1.0, 0.0, 3.0, 4.0, 5.0, 1.0},
                label + ": complete orientation/translation was not transported row-major");
            RequireUnusedDirectSlots(frame, 1, label);
            RequireAmbientAndDomeSlots(frame, false, label);
            // shadowEnabled transports for all five shapes. Additional SHADOW
            // commands are traversed, not mistaken for support of an area map.
        }
    }
}

struct EligibilityCase
{
    const char* name;
    uint32_t selectedCount;
    bool visible;
    float intensity;
    std::array<float, 3> color;
    float exposure;
    float diffuse;
    float specular;
    uint32_t type = OPENUSD_SILK_LIGHT_SPHERE;
    float radius = 0.5f;
    float shapeX = 0.0f;
    float shapeY = 0.0f;
};

// Exact-zero admission, not a physical-radiance oracle. These are finite
// retained-record controls, not claims about the public authoring facade.
// denorm_min() is .NET float.Epsilon; epsilon() is NOT that value.
constexpr float Tiny = std::numeric_limits<float>::denorm_min();
const EligibilityCase EligibilityCases[] = {
    {"visible", 1u, true, 1.0f, {1.0f, 1.0f, 1.0f}, 0.0f, 1.0f, 1.0f},
    {"hidden", 0u, false, 1.0f, {1.0f, 1.0f, 1.0f}, 0.0f, 1.0f, 1.0f},
    {"intensity+0", 0u, true, 0.0f, {1.0f, 1.0f, 1.0f}, 0.0f, 1.0f, 1.0f},
    {"intensity-0", 0u, true, -0.0f, {1.0f, 1.0f, 1.0f}, 0.0f, 1.0f, 1.0f},
    {"rgb-zero", 0u, true, 1.0f, {0.0f, 0.0f, 0.0f}, 0.0f, 1.0f, 1.0f},
    {"rgb-signed-zero", 0u, true, 1.0f, {-0.0f, 0.0f, -0.0f}, 0.0f, 1.0f, 1.0f},
    {"multipliers-zero", 0u, true, 1.0f, {1.0f, 1.0f, 1.0f}, 0.0f, 0.0f, 0.0f},
    {"multipliers-signed-zero", 0u, true, 1.0f, {1.0f, 1.0f, 1.0f}, 0.0f, -0.0f, -0.0f},
    {"finite-underflow", 0u, true, 1.0f, {1.0f, 1.0f, 1.0f}, -200.0f, 1.0f, 1.0f},
    {"specular-only", 1u, true, 1.0f, {1.0f, 1.0f, 1.0f}, 0.0f, 0.0f, 1.0f},
    {"diffuse-only", 1u, true, 1.0f, {1.0f, 1.0f, 1.0f}, 0.0f, 1.0f, 0.0f},
    {"red-only", 1u, true, 1.0f, {1.0f, 0.0f, 0.0f}, 0.0f, 1.0f, 1.0f},
    {"green-only", 1u, true, 1.0f, {0.0f, 1.0f, 0.0f}, 0.0f, 1.0f, 1.0f},
    {"blue-only", 1u, true, 1.0f, {0.0f, 0.0f, 1.0f}, 0.0f, 1.0f, 1.0f},
    {"tiny-intensity", 1u, true, Tiny, {1.0f, 1.0f, 1.0f}, 0.0f, 1.0f, 1.0f},
    {"tiny-red", 1u, true, 1.0f, {Tiny, 0.0f, 0.0f}, 0.0f, 1.0f, 1.0f},
    {"tiny-green", 1u, true, 1.0f, {0.0f, Tiny, 0.0f}, 0.0f, 1.0f, 1.0f},
    {"tiny-blue", 1u, true, 1.0f, {0.0f, 0.0f, Tiny}, 0.0f, 1.0f, 1.0f},
    {"tiny-diffuse-only", 1u, true, 1.0f, {1.0f, 1.0f, 1.0f}, 0.0f, Tiny, 0.0f},
    {"tiny-specular-only", 1u, true, 1.0f, {1.0f, 1.0f, 1.0f}, 0.0f, 0.0f, Tiny},
    {"tiny-intensity-and-color", 1u, true, Tiny, {Tiny, 0.0f, 0.0f}, 0.0f, 1.0f, 1.0f},
    {"negative-intensity", 1u, true, -2.0f, {1.0f, 1.0f, 1.0f}, 0.0f, 1.0f, 1.0f},
    {"negative-red-only", 1u, true, 1.0f, {-2.0f, 0.0f, 0.0f}, 0.0f, 1.0f, 1.0f},
    {"negative-green-only", 1u, true, 1.0f, {0.0f, -2.0f, 0.0f}, 0.0f, 1.0f, 1.0f},
    {"negative-blue-only", 1u, true, 1.0f, {0.0f, 0.0f, -2.0f}, 0.0f, 1.0f, 1.0f},
    {"negative-diffuse-only", 1u, true, 1.0f, {1.0f, 1.0f, 1.0f}, 0.0f, -2.0f, 0.0f},
    {"negative-specular-only", 1u, true, 1.0f, {1.0f, 1.0f, 1.0f}, 0.0f, 0.0f, -2.0f},
    {"sphere-zero-radius", 1u, true, 1.0f, {1.0f, 1.0f, 1.0f}, 0.0f, 1.0f, 1.0f,
        OPENUSD_SILK_LIGHT_SPHERE, 0.0f},
    {"disk-zero-radius", 1u, true, 1.0f, {1.0f, 1.0f, 1.0f}, 0.0f, 1.0f, 1.0f,
        OPENUSD_SILK_LIGHT_DISK, 0.0f},
    {"rect-zero-width", 1u, true, 1.0f, {1.0f, 1.0f, 1.0f}, 0.0f, 1.0f, 1.0f,
        OPENUSD_SILK_LIGHT_RECT, 0.5f, 0.0f, 6.0f},
    {"rect-zero-height", 1u, true, 1.0f, {1.0f, 1.0f, 1.0f}, 0.0f, 1.0f, 1.0f,
        OPENUSD_SILK_LIGHT_RECT, 0.5f, 4.0f, 0.0f},
    {"rect-zero-dimensions", 1u, true, 1.0f, {1.0f, 1.0f, 1.0f}, 0.0f, 1.0f, 1.0f,
        OPENUSD_SILK_LIGHT_RECT},
    {"cylinder-zero-length", 1u, true, 1.0f, {1.0f, 1.0f, 1.0f}, 0.0f, 1.0f, 1.0f,
        OPENUSD_SILK_LIGHT_CYLINDER}};

void VerifyDirectEligibilityPartitions()
{
    for (const EligibilityCase& row : EligibilityCases)
    {
        HdSilkSceneState state;
        HdSilkLightRecord light;
        light.path = "/Lights/Eligibility";
        light.type = row.type;
        light.visible = row.visible;
        light.intensity = row.intensity;
        std::copy(row.color.begin(), row.color.end(), light.color);
        light.exposure = row.exposure;
        light.diffuse = row.diffuse;
        light.specular = row.specular;
        light.radius = row.radius;
        light.shapeX = row.shapeX;
        light.shapeY = row.shapeY;
        state.ReplaceLight(light);

        uint64_t revision = 0;
        uint32_t commandCount = 0;
        const std::vector<uint8_t> bytes = state.BuildPage(&revision, &commandCount);
        const Frame frame = ReadFrame(bytes.data(), bytes.size(), commandCount);
        const std::string label = row.name;
        Require(revision == 1u && commandCount == 1u &&
                frame.directLightCount == row.selectedCount && frame.lightingFlags == 1u,
            label + ": wrong admission or missing authored-direct flag");
        if (row.selectedCount == 1u)
        {
            const DirectSlot& slot = frame.directLights[0];
            // Compare the table's concrete float representations, not a
            // multiplied-radiance helper or tolerant epsilon comparison.
            Require(std::memcmp(slot.color.data(), row.color.data(), 3 * sizeof(float)) == 0 &&
                    std::memcmp(&slot.intensity, &row.intensity, sizeof(float)) == 0 &&
                    std::memcmp(&slot.exposure, &row.exposure, sizeof(float)) == 0 &&
                    std::memcmp(&slot.diffuse, &row.diffuse, sizeof(float)) == 0 &&
                    std::memcmp(&slot.specular, &row.specular, sizeof(float)) == 0,
                label + ": selected color/intensity/exposure/multiplier bits changed");
            Require(slot.type == row.type && slot.shadowEnabled == 0u &&
                    slot.shapeX == row.shapeX && slot.shapeY == row.shapeY &&
                    slot.radius == row.radius && slot.transform == IdentityTransform,
                label + ": selected geometry controls or identity transform changed");
        }
        RequireUnusedDirectSlots(frame, row.selectedCount, label);
        RequireAmbientAndDomeSlots(frame, false, label);
    }
}

void VerifyNonFiniteDirectLightsRefuseAndRetry()
{
    for (int field = 0; field < 7; ++field)
    {
        HdSilkSceneState state;
        HdSilkLightRecord light;
        light.path = "/Lights/Invalid";
        light.type = OPENUSD_SILK_LIGHT_DISTANT;
        state.ReplaceLight(light);
        uint64_t revision = 0;
        uint32_t commands = 0;
        (void)state.BuildPage(&revision, &commands);
        Require(revision == 1u && commands == 1u, "finite control did not publish");
        switch (field)
        {
        case 0: light.intensity = std::numeric_limits<float>::quiet_NaN(); break;
        case 1: light.color[2] = std::numeric_limits<float>::infinity(); break;
        case 2: light.exposure = -std::numeric_limits<float>::infinity(); break;
        case 3: light.transform[12] = std::numeric_limits<double>::max(); break;
        case 4: light.exposure = 128.0f; break;
        case 5: light.shapeX = std::numeric_limits<float>::quiet_NaN(); break;
        case 6:
            light.visible = false;
            light.specular = std::numeric_limits<float>::quiet_NaN();
            break;
        }
        state.ReplaceLight(light);
        bool refused = false;
        try
        {
            (void)state.BuildPage(&revision, &commands);
        }
        catch (const std::runtime_error& error)
        {
            const std::string diagnostic = error.what();
            refused = diagnostic.find("/Lights/Invalid") != std::string::npos &&
                diagnostic.find("finite GPU value") != std::string::npos;
        }
        Require(refused && revision == 1u && commands == 1u,
            "invalid direct light was published or changed output revision");
        light = HdSilkLightRecord();
        light.path = "/Lights/Invalid";
        light.type = OPENUSD_SILK_LIGHT_DISTANT;
        state.ReplaceLight(light);
        const std::vector<uint8_t> recovered = state.BuildPage(&revision, &commands);
        const Frame frame = ReadFrame(recovered.data(), recovered.size(), commands);
        Require(revision == 2u && frame.directLightCount == 1u &&
                frame.directLights[0].intensity == 1.0f,
            "correcting a non-finite direct light did not recover on retry");
    }
}

void VerifyAuthoredFlagsAndEffectiveCapacity()
{
    HdSilkSceneState emptyState;
    uint64_t revision = 0;
    uint32_t commandCount = 0;
    std::vector<uint8_t> bytes = emptyState.BuildPage(&revision, &commandCount);
    Frame frame = ReadFrame(bytes.data(), bytes.size(), commandCount);
    Require(revision == 1u && commandCount == 1u &&
            frame.directLightCount == 0u && frame.lightingFlags == 0u,
        "truly empty retained scene must not claim an authored direct light");
    RequireUnusedDirectSlots(frame, 0, "truly empty");
    // The current native publisher writes zero ambient, not a serialized
    // headlight. The flag permits the consumer's fallback; no pixel claim here.
    RequireAmbientAndDomeSlots(frame, false, "truly empty");

    HdSilkLightRecord dome;
    dome.path = "/Lights/OnlyDome";
    dome.ambientOnly = true;
    dome.color[0] = 1.0f;
    dome.color[1] = 0.5f;
    dome.color[2] = 0.25f;
    dome.intensity = 2.0f;
    dome.exposure = 1.0f;
    dome.diffuse = 0.5f;
    emptyState.ReplaceLight(dome);
    bytes = emptyState.BuildPage(&revision, &commandCount);
    frame = ReadFrame(bytes.data(), bytes.size(), commandCount);
    Require(revision == 2u && commandCount == 1u &&
            frame.directLightCount == 0u && frame.lightingFlags == 0u,
        "dome-only scene must not claim an authored direct light");
    RequireUnusedDirectSlots(frame, 0, "dome-only");
    RequireAmbientAndDomeSlots(frame, true, "dome-only");
    emptyState.RemoveLight("/Lights/OnlyDome");

    // Each dark record independently, then all of them together. Non-default
    // receiver/caster categories on excluded lights must not activate linking.
    std::vector<HdSilkLightRecord> excluded;
    for (const EligibilityCase& row : EligibilityCases)
    {
        if (row.selectedCount != 0u)
        {
            continue;
        }
        HdSilkLightRecord light;
        light.path = "/Excluded/" + std::string(row.name);
        light.type = OPENUSD_SILK_LIGHT_SPHERE;
        light.visible = row.visible;
        light.intensity = row.intensity;
        std::copy(row.color.begin(), row.color.end(), light.color);
        light.exposure = row.exposure;
        light.diffuse = row.diffuse;
        light.specular = row.specular;
        light.lightLinkCategory = "excluded-receivers";
        light.shadowLinkCategory = "excluded-casters";
        excluded.push_back(light);

        emptyState.ReplaceLight(light);
        Require(!emptyState.HasLightLinks(), std::string(row.name) + ": dark record activated linking");
        bytes = emptyState.BuildPage(nullptr, &commandCount);
        frame = ReadFrame(bytes.data(), bytes.size(), commandCount);
        Require(commandCount == 1u && frame.directLightCount == 0u && frame.lightingFlags == 1u,
            std::string(row.name) + ": retained dark record lost authored flag or emitted linking");
        RequireUnusedDirectSlots(frame, 0, row.name);
        RequireAmbientAndDomeSlots(frame, false, row.name);

        emptyState.RemoveLight(light.path);
        bytes = emptyState.BuildPage(nullptr, &commandCount);
        frame = ReadFrame(bytes.data(), bytes.size(), commandCount);
        Require(commandCount == 1u && frame.directLightCount == 0u &&
                frame.lightingFlags == 0u && !emptyState.HasLightLinks(),
            std::string(row.name) + ": removing the last direct record did not clear the flag");
        RequireUnusedDirectSlots(frame, 0, "last direct removed");
        RequireAmbientAndDomeSlots(frame, false, "last direct removed");
    }
    for (const HdSilkLightRecord& light : excluded)
    {
        emptyState.ReplaceLight(light);
    }
    Require(!emptyState.HasLightLinks(), "all-excluded mixture activated direct linking");
    bytes = emptyState.BuildPage(nullptr, &commandCount);
    frame = ReadFrame(bytes.data(), bytes.size(), commandCount);
    Require(commandCount == 1u && frame.directLightCount == 0u && frame.lightingFlags == 1u,
        "all-excluded mixture must retain only the authored-direct flag");
    RequireUnusedDirectSlots(frame, 0, "all-excluded mixture");
    RequireAmbientAndDomeSlots(frame, false, "all-excluded mixture");
    for (const HdSilkLightRecord& light : excluded)
    {
        emptyState.RemoveLight(light.path);
    }
    bytes = emptyState.BuildPage(nullptr, &commandCount);
    frame = ReadFrame(bytes.data(), bytes.size(), commandCount);
    Require(commandCount == 1u && frame.directLightCount == 0u &&
            frame.lightingFlags == 0u && !emptyState.HasLightLinks(),
        "removing the all-excluded mixture must restore the empty-scene flag");
    RequireUnusedDirectSlots(frame, 0, "mixture removed");
    RequireAmbientAndDomeSlots(frame, false, "mixture removed");

    HdSilkSceneState capacityState;
    for (uint32_t remaining = 128; remaining > 0; --remaining)
    {
        const uint32_t index = remaining - 1;
        const std::string digits = std::to_string(index);
        HdSilkLightRecord light;
        light.path = "/Lights/L" + std::string(3 - digits.size(), '0') + digits;
        light.type = OPENUSD_SILK_LIGHT_DISTANT;
        light.color[0] = 0.25f;
        light.color[1] = 0.5f;
        light.color[2] = 0.75f;
        light.intensity = static_cast<float>(index + 1);
        light.exposure = 1.0f;
        light.diffuse = 0.25f;
        light.specular = 0.75f;
        light.radius = 0.375f;
        capacityState.ReplaceLight(light);
    }
    bytes = capacityState.BuildPage(&revision, &commandCount);
    frame = ReadFrame(bytes.data(), bytes.size(), commandCount);
    Require(revision == 1u && commandCount == 1u &&
            frame.directLightCount == 128u && frame.lightingFlags == 1u,
        "exactly 128 effective direct lights must publish");
    RequireMarkerDirectLights(frame, 128, "128 effective baseline");
    RequireAmbientAndDomeSlots(frame, false, "128 effective baseline");

    // Paths bracket and interleave the selected identities. These distinct,
    // explicitly authored records raise retained cardinality to 133, not
    // effective cardinality: none may shift or occupy a direct slot.
    HdSilkLightRecord hidden;
    hidden.path = "/Lights/AHidden";
    hidden.type = OPENUSD_SILK_LIGHT_DISTANT;
    hidden.visible = false;
    hidden.lightLinkCategory = "hidden-receivers";
    capacityState.ReplaceLight(hidden);

    HdSilkLightRecord zeroIntensity;
    zeroIntensity.path = "/Lights/L031aZeroIntensity";
    zeroIntensity.type = OPENUSD_SILK_LIGHT_DISTANT;
    zeroIntensity.intensity = 0.0f;
    zeroIntensity.lightLinkCategory = "zero-intensity-receivers";
    capacityState.ReplaceLight(zeroIntensity);

    HdSilkLightRecord zeroColor;
    zeroColor.path = "/Lights/L063aZeroColor";
    zeroColor.type = OPENUSD_SILK_LIGHT_SPHERE;
    zeroColor.color[0] = 0.0f;
    zeroColor.color[1] = 0.0f;
    zeroColor.color[2] = 0.0f;
    zeroColor.lightLinkCategory = "zero-color-receivers";
    capacityState.ReplaceLight(zeroColor);

    HdSilkLightRecord zeroMultipliers;
    zeroMultipliers.path = "/Lights/L095aZeroMultipliers";
    zeroMultipliers.type = OPENUSD_SILK_LIGHT_RECT;
    zeroMultipliers.diffuse = 0.0f;
    zeroMultipliers.specular = 0.0f;
    zeroMultipliers.lightLinkCategory = "zero-multiplier-receivers";
    capacityState.ReplaceLight(zeroMultipliers);

    HdSilkLightRecord underflow;
    underflow.path = "/Lights/ZUnderflow";
    underflow.type = OPENUSD_SILK_LIGHT_DISK;
    underflow.exposure = -200.0f;
    underflow.lightLinkCategory = "underflow-receivers";
    capacityState.ReplaceLight(underflow);

    bytes = capacityState.BuildPage(&revision, &commandCount);
    frame = ReadFrame(bytes.data(), bytes.size(), commandCount);
    Require(revision == 2u && commandCount == 1u &&
            frame.directLightCount == 128u && frame.lightingFlags == 1u &&
            !capacityState.HasLightLinks(),
        "128 effective plus five excluded records must publish without capacity refusal or linking");
    RequireMarkerDirectLights(frame, 128, "128 effective plus excluded");
    RequireAmbientAndDomeSlots(frame, false, "128 effective plus excluded");

    // Reuse the positive partition data at the last sorted identity. At the
    // capacity boundary, tiny/single-channel/multiplier controls must still
    // occupy slot 127, even alongside the five excluded records above.
    for (const EligibilityCase& row : EligibilityCases)
    {
        if (row.selectedCount != 1u)
        {
            continue;
        }
        HdSilkLightRecord last;
        last.path = "/Lights/L127";
        last.type = row.type;
        last.visible = row.visible;
        last.intensity = row.intensity;
        std::copy(row.color.begin(), row.color.end(), last.color);
        last.exposure = row.exposure;
        last.diffuse = row.diffuse;
        last.specular = row.specular;
        last.radius = row.radius;
        last.shapeX = row.shapeX;
        last.shapeY = row.shapeY;
        capacityState.ReplaceLight(last);

        const uint64_t previousRevision = revision;
        bytes = capacityState.BuildPage(&revision, &commandCount);
        frame = ReadFrame(bytes.data(), bytes.size(), commandCount);
        const std::string label = std::string("capacity slot 127: ") + row.name;
        Require(revision == previousRevision + 1u && commandCount == 1u &&
                frame.directLightCount == 128u && frame.lightingFlags == 1u &&
                !capacityState.HasLightLinks(),
            label + ": effective boundary control was lost or activated excluded linking");
        RequireMarkerDirectLights(frame, 127, label);
        const DirectSlot& slot = frame.directLights[127];
        Require(std::memcmp(slot.color.data(), row.color.data(), 3 * sizeof(float)) == 0 &&
                std::memcmp(&slot.intensity, &row.intensity, sizeof(float)) == 0 &&
                std::memcmp(&slot.exposure, &row.exposure, sizeof(float)) == 0 &&
                std::memcmp(&slot.diffuse, &row.diffuse, sizeof(float)) == 0 &&
                std::memcmp(&slot.specular, &row.specular, sizeof(float)) == 0,
            label + ": boundary control float bits changed");
        Require(slot.type == row.type && slot.shadowEnabled == 0u &&
                slot.shapeX == row.shapeX && slot.shapeY == row.shapeY &&
                slot.radius == row.radius && slot.transform == IdentityTransform,
            label + ": boundary control shape or transform changed");
        RequireAmbientAndDomeSlots(frame, false, label);
    }
}

// Retained composition-to-wire cases. These readers deliberately stay local:
// the shared header is the FRAME-only contract used by the session probe too.
using Words = std::array<uint32_t, 4>;
constexpr Words FullWords{0xffffffffu, 0xffffffffu, 0xffffffffu, 0xffffffffu};
constexpr Words ZeroWords{0u, 0u, 0u, 0u};
constexpr Words Without127{0xffffffffu, 0xffffffffu, 0xffffffffu, 0x7fffffffu};
constexpr Words BoundaryWithout32{0x80000000u, 0x80000000u, 0x80000001u, 0x80000001u};
const std::vector<std::string> ReceiverBShadowS{
    "r031", "r032", "r063", "r064", "r095", "r096", "r127",
    "s000", "s033", "s062", "s065", "s094", "s097", "s126"};
const std::vector<std::string> ReceiverSShadowB{
    "r000", "r033", "r062", "r065", "r094", "r097", "r126",
    "s031", "s032", "s063", "s064", "s095", "s096", "s127"};
const std::vector<float> CasterPoints3{-1.0f, -2.0f, -2.0f, 1.0f, 2.0f, -2.0f, -1.0f, 2.0f, 2.0f};
const std::vector<float> CasterPoints6{-2.0f, -4.0f, -4.0f, 2.0f, 4.0f, -4.0f, -2.0f, 4.0f, 4.0f};
constexpr std::array<double, 16> TranslatedTransform{
    1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0,
    0.0, 0.0, 1.0, 0.0, 4.0, 5.0, 6.0, 1.0};
constexpr std::array<double, 16> TowardXTransform{
    0.0, 0.0, -1.0, 0.0, 0.0, 1.0, 0.0, 0.0,
    1.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 1.0};

struct WideBitCase
{
    const char* suffix;
    uint32_t bit;
    Words words;
    Words wrongWord;
};
constexpr WideBitCase WideBits[] = {
    {"031", 31u,  {0x80000000u, 0u, 0u, 0u}, {0u, 0u, 0u, 0x80000000u}},
    {"032", 32u,  {0u, 1u, 0u, 0u}, {1u, 0u, 0u, 0u}},
    {"063", 63u,  {0u, 0x80000000u, 0u, 0u}, {0x80000000u, 0u, 0u, 0u}},
    {"064", 64u,  {0u, 0u, 1u, 0u}, {0u, 1u, 0u, 0u}},
    {"095", 95u,  {0u, 0u, 0x80000000u, 0u}, {0u, 0x80000000u, 0u, 0u}},
    {"096", 96u,  {0u, 0u, 0u, 1u}, {0u, 0u, 1u, 0u}},
    {"127", 127u, {0u, 0u, 0u, 0x80000000u}, {0x80000000u, 0u, 0u, 0u}}};

uint64_t ReadU64Le(CommandView command, size_t offset)
{
    return static_cast<uint64_t>(ReadU32Le(command, offset)) |
        (static_cast<uint64_t>(ReadU32Le(command, offset + 4)) << 32);
}

std::string ReadText(CommandView command, size_t offset, size_t count)
{
    Require(offset <= command.size && count <= command.size - offset,
        "retained decoder: truncated string");
    return std::string(reinterpret_cast<const char*>(command.data + offset), count);
}

std::array<double, 16> ReadMatrix(CommandView command, size_t offset)
{
    std::array<double, 16> values{};
    for (size_t element = 0; element < values.size(); ++element)
    {
        values[element] = ReadF64Le(command, offset + 8 * element);
    }
    return values;
}

struct LinkMasks
{
    Words light{};
    Words shadow{};
    uint32_t dome = 0;
};

struct LinkRow
{
    std::string path;
    int32_t instance = -1;
    LinkMasks masks;
};

struct LinkTableView
{
    uint32_t directCount = 0;
    uint32_t domeCount = 0;
    uint32_t reasons = 0;
    std::vector<LinkRow> rows;
};

struct ShadowView
{
    uint32_t lightIndex = 0;
    uint32_t mapIndex = 0;
    uint32_t resolution = 0;
    uint32_t flags = 0;
    std::array<double, 16> view{};
    std::array<double, 16> projection{};
    float depthBias = 0.0f;
    float normalBias = 0.0f;
    float pcfRadius = 0.0f;
    uint32_t reserved = 0;
};

struct ShadowTableView
{
    uint32_t directCount = 0;
    uint32_t reasons = 0;
    uint32_t reserved = 0;
    std::vector<ShadowView> maps;
};

struct MeshView
{
    uint64_t hash = 0;
    std::string path;
    int32_t primId = -1;
    int32_t instanceId = 0;
    int32_t instance = 0;
    uint32_t topology = 0;
    uint64_t topologyRevision = 0;
    uint32_t doubleSided = 0;
    uint32_t cullStyle = 0;
    std::array<float, 4> color{};
    std::array<double, 16> transform{};
    uint64_t materialHash = 0;
    std::string materialPath;
    std::vector<float> points;
    std::vector<uint32_t> indices;
    std::vector<uint32_t> subprims;
    std::string instancerPath;
    std::vector<std::pair<std::string, int32_t>> context;
};

struct ScalarView
{
    uint32_t parameter = 0;
    std::vector<float> components;
};

struct MaterialView
{
    uint64_t hash = 0;
    std::string path;
    uint32_t surface = 0;
    std::vector<ScalarView> scalars;
    std::array<float, 6> uv{};
};

struct RemovalView
{
    uint64_t hash = 0;
    std::string path;
    int32_t instance = -1;
};

struct EnvironmentView
{
    uint64_t hash = 0;
    std::string path;
    std::string texture;
    uint32_t format = 0;
    uint32_t colorSpace = 0;
    uint32_t reasons = 0;
    uint32_t domeIndex = 0;
    std::array<float, 3> color{};
    float intensity = 0.0f;
    float exposure = 0.0f;
    float diffuse = 0.0f;
    float specular = 0.0f;
    std::array<double, 16> transform{};
};

struct RetainedPage
{
    Frame frame;
    std::vector<uint32_t> order;
    std::vector<uint8_t> frameBytes;
    std::vector<uint8_t> linkBytes;
    std::vector<uint8_t> shadowBytes;
    bool hasLinks = false;
    bool hasShadows = false;
    LinkTableView links;
    ShadowTableView shadows;
    std::vector<MeshView> meshes;
    std::vector<MaterialView> materials;
    std::vector<RemovalView> meshRemovals;
    std::vector<RemovalView> materialRemovals;
    std::vector<EnvironmentView> environments;
    std::vector<RemovalView> environmentRemovals;
};

LinkTableView ReadLinks(CommandView command)
{
    LinkTableView table;
    const uint32_t count = ReadU32Le(command, 8);
    table.directCount = ReadU32Le(command, 12);
    table.reasons = ReadU32Le(command, 16);
    table.domeCount = ReadU32Le(command, 20);
    Require(count <= 4096u && table.directCount <= 128u && table.domeCount <= 8u &&
            (table.reasons & ~3u) == 0u,
        "LIGHT_LINK: invalid header");
    size_t cursor = 24;
    for (uint32_t index = 0; index < count; ++index)
    {
        LinkRow row;
        for (size_t word = 0; word < 4; ++word)
        {
            row.masks.light[word] = ReadU32Le(command, cursor + 4 * word);
            row.masks.shadow[word] = ReadU32Le(command, cursor + 16 + 4 * word);
        }
        row.masks.dome = ReadU32Le(command, cursor + 32);
        row.instance = ReadI32Le(command, cursor + 36);
        const uint32_t pathBytes = ReadU32Le(command, cursor + 40);
        row.path = ReadText(command, cursor + 44, pathBytes);
        cursor += 44 + static_cast<size_t>(pathBytes);
        Require(!row.path.empty() && row.path.front() == '/' &&
                row.path.find('\0') == std::string::npos && row.instance >= -1,
            "LIGHT_LINK: invalid path or signed instance identity");
        for (size_t bit = table.directCount; bit < 128; ++bit)
        {
            const uint32_t invalid = uint32_t{1} << (bit % 32);
            Require(((row.masks.light[bit / 32] | row.masks.shadow[bit / 32]) & invalid) == 0u,
                "LIGHT_LINK: mask names an unpublished direct slot");
        }
        Require((row.masks.dome & ~((1u << table.domeCount) - 1u)) == 0u,
            "LIGHT_LINK: mask names an unpublished dome slot");
        if (!table.rows.empty())
        {
            const LinkRow& previous = table.rows.back();
            const bool earlierPath = std::lexicographical_compare(
                previous.path.begin(), previous.path.end(), row.path.begin(), row.path.end(),
                [](char a, char b) { return static_cast<unsigned char>(a) < static_cast<unsigned char>(b); });
            Require(earlierPath || (previous.path == row.path && previous.instance < row.instance),
                "LIGHT_LINK: rows are not unique, path/signed-instance ordered");
        }
        table.rows.push_back(std::move(row));
    }
    Require(cursor == command.size, "LIGHT_LINK: trailing or missing entry bytes");
    return table;
}

ShadowTableView ReadShadows(CommandView command)
{
    ShadowTableView table;
    const uint32_t count = ReadU32Le(command, 8);
    table.directCount = ReadU32Le(command, 12);
    table.reasons = ReadU32Le(command, 16);
    table.reserved = ReadU32Le(command, 20);
    Require(count <= 4u && table.directCount <= 128u && (table.reasons & ~7u) == 0u &&
            table.reserved == 0u && command.size == 24 + 288 * static_cast<size_t>(count),
        "SHADOW: invalid header, reserved word or 288-byte descriptor stride");
    for (uint32_t index = 0; index < count; ++index)
    {
        const size_t offset = 24 + 288 * static_cast<size_t>(index);
        ShadowView map;
        map.lightIndex = ReadU32Le(command, offset);
        map.mapIndex = ReadU32Le(command, offset + 4);
        map.resolution = ReadU32Le(command, offset + 8);
        map.flags = ReadU32Le(command, offset + 12);
        map.view = ReadMatrix(command, offset + 16);
        map.projection = ReadMatrix(command, offset + 144);
        map.depthBias = ReadF32Le(command, offset + 272);
        map.normalBias = ReadF32Le(command, offset + 276);
        map.pcfRadius = ReadF32Le(command, offset + 280);
        map.reserved = ReadU32Le(command, offset + 284);
        Require(map.lightIndex < table.directCount && map.mapIndex == index &&
                (map.flags & ~3u) == 0u && map.reserved == 0u,
            "SHADOW: invalid descriptor identity, flags or reserved word");
        table.maps.push_back(map);
    }
    return table;
}

MeshView ReadMesh(CommandView command)
{
    MeshView mesh;
    mesh.hash = ReadU64Le(command, 8);
    mesh.primId = ReadI32Le(command, 16);
    mesh.instanceId = ReadI32Le(command, 20);
    mesh.instance = ReadI32Le(command, 24);
    mesh.topology = ReadU32Le(command, 28);
    mesh.topologyRevision = ReadU64Le(command, 32);
    mesh.doubleSided = ReadU32Le(command, 40);
    mesh.cullStyle = ReadU32Le(command, 44);
    const uint32_t pathBytes = ReadU32Le(command, 48);
    const uint32_t points = ReadU32Le(command, 52);
    const uint32_t indices = ReadU32Le(command, 56);
    const uint32_t subprims = ReadU32Le(command, 60);
    for (size_t component = 0; component < 4; ++component)
    {
        mesh.color[component] = ReadF32Le(command, 64 + 4 * component);
    }
    mesh.transform = ReadMatrix(command, 80);
    mesh.materialHash = ReadU64Le(command, 208);
    const uint32_t materialBytes = ReadU32Le(command, 216);
    // These fixtures intentionally have no attributes, rig or subprim tables.
    // Reject unexpected sections instead of pretending a partial read is full.
    for (size_t offset = 220; offset <= 256; offset += 4)
    {
        Require(ReadU32Le(command, offset) == 0u,
            "MESH_UPSERT: unexpected attribute/deformation/subprim section");
    }
    const uint32_t instancerBytes = ReadU32Le(command, 260);
    const uint32_t contextCount = ReadU32Le(command, 264);
    mesh.path = ReadText(command, 268, pathBytes);
    size_t cursor = 268 + static_cast<size_t>(pathBytes);
    for (size_t component = 0; component < 3 * static_cast<size_t>(points); ++component, cursor += 4)
    {
        mesh.points.push_back(ReadF32Le(command, cursor));
    }
    for (uint32_t index = 0; index < indices; ++index, cursor += 4)
    {
        mesh.indices.push_back(ReadU32Le(command, cursor));
    }
    for (uint32_t index = 0; index < subprims; ++index, cursor += 4)
    {
        mesh.subprims.push_back(ReadU32Le(command, cursor));
    }
    mesh.materialPath = ReadText(command, cursor, materialBytes);
    cursor += materialBytes;
    mesh.instancerPath = ReadText(command, cursor, instancerBytes);
    cursor += instancerBytes;
    Require(contextCount <= 64u, "MESH_UPSERT: excessive instancer context");
    for (uint32_t index = 0; index < contextCount; ++index)
    {
        const uint32_t bytes = ReadU32Le(command, cursor);
        const int32_t identity = ReadI32Le(command, cursor + 4);
        mesh.context.emplace_back(ReadText(command, cursor + 8, bytes), identity);
        cursor += 8 + static_cast<size_t>(bytes);
    }
    Require(cursor == command.size, "MESH_UPSERT: incomplete command consumption");
    return mesh;
}

MaterialView ReadMaterial(CommandView command)
{
    MaterialView material;
    material.hash = ReadU64Le(command, 8);
    const uint32_t pathBytes = ReadU32Le(command, 16);
    material.surface = ReadU32Le(command, 20);
    const uint32_t scalarCount = ReadU32Le(command, 24);
    Require(ReadU32Le(command, 28) == 0u, "MATERIAL_UPSERT: fixture unexpectedly has textures");
    material.path = ReadText(command, 32, pathBytes);
    size_t cursor = 32 + static_cast<size_t>(pathBytes);
    for (uint32_t index = 0; index < scalarCount; ++index)
    {
        ScalarView scalar;
        scalar.parameter = ReadU32Le(command, cursor);
        const uint32_t count = ReadU32Le(command, cursor + 4);
        Require(count >= 1u && count <= 4u, "MATERIAL_UPSERT: invalid scalar component count");
        cursor += 8;
        for (uint32_t component = 0; component < count; ++component, cursor += 4)
        {
            scalar.components.push_back(ReadF32Le(command, cursor));
        }
        material.scalars.push_back(std::move(scalar));
    }
    Require(ReadU32Le(command, cursor) == 0u && ReadU32Le(command, cursor + 4) == 0u,
        "MATERIAL_UPSERT: unexpected generated shader sections");
    cursor += 8;
    for (size_t component = 0; component < 6; ++component, cursor += 4)
    {
        material.uv[component] = ReadF32Le(command, cursor);
    }
    Require(cursor == command.size, "MATERIAL_UPSERT: incomplete command consumption");
    return material;
}

RemovalView ReadRemoval(CommandView command, bool mesh)
{
    RemovalView removal;
    removal.hash = ReadU64Le(command, 8);
    removal.instance = mesh ? ReadI32Le(command, 16) : -1;
    const size_t start = mesh ? 24 : 20;
    const uint32_t count = ReadU32Le(command, start - 4);
    removal.path = ReadText(command, start, count);
    Require(start + count == command.size && !removal.path.empty() &&
            removal.path.front() == '/' && (!mesh || removal.instance >= 0),
        "REMOVE: invalid identity or trailing bytes");
    return removal;
}

EnvironmentView ReadEnvironment(CommandView command)
{
    EnvironmentView environment;
    environment.hash = ReadU64Le(command, 8);
    const uint32_t pathBytes = ReadU32Le(command, 16);
    const uint32_t textureBytes = ReadU32Le(command, 20);
    environment.format = ReadU32Le(command, 24);
    environment.colorSpace = ReadU32Le(command, 28);
    environment.reasons = ReadU32Le(command, 32);
    environment.domeIndex = ReadU32Le(command, 36);
    for (size_t channel = 0; channel < 3; ++channel)
    {
        environment.color[channel] = ReadF32Le(command, 40 + 4 * channel);
    }
    environment.intensity = ReadF32Le(command, 52);
    environment.exposure = ReadF32Le(command, 56);
    environment.diffuse = ReadF32Le(command, 60);
    environment.specular = ReadF32Le(command, 64);
    Require(ReadU32Le(command, 68) == 0u, "ENVIRONMENT: nonzero reserved word");
    environment.transform = ReadMatrix(command, 72);
    environment.path = ReadText(command, 200, pathBytes);
    environment.texture = ReadText(command, 200 + static_cast<size_t>(pathBytes), textureBytes);
    Require(200 + static_cast<size_t>(pathBytes) + textureBytes == command.size,
        "ENVIRONMENT: incomplete command consumption");
    return environment;
}

RetainedPage ReadRetainedPage(const std::vector<uint8_t>& bytes, uint32_t count)
{
    RetainedPage page;
    page.frame = ReadFrame(bytes.data(), bytes.size(), count);
    VisitCommands(bytes.data(), bytes.size(), count, [&](uint32_t type, CommandView command)
    {
        page.order.push_back(type);
        switch (type)
        {
        case 1u: page.frameBytes.assign(command.data, command.data + command.size); break;
        case 2u: page.meshes.push_back(ReadMesh(command)); break;
        case 3u: page.meshRemovals.push_back(ReadRemoval(command, true)); break;
        case 4u: page.materials.push_back(ReadMaterial(command)); break;
        case 5u: page.materialRemovals.push_back(ReadRemoval(command, false)); break;
        case 6u: page.environments.push_back(ReadEnvironment(command)); break;
        case 7u: page.environmentRemovals.push_back(ReadRemoval(command, false)); break;
        case 8u:
            Require(!page.hasLinks, "duplicate LIGHT_LINK");
            page.hasLinks = true;
            page.links = ReadLinks(command);
            page.linkBytes.assign(command.data, command.data + command.size);
            break;
        case 9u:
            Require(!page.hasShadows, "duplicate SHADOW");
            page.hasShadows = true;
            page.shadows = ReadShadows(command);
            page.shadowBytes.assign(command.data, command.data + command.size);
            break;
        default: throw std::runtime_error("unexpected retained command " + std::to_string(type));
        }
    });
    return page;
}

void RequireOrder(const RetainedPage& page, std::initializer_list<uint32_t> order, const std::string& label)
{
    if (page.order == std::vector<uint32_t>(order))
    {
        return;
    }
    std::string diagnostic = label + ": command order expected";
    for (uint32_t type : order)
    {
        diagnostic += " " + std::to_string(type);
    }
    diagnostic += "; actual";
    for (uint32_t type : page.order)
    {
        diagnostic += " " + std::to_string(type);
    }
    throw std::runtime_error(diagnostic);
}

LinkMasks LookupPublished(
    const LinkTableView& table, const std::string& path, int32_t instance,
    const Words& defaultDirect = FullWords, uint32_t defaultDome = 0u)
{
    const LinkRow* fallback = nullptr;
    for (const LinkRow& row : table.rows)
    {
        if (row.path != path)
        {
            continue;
        }
        if (row.instance == instance)
        {
            return row.masks;
        }
        if (row.instance == -1)
        {
            fallback = &row;
        }
    }
    return fallback != nullptr ? fallback->masks : LinkMasks{defaultDirect, defaultDirect, defaultDome};
}

void RequireMasks(
    const LinkMasks& actual, const Words& light, const Words& shadow, uint32_t dome,
    const std::string& label)
{
    for (size_t word = 0; word < 4; ++word)
    {
        Require(actual.light[word] == light[word],
            label + ": receiver word " + std::to_string(word) + " expected " +
            std::to_string(light[word]) + ", actual " + std::to_string(actual.light[word]));
        Require(actual.shadow[word] == shadow[word],
            label + ": caster word " + std::to_string(word) + " expected " +
            std::to_string(shadow[word]) + ", actual " + std::to_string(actual.shadow[word]));
    }
    Require(actual.dome == dome, label + ": wrong independent dome word");
}

struct ShadowOracle
{
    std::array<double, 16> view;
    std::array<double, 16> projection;
    float normalBias;
};

// Row-vector OpenGL clip depth, independently solved from the fixture boxes:
// radius 3 -> near/far 4/10, radius 6 -> 7/19; eye distances 7 and 13.
// The X camera basis is (-Z, +Y, +X), not the light's local-to-world matrix.
constexpr std::array<double, 16> Projection3{
    0.333333333333333333, 0.0, 0.0, 0.0,
    0.0, 0.333333333333333333, 0.0, 0.0,
    0.0, 0.0, -0.333333333333333333, 0.0,
    0.0, 0.0, -2.333333333333333333, 1.0};
constexpr std::array<double, 16> Projection6{
    0.166666666666666667, 0.0, 0.0, 0.0,
    0.0, 0.166666666666666667, 0.0, 0.0,
    0.0, 0.0, -0.166666666666666667, 0.0,
    0.0, 0.0, -2.166666666666666667, 1.0};
constexpr ShadowOracle Z3{
    {1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0,
     0.0, 0.0, 1.0, 0.0, 0.0, 0.0, -7.0, 1.0},
    Projection3, 0.0087890625f};
constexpr ShadowOracle X3{
    {0.0, 0.0, 1.0, 0.0, 0.0, 1.0, 0.0, 0.0,
     -1.0, 0.0, 0.0, 0.0, 0.0, 0.0, -7.0, 1.0},
    Projection3, 0.0087890625f};
constexpr ShadowOracle Z6{
    {1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0,
     0.0, 0.0, 1.0, 0.0, -4.0, -5.0, -19.0, 1.0},
    Projection6, 0.017578125f};
constexpr ShadowOracle X6{
    {0.0, 0.0, 1.0, 0.0, 0.0, 1.0, 0.0, 0.0,
     -1.0, 0.0, 0.0, 0.0, 6.0, -5.0, -17.0, 1.0},
    Projection6, 0.017578125f};

void RequireShadowHeader(
    const RetainedPage& page, size_t count, uint32_t directCount, uint32_t reasons,
    const std::string& label)
{
    Require(page.hasShadows && page.shadows.maps.size() == count &&
            page.shadows.directCount == directCount && page.shadows.reasons == reasons &&
            page.shadows.reserved == 0u && page.shadowBytes.size() == 24 + 288 * count,
        label + ": SHADOW expected count/direct/reasons " + std::to_string(count) + "/" +
        std::to_string(directCount) + "/" + std::to_string(reasons) + "; actual " +
        std::to_string(page.shadows.maps.size()) + "/" + std::to_string(page.shadows.directCount) +
        "/" + std::to_string(page.shadows.reasons) + " (also requires zero reserved and 288-byte stride)");
}

void RequireShadow(
    const ShadowView& actual, uint32_t light, uint32_t map, uint32_t flags,
    const ShadowOracle& expected, const std::string& label)
{
    Require(actual.lightIndex == light && actual.mapIndex == map &&
            actual.resolution == 1024u && actual.flags == flags && actual.reserved == 0u,
        label + ": wrong descriptor identity/resolution/flags/reserved");
    for (size_t element = 0; element < 16; ++element)
    {
        Require(std::abs(actual.view[element] - expected.view[element]) <=
                1e-12 * std::max(1.0, std::abs(expected.view[element])),
            label + ": view element " + std::to_string(element));
        Require(std::abs(actual.projection[element] - expected.projection[element]) <=
                1e-12 * std::max(1.0, std::abs(expected.projection[element])),
            label + ": projection element " + std::to_string(element));
    }
    Require(actual.depthBias == 0.00146484375f && actual.normalBias == expected.normalBias &&
            actual.pcfRadius == 1.0f,
        label + ": wrong normalized depth bias, world normal bias or PCF radius");
}

void RequireRoughness(
    const MaterialView& material, const std::string& path, float roughness, const std::string& label)
{
    Require(material.path == path && material.hash != 0u && material.surface == 1u &&
            material.scalars.size() == 1 && material.scalars[0].parameter == 5u &&
            material.scalars[0].components == std::vector<float>{roughness} &&
            material.uv == std::array<float, 6>{1.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f},
        label + ": wrong Preview Surface identity, roughness or trailing UV transform");
}

void RequireTriangle(
    const MeshView& mesh, const std::string& path, int32_t primId, uint64_t topologyRevision,
    const std::vector<float>& points, const std::array<double, 16>& transform,
    const std::string& materialPath, uint64_t materialHash, const std::string& label)
{
    Require(mesh.path == path && mesh.hash != 0u && mesh.primId == primId &&
            mesh.instanceId == 0 && mesh.instance == 0 && mesh.topology == 1u &&
            mesh.topologyRevision == topologyRevision && mesh.doubleSided == 1u && mesh.cullStyle == 4u,
        label + ": wrong concrete mesh identity/topology/presentation");
    Require(mesh.points == points && mesh.indices == std::vector<uint32_t>{0u, 1u, 2u} &&
            mesh.subprims == std::vector<uint32_t>{0u} && mesh.transform == transform &&
            mesh.materialPath == materialPath && mesh.materialHash == materialHash &&
            mesh.instancerPath.empty() && mesh.context.empty() &&
            mesh.color == std::array<float, 4>{0.7f, 0.7f, 0.7f, 1.0f},
        label + ": wrong points, transform, triangle, material binding or unexpected instancing");
}

void VerifyOverflowPreservesRetainedTransaction()
{
    HdSilkSceneState state;
    state.SetComplexity(OPENUSD_SILK_COMPLEXITY_LOW);
    state.SetDrawMode(OPENUSD_SILK_DRAW_MODE_SMOOTH_SHADED);
    std::vector<HdSilkLightRecord> lights;
    std::vector<std::string> keepCategories{
        "r031", "r032", "r063", "r064", "r095", "r096", "r127"};
    for (uint32_t index = 0; index < 128; ++index)
    {
        const std::string digits = std::to_string(index);
        const std::string suffix = std::string(3 - digits.size(), '0') + digits;
        HdSilkLightRecord light;
        light.path = "/Lights/L" + suffix;
        light.type = OPENUSD_SILK_LIGHT_DISTANT;
        light.intensity = static_cast<float>(index + 1);
        light.lightLinkCategory = "r" + suffix;
        light.shadowLinkCategory = "s" + suffix;
        light.shadowEnabled = index == 127 ? 1u : 0u;
        if (index == 127)
        {
            std::copy(TowardXTransform.begin(), TowardXTransform.end(), light.transform);
        }
        state.ReplaceLight(light);
        lights.push_back(light);
        keepCategories.push_back("s" + suffix);
    }
    HdSilkMaterialScalar roughness;
    roughness.parameter = OPENUSD_SILK_MATERIAL_ROUGHNESS;
    roughness.componentCount = 1u;
    roughness.value[0] = 0.25f;
    HdSilkMaterialRecord keepMaterial;
    keepMaterial.path = "/Materials/Keep";
    keepMaterial.surfaceKind = OPENUSD_SILK_SURFACE_PREVIEW_SURFACE;
    keepMaterial.scalars = {roughness};
    HdSilkMaterialRecord goneMaterial;
    goneMaterial.path = "/Materials/Gone";
    goneMaterial.surfaceKind = OPENUSD_SILK_SURFACE_PREVIEW_SURFACE;
    goneMaterial.scalars = {roughness};
    state.ReplaceMaterial(keepMaterial);
    state.ReplaceMaterial(goneMaterial);

    HdSilkMeshRecord keep;
    keep.path = "/Geom/Keep";
    keep.primId = 11;
    keep.topologyRevision = 1;
    keep.points = CasterPoints3;
    keep.indices = {0u, 1u, 2u};
    keep.triangleSubprims = {0u};
    keep.materialPath = "/Materials/Keep";
    HdSilkMeshRecord gone;
    gone.path = "/Geom/Gone";
    gone.primId = 12;
    gone.topologyRevision = 1;
    gone.points = {0.0f, 0.0f, 0.0f, 0.5f, 0.0f, 0.0f, 0.0f, 0.5f, 0.0f};
    gone.indices = {0u, 1u, 2u};
    gone.triangleSubprims = {0u};
    gone.materialPath = "/Materials/Gone";
    state.ReplaceMeshInstances(keep.path, {keep});
    state.ReplaceMeshInstances(gone.path, {gone});
    state.SetCategoryMemberships({
        HdSilkCategoryMembership{keep.path, -1, keepCategories},
        HdSilkCategoryMembership{gone.path, -1, keepCategories}}, false);

    uint64_t revision = 0;
    uint32_t count = 0;
    const std::vector<uint8_t> baselineBytes = state.BuildPage(&revision, &count);
    const uint64_t baselineRevision = revision;
    const RetainedPage baseline = ReadRetainedPage(baselineBytes, count);
    RequireOrder(baseline, {1u, 8u, 9u, 4u, 4u, 2u, 2u}, "overflow baseline");
    Require(baselineRevision == 1u && baseline.frame.directLightCount == 128u &&
            baseline.links.rows.size() == 2 && baseline.links.directCount == 128u &&
            baseline.links.reasons == 0u && baseline.links.domeCount == 0u,
        "overflow baseline: wrong revision, count or table");
    RequireMasks(LookupPublished(baseline.links, keep.path, 0), BoundaryWords, FullWords, 0u, "baseline Keep");
    RequireMasks(LookupPublished(baseline.links, gone.path, 0), BoundaryWords, FullWords, 0u, "baseline Gone");
    RequireRoughness(baseline.materials[0], goneMaterial.path, 0.25f, "baseline Gone material");
    RequireRoughness(baseline.materials[1], keepMaterial.path, 0.25f, "baseline Keep material");
    Require(baseline.materials[0].hash != baseline.materials[1].hash &&
            baseline.meshes[0].hash != baseline.meshes[1].hash,
        "overflow baseline: distinct paths share an identity");
    RequireTriangle(baseline.meshes[0], gone.path, 12, 1, gone.points, IdentityTransform,
        goneMaterial.path, baseline.materials[0].hash, "baseline Gone mesh");
    RequireTriangle(baseline.meshes[1], keep.path, 11, 1, CasterPoints3, IdentityTransform,
        keepMaterial.path, baseline.materials[1].hash, "baseline Keep mesh");
    RequireShadowHeader(baseline, 1, 128u, 0u, "overflow baseline");
    RequireShadow(baseline.shadows.maps[0], 127u, 0u, 1u, X3, "baseline high-127 shadow");

    keep.points = CasterPoints6;
    keep.topologyRevision = 2;
    std::copy(TranslatedTransform.begin(), TranslatedTransform.end(), keep.transform);
    keepMaterial.scalars[0].value[0] = 0.75f;
    state.ReplaceMeshInstances(keep.path, {keep});
    state.ReplaceMaterial(keepMaterial);
    state.RemoveMesh(gone.path);
    state.RemoveMaterial(goneMaterial.path);
    keepCategories.erase(std::remove(keepCategories.begin(), keepCategories.end(), "r032"), keepCategories.end());
    keepCategories.erase(std::remove(keepCategories.begin(), keepCategories.end(), "s127"), keepCategories.end());
    state.SetCategoryMemberships({HdSilkCategoryMembership{keep.path, -1, keepCategories}}, false);
    lights[126].shadowEnabled = 1u;
    state.ReplaceLight(lights[126]);
    HdSilkLightRecord extra;
    extra.path = "/Lights/L128";
    extra.type = OPENUSD_SILK_LIGHT_DISTANT;
    extra.intensity = 129.0f;
    state.ReplaceLight(extra);

    HdSilkLightRecord laterExtra;
    laterExtra.path = "/Lights/L129";
    laterExtra.type = OPENUSD_SILK_LIGHT_DISTANT;
    laterExtra.intensity = 130.0f;
    for (int attempt = 0; attempt < 3; ++attempt)
    {
        // A later invalid cardinality distinguishes the bounded guard from an
        // accidental equality check for just the first blocked count, 129.
        if (attempt == 2)
        {
            state.ReplaceLight(laterExtra);
        }
        const uint32_t effectiveCount = attempt == 2 ? 130u : 129u;
        const std::string expectedError = "hdSilk refused the frame: " +
            std::to_string(effectiveCount) +
            " effective direct lights exceed the bounded limit of 128. "
            "Reduce visible, nonzero direct lights to 128 or fewer and retry; no command page was published.";
        uint64_t refusedRevision = 0x0123456789abcdefull;
        uint32_t refusedCount = 0xa5a5a5a5u;
        std::vector<uint8_t> refusedBytes{0xa5u, 0x5au};
        bool rejected = false;
        try
        {
            refusedBytes = state.BuildPage(&refusedRevision, &refusedCount);
        }
        catch (const std::length_error& error)
        {
            Require(typeid(error) == typeid(std::length_error) && error.what() == expectedError,
                "overflow: exception must be exactly length_error with the actual count/bound diagnostic");
            rejected = true;
        }
        Require(rejected && refusedRevision == 0x0123456789abcdefull &&
                refusedCount == 0xa5a5a5a5u && refusedBytes == std::vector<uint8_t>{0xa5u, 0x5au},
            "overflow attempt " + std::to_string(attempt) + ": output sentinels changed or no refusal");
    }

    // Only the two over-capacity lights are removed. None of the pending edits
    // may be replayed: that would hide consumption by a refused preflight.
    state.RemoveLight(laterExtra.path);
    state.RemoveLight(extra.path);
    const std::vector<uint8_t> retryBytes = state.BuildPage(&revision, &count);
    const RetainedPage retry = ReadRetainedPage(retryBytes, count);
    RequireOrder(retry, {1u, 8u, 9u, 4u, 5u, 2u, 3u}, "overflow retry");
    Require(count == 7u && revision == baselineRevision + 1u &&
            retry.frame.directLightCount == 128u && retry.frame.lightingFlags == 1u &&
            retry.links.rows.size() == 1 && retry.links.rows[0].path == keep.path &&
            retry.links.rows[0].instance == -1 && retry.links.directCount == 128u &&
            retry.links.domeCount == 0u && retry.links.reasons == 0u,
        "overflow retry: revision, exact seven commands or current link identities were lost");
    for (size_t index = 0; index < 128; ++index)
    {
        Require(retry.frame.directLights[index].type == 1u &&
                retry.frame.directLights[index].intensity == static_cast<float>(index + 1) &&
                retry.frame.directLights[index].shadowEnabled == (index >= 126 ? 1u : 0u),
            "overflow retry: wrong current direct identity/control at " + std::to_string(index));
    }
    RequireMasks(retry.links.rows[0].masks, BoundaryWithout32, Without127, 0u, "retry Keep");
    RequireRoughness(retry.materials[0], keepMaterial.path, 0.75f, "retry material");
    Require(retry.materials[0].hash == baseline.materials[1].hash &&
            retry.meshes[0].hash == baseline.meshes[1].hash,
        "overflow retry: surviving mesh/material identity rotated");
    RequireTriangle(retry.meshes[0], keep.path, 11, 2, CasterPoints6, TranslatedTransform,
        keepMaterial.path, baseline.materials[1].hash, "retry revised geometry and binding");
    Require(retry.meshRemovals.size() == 1 && retry.meshRemovals[0].path == gone.path &&
            retry.meshRemovals[0].instance == 0 && retry.meshRemovals[0].hash == baseline.meshes[0].hash &&
            retry.materialRemovals.size() == 1 && retry.materialRemovals[0].path == goneMaterial.path &&
            retry.materialRemovals[0].hash == baseline.materials[0].hash,
        "overflow retry: both exact Gone removals must survive every failure, once each");
    RequireShadowHeader(retry, 2, 128u, 0u, "overflow retry");
    RequireShadow(retry.shadows.maps[0], 126u, 0u, 1u, Z6, "retry added distant 126");
    RequireShadow(retry.shadows.maps[1], 127u, 1u, 3u, X6, "retry caster-excluded distant 127");
    Require(retry.frameBytes != baseline.frameBytes && retry.linkBytes != baseline.linkBytes &&
            retry.shadowBytes != baseline.shadowBytes,
        "overflow retry did not publish the intersecting frame/link/shadow edits");
    for (uint64_t step = 2; step <= 3; ++step)
    {
        const std::vector<uint8_t> quietBytes = state.BuildPage(&revision, &count);
        const RetainedPage quiet = ReadRetainedPage(quietBytes, count);
        RequireOrder(quiet, {1u}, "overflow subsequent quiet page");
        Require(revision == baselineRevision + step && quiet.frameBytes == retry.frameBytes,
            "overflow quiet page: revision did not advance or FRAME changed");
    }
    const RetainedPage stillOwned = ReadRetainedPage(baselineBytes, 7u);
    Require(stillOwned.frameBytes == baseline.frameBytes && stillOwned.linkBytes == baseline.linkBytes &&
            stillOwned.meshes[1].points == CasterPoints3 &&
            stillOwned.materials[1].scalars[0].components == std::vector<float>{0.25f},
        "later retained publication altered the independently owned baseline bytes");
}

void VerifyRemoveReinsertCancelsPendingRemovals()
{
    HdSilkSceneState state;
    HdSilkMaterialScalar roughness;
    roughness.parameter = OPENUSD_SILK_MATERIAL_ROUGHNESS;
    roughness.componentCount = 1u;
    roughness.value[0] = 0.25f;
    HdSilkMaterialRecord material;
    material.path = "/Materials/Reborn";
    material.surfaceKind = OPENUSD_SILK_SURFACE_PREVIEW_SURFACE;
    material.scalars = {roughness};
    HdSilkMeshRecord mesh;
    mesh.path = "/Geom/Reborn";
    mesh.primId = 41;
    mesh.topologyRevision = 1;
    mesh.points = CasterPoints3;
    mesh.indices = {0u, 1u, 2u};
    mesh.triangleSubprims = {0u};
    mesh.materialPath = material.path;
    state.ReplaceMaterial(material);
    state.ReplaceMeshInstances(mesh.path, {mesh});
    uint64_t revision = 0;
    uint32_t count = 0;
    std::vector<uint8_t> bytes = state.BuildPage(&revision, &count);
    const RetainedPage baseline = ReadRetainedPage(bytes, count);
    RequireOrder(baseline, {1u, 4u, 2u}, "reinsert baseline");
    Require(revision == 1u, "reinsert baseline revision");
    RequireRoughness(baseline.materials[0], material.path, 0.25f, "reinsert baseline material");
    RequireTriangle(baseline.meshes[0], mesh.path, 41, 1, CasterPoints3, IdentityTransform,
        material.path, baseline.materials[0].hash, "reinsert baseline mesh");

    state.RemoveMesh(mesh.path);
    state.RemoveMaterial(material.path);
    // An already-absent removal must not survive cancellation as a duplicate.
    state.RemoveMesh(mesh.path);
    state.RemoveMaterial(material.path);
    mesh.points = CasterPoints6;
    mesh.topologyRevision = 2;
    std::copy(TranslatedTransform.begin(), TranslatedTransform.end(), mesh.transform);
    material.scalars[0].value[0] = 0.75f;
    state.ReplaceMeshInstances(mesh.path, {mesh});
    state.ReplaceMaterial(material);
    bytes = state.BuildPage(&revision, &count);
    const RetainedPage reborn = ReadRetainedPage(bytes, count);
    RequireOrder(reborn, {1u, 4u, 2u}, "remove/reinsert cancellation");
    Require(revision == 2u && reborn.meshRemovals.empty() && reborn.materialRemovals.empty() &&
            reborn.meshes[0].hash == baseline.meshes[0].hash &&
            reborn.materials[0].hash == baseline.materials[0].hash,
        "remove/reinsert emitted stale removals or changed surviving identities/revision");
    RequireRoughness(reborn.materials[0], material.path, 0.75f, "reinsert current material");
    RequireTriangle(reborn.meshes[0], mesh.path, 41, 2, CasterPoints6, TranslatedTransform,
        material.path, baseline.materials[0].hash, "reinsert current mesh");
    bytes = state.BuildPage(&revision, &count);
    const RetainedPage quiet = ReadRetainedPage(bytes, count);
    RequireOrder(quiet, {1u}, "reinsert quiet page");
    Require(revision == 3u && quiet.frameBytes == reborn.frameBytes,
        "remove/reinsert failed to settle at the next revision");
}

void VerifySteadyPagesAndTableRetirement()
{
    HdSilkSceneState state;
    std::vector<std::string> categories{"r031", "r032", "r063", "r064", "r095", "r096", "r127"};
    HdSilkLightRecord last;
    for (uint32_t index = 0; index < 128; ++index)
    {
        const std::string digits = std::to_string(index);
        const std::string suffix = std::string(3 - digits.size(), '0') + digits;
        HdSilkLightRecord light;
        light.path = "/Lights/L" + suffix;
        light.type = OPENUSD_SILK_LIGHT_DISTANT;
        light.intensity = static_cast<float>(index + 1);
        light.lightLinkCategory = "r" + suffix;
        light.shadowLinkCategory = "s" + suffix;
        light.shadowEnabled = index == 127 ? 1u : 0u;
        categories.push_back("s" + suffix);
        state.ReplaceLight(light);
        if (index == 127)
        {
            last = light;
        }
    }
    HdSilkMeshRecord mesh;
    mesh.path = "/Geom/Steady";
    mesh.primId = 51;
    mesh.topologyRevision = 1;
    mesh.points = CasterPoints3;
    mesh.indices = {0u, 1u, 2u};
    mesh.triangleSubprims = {0u};
    state.ReplaceMeshInstances(mesh.path, {mesh});
    state.SetCategoryMemberships({HdSilkCategoryMembership{mesh.path, -1, categories}}, false);
    uint64_t revision = 0;
    uint32_t count = 0;
    std::vector<uint8_t> bytes = state.BuildPage(&revision, &count);
    const RetainedPage baseline = ReadRetainedPage(bytes, count);
    RequireOrder(baseline, {1u, 8u, 9u, 2u}, "steady baseline");
    RequireMasks(baseline.links.rows.at(0).masks, BoundaryWords, FullWords, 0u, "steady baseline masks");
    RequireShadowHeader(baseline, 1, 128u, 0u, "steady baseline");
    RequireShadow(baseline.shadows.maps[0], 127u, 0u, 1u, Z3, "steady baseline shadow");
    for (uint64_t expectedRevision : {2ull, 3ull})
    {
        state.ReplaceLight(last);
        state.RemoveLight("/Lights/AlreadyAbsent");
        bytes = state.BuildPage(&revision, &count);
        const RetainedPage quiet = ReadRetainedPage(bytes, count);
        RequireOrder(quiet, {1u}, "same-valued replacement/absent removal");
        Require(revision == expectedRevision && quiet.frameBytes == baseline.frameBytes,
            "steady FRAME/revision changed after a no-op light edit");
    }
    last.intensity = 256.0f;
    state.ReplaceLight(last);
    bytes = state.BuildPage(&revision, &count);
    const RetainedPage controlEdit = ReadRetainedPage(bytes, count);
    RequireOrder(controlEdit, {1u}, "frame-only control edit");
    Require(revision == 4u && controlEdit.frameBytes != baseline.frameBytes &&
            controlEdit.frame.directLights[127].intensity == 256.0f &&
            controlEdit.frame.directLightCount == 128u,
        "frame control edit must not invent link/shadow changes");

    state.SetCategoryMemberships({}, false);
    bytes = state.BuildPage(&revision, &count);
    const RetainedPage linksRetired = ReadRetainedPage(bytes, count);
    RequireOrder(linksRetired, {1u, 8u}, "LINK retirement");
    Require(revision == 5u && linksRetired.links.rows.empty() && linksRetired.links.reasons == 0u &&
            linksRetired.links.directCount == 0u && linksRetired.links.domeCount == 0u &&
            linksRetired.frameBytes == controlEdit.frameBytes,
        "LINK retirement must canonicalize both counts without disturbing current FRAME");
    bytes = state.BuildPage(&revision, &count);
    const RetainedPage linksQuiet = ReadRetainedPage(bytes, count);
    RequireOrder(linksQuiet, {1u}, "LINK retirement settles");
    Require(revision == 6u && linksQuiet.frameBytes == controlEdit.frameBytes, "LINK quiet revision/FRAME");

    last.shadowEnabled = 0u;
    state.ReplaceLight(last);
    bytes = state.BuildPage(&revision, &count);
    const RetainedPage shadowsRetired = ReadRetainedPage(bytes, count);
    RequireOrder(shadowsRetired, {1u, 9u}, "SHADOW retirement");
    // This count is source-confirmed for no enabled shadows, not borrowed
    // from LINK's empty-table convention. Active unsupported shadows keep 128.
    RequireShadowHeader(shadowsRetired, 0, 0u, 0u, "SHADOW retirement");
    Require(revision == 7u && shadowsRetired.frame.directLightCount == 128u &&
            shadowsRetired.frame.directLights[127].shadowEnabled == 0u,
        "SHADOW retirement lost the still-effective direct light");
    bytes = state.BuildPage(&revision, &count);
    const RetainedPage shadowsQuiet = ReadRetainedPage(bytes, count);
    RequireOrder(shadowsQuiet, {1u}, "SHADOW retirement settles");
    Require(revision == 8u && shadowsQuiet.frameBytes == shadowsRetired.frameBytes,
        "SHADOW retirement repeated or did not settle");
}

void VerifyWideLinkWordsAndSparseFallbacks()
{
    HdSilkSceneState state;
    std::vector<std::string> allCategories;
    for (uint32_t index = 0; index < 128; ++index)
    {
        const std::string digits = std::to_string(index);
        const std::string suffix = std::string(3 - digits.size(), '0') + digits;
        HdSilkLightRecord light;
        light.path = "/Lights/L" + suffix;
        light.type = OPENUSD_SILK_LIGHT_DISTANT;
        light.intensity = static_cast<float>(index + 1);
        light.lightLinkCategory = "r" + suffix;
        light.shadowLinkCategory = "s" + suffix;
        state.ReplaceLight(light);
        allCategories.push_back("r" + suffix);
        allCategories.push_back("s" + suffix);
    }
    for (uint32_t index = 0; index < 3; ++index)
    {
        HdSilkLightRecord dome;
        dome.path = "/Domes/D" + std::to_string(index);
        dome.ambientOnly = true;
        dome.intensity = static_cast<float>(index + 1);
        dome.lightLinkCategory = "d" + std::to_string(index);
        state.ReplaceLight(dome);
        allCategories.push_back(dome.lightLinkCategory);
    }
    std::vector<std::string> base = ReceiverBShadowS;
    base.insert(base.end(), {"d0", "d2"});
    std::vector<std::string> alternate = ReceiverSShadowB;
    alternate.push_back("d1");
    std::vector<std::string> redundant = base;
    redundant.insert(redundant.end(), {"irrelevant", "irrelevant"});
    std::vector<HdSilkCategoryMembership> memberships{
        {"/Geom/Restricted", 15, allCategories},
        {"/Geom/Restricted", 2, redundant},
        {"/Geom/Restricted", 7, alternate},
        {"/Geom/Restricted", -1, base},
        {"/Geom/Swapped", -1, alternate}};
    for (const WideBitCase& bit : WideBits)
    {
        HdSilkCategoryMembership receiver;
        receiver.path = "/Geom/R" + std::string(bit.suffix);
        receiver.categories = {"r" + std::string(bit.suffix)};
        memberships.push_back(receiver);
        HdSilkCategoryMembership caster;
        caster.path = "/Geom/S" + std::string(bit.suffix);
        caster.categories = {"s" + std::string(bit.suffix)};
        memberships.push_back(caster);
    }
    state.SetCategoryMemberships(std::move(memberships), false);
    Require(state.HasLightLinks(), "wide categories must activate retained linking");
    uint64_t revision = 0;
    uint32_t count = 0;
    std::vector<uint8_t> bytes = state.BuildPage(&revision, &count);
    const RetainedPage page = ReadRetainedPage(bytes, count);
    RequireOrder(page, {1u, 8u}, "wide sparse links");
    Require(revision == 1u && page.frame.directLightCount == 128u && page.frame.domeCount == 3u &&
            page.links.directCount == 128u && page.links.domeCount == 3u &&
            page.links.reasons == 0u && page.links.rows.size() == 18,
        "wide sparse links: wrong counts, flags or canonicalized row count");
    RequireMasks(LookupPublished(page.links, "/Geom/Restricted", -1, FullWords, 7u),
        BoundaryWords, OtherWords, 5u, "restricted path B/S/dome5");
    RequireMasks(LookupPublished(page.links, "/Geom/Swapped", 0, FullWords, 7u),
        OtherWords, BoundaryWords, 2u, "swapped path S/B/dome2");
    RequireMasks(LookupPublished(page.links, "/Geom/Restricted", 7, FullWords, 7u),
        OtherWords, BoundaryWords, 2u, "different exact instance");
    RequireMasks(LookupPublished(page.links, "/Geom/Restricted", 15, FullWords, 7u),
        FullWords, FullWords, 7u, "explicit full-128 opt-in");
    for (int32_t inherited : {2, 99})
    {
        RequireMasks(LookupPublished(page.links, "/Geom/Restricted", inherited, FullWords, 7u),
            BoundaryWords, OtherWords, 5u, "omitted exact instance inherits its path, not global full");
    }
    RequireMasks(LookupPublished(page.links, "/Geom/Omitted", 7, FullWords, 7u),
        FullWords, FullWords, 7u, "omitted path is full current direct/dome");
    std::vector<int32_t> restrictedIdentities;
    for (const LinkRow& row : page.links.rows)
    {
        if (row.path == "/Geom/Restricted")
        {
            restrictedIdentities.push_back(row.instance);
        }
    }
    Require(restrictedIdentities == std::vector<int32_t>{-1, 7, 15},
        "path-relative canonicalization lost full opt-in, kept redundant instance 2, or sorted -1 wrongly");
    for (const WideBitCase& bit : WideBits)
    {
        const LinkMasks receiver = LookupPublished(page.links, "/Geom/R" + std::string(bit.suffix), 0);
        const LinkMasks caster = LookupPublished(page.links, "/Geom/S" + std::string(bit.suffix), 0);
        RequireMasks(receiver, bit.words, ZeroWords, 0u, std::string("receiver-only ") + bit.suffix);
        RequireMasks(caster, ZeroWords, bit.words, 0u, std::string("caster-only ") + bit.suffix);
        Require(receiver.light != bit.wrongWord && caster.shadow != bit.wrongWord,
            std::string("literal wrong-word control aliased ") + bit.suffix);
        for (size_t candidate = 0; candidate < 128; ++candidate)
        {
            const uint32_t flag = uint32_t{1} << (candidate % 32);
            Require(((receiver.light[candidate / 32] & flag) != 0u) == (candidate == bit.bit) &&
                    ((caster.shadow[candidate / 32] & flag) != 0u) == (candidate == bit.bit),
                std::string("singleton wrong-position/word control ") + bit.suffix);
        }
    }
    for (size_t index = 0; index < 128; ++index)
    {
        Require(page.frame.directLights[index].intensity == static_cast<float>(index + 1) &&
                page.frame.directLights[index].type == 1u &&
                page.frame.directLights[index].shadowEnabled == 0u,
            "wide link indices did not refer to the sorted FRAME marker identities");
    }

    // The same on-wire path/exact/global fallback rules at every First-count
    // boundary. Three domes keep the count-zero table meaningful too.
    for (const MaskCase& boundary : MaskCounts)
    {
        HdSilkSceneState bounded;
        std::vector<std::string> full;
        for (uint32_t index = 0; index < boundary.input; ++index)
        {
            const std::string digits = std::to_string(index);
            const std::string suffix = std::string(3 - digits.size(), '0') + digits;
            HdSilkLightRecord light;
            light.path = "/Lights/L" + suffix;
            light.type = OPENUSD_SILK_LIGHT_DISTANT;
            light.intensity = static_cast<float>(index + 1);
            light.lightLinkCategory = "r" + suffix;
            light.shadowLinkCategory = "s" + suffix;
            bounded.ReplaceLight(light);
            full.push_back("r" + suffix);
            full.push_back("s" + suffix);
        }
        for (uint32_t index = 0; index < 3; ++index)
        {
            HdSilkLightRecord dome;
            dome.path = "/Domes/D" + std::to_string(index);
            dome.ambientOnly = true;
            dome.lightLinkCategory = "d" + std::to_string(index);
            bounded.ReplaceLight(dome);
            full.push_back(dome.lightLinkCategory);
        }
        bounded.SetCategoryMemberships({
            HdSilkCategoryMembership{"/Geom/Boundary", -1, {}},
            HdSilkCategoryMembership{"/Geom/Boundary", 7, full}}, false);
        bytes = bounded.BuildPage(&revision, &count);
        const RetainedPage decoded = ReadRetainedPage(bytes, count);
        const std::string label = "published fallback count " + std::to_string(boundary.input);
        RequireOrder(decoded, {1u, 8u}, label);
        Require(revision == 1u && decoded.frame.directLightCount == boundary.input &&
                decoded.links.directCount == boundary.input && decoded.links.domeCount == 3u &&
                decoded.links.reasons == 0u && decoded.links.rows.size() == 2 &&
                decoded.links.rows[0].instance == -1 && decoded.links.rows[1].instance == 7,
            label + ": exact full mask was incorrectly canonicalized against global defaults");
        RequireMasks(decoded.links.rows[0].masks, ZeroWords, ZeroWords, 0u, label + " path");
        RequireMasks(decoded.links.rows[1].masks, boundary.words, boundary.words, 7u, label + " exact");
        RequireMasks(LookupPublished(decoded.links, "/Geom/Boundary", 8, boundary.words, 7u),
            ZeroWords, ZeroWords, 0u, label + " path inheritance");
        RequireMasks(LookupPublished(decoded.links, "/Geom/Absent", 7, boundary.words, 7u),
            boundary.words, boundary.words, 7u, label + " global inheritance");
        RequireUnusedDirectSlots(decoded.frame, boundary.input, label);
    }
}

void VerifySortedLightRemapping()
{
    for (uint32_t removedIdentity : {0u, 31u})
    {
        HdSilkSceneState state;
        std::vector<HdSilkLightRecord> lights;
        for (uint32_t index = 0; index < 128; ++index)
        {
            const std::string digits = std::to_string(index);
            const std::string suffix = std::string(3 - digits.size(), '0') + digits;
            HdSilkLightRecord light;
            light.path = "/Lights/L" + suffix;
            light.type = OPENUSD_SILK_LIGHT_DISTANT;
            light.intensity = static_cast<float>(index + 1);
            light.color[0] = 0.25f;
            light.color[1] = 0.5f;
            light.color[2] = 0.75f;
            light.exposure = 1.0f;
            light.diffuse = 0.25f;
            light.specular = 0.75f;
            light.radius = 0.375f;
            light.lightLinkCategory = "r" + suffix;
            light.shadowLinkCategory = "s" + suffix;
            light.shadowEnabled = index == 31 || index == 64 || index == 96 || index == 127 ? 1u : 0u;
            if (index == 127)
            {
                std::copy(TowardXTransform.begin(), TowardXTransform.end(), light.transform);
            }
            state.ReplaceLight(light);
            lights.push_back(light);
        }
        HdSilkMeshRecord mesh;
        mesh.path = "/Geom/Remap";
        mesh.primId = 61;
        mesh.topologyRevision = 1;
        mesh.points = CasterPoints3;
        mesh.indices = {0u, 1u, 2u};
        mesh.triangleSubprims = {0u};
        state.ReplaceMeshInstances(mesh.path, {mesh});
        state.SetCategoryMemberships({HdSilkCategoryMembership{mesh.path, -1, ReceiverBShadowS}}, false);
        uint64_t revision = 0;
        uint32_t count = 0;
        std::vector<uint8_t> bytes = state.BuildPage(&revision, &count);
        const RetainedPage baseline = ReadRetainedPage(bytes, count);
        RequireOrder(baseline, {1u, 8u, 9u, 2u}, "remap baseline");
        RequireMasks(baseline.links.rows.at(0).masks, BoundaryWords, OtherWords, 0u, "remap baseline masks");
        RequireShadowHeader(baseline, 4, 128u, 0u, "remap baseline");
        const std::array<uint32_t, 4> originalIndices{31u, 64u, 96u, 127u};
        for (size_t map = 0; map < 4; ++map)
        {
            RequireShadow(baseline.shadows.maps[map], originalIndices[map], static_cast<uint32_t>(map),
                3u, map == 3 ? X3 : Z3, "remap baseline descriptor");
        }
        lights[42].intensity = 432.0f;
        state.ReplaceLight(lights[42]);
        bytes = state.BuildPage(&revision, &count);
        const RetainedPage valueEdit = ReadRetainedPage(bytes, count);
        RequireOrder(valueEdit, {1u}, "identity-preserving value replacement");
        Require(revision == 2u && valueEdit.frame.directLightCount == 128u &&
                valueEdit.frame.directLights[42].intensity == 432.0f &&
                valueEdit.frame.directLights[41].intensity == 42.0f &&
                valueEdit.frame.directLights[43].intensity == 44.0f,
            "replacing one sorted identity duplicated it or changed a neighbor");

        lights[32].lightLinkCategory = "unmatched-receiver";
        state.ReplaceLight(lights[32]);
        bytes = state.BuildPage(&revision, &count);
        const RetainedPage receiverEdit = ReadRetainedPage(bytes, count);
        RequireOrder(receiverEdit, {1u, 8u}, "receiver-only category edit");
        RequireMasks(receiverEdit.links.rows.at(0).masks, BoundaryWithout32, OtherWords, 0u,
            "receiver-only edit must not alter caster words");
        Require(receiverEdit.frameBytes == valueEdit.frameBytes && revision == 3u,
            "category-only receiver edit altered FRAME or revision");
        lights[33].shadowLinkCategory = "unmatched-caster";
        state.ReplaceLight(lights[33]);
        bytes = state.BuildPage(&revision, &count);
        const RetainedPage casterEdit = ReadRetainedPage(bytes, count);
        RequireOrder(casterEdit, {1u, 8u}, "caster-only category edit");
        RequireMasks(casterEdit.links.rows.at(0).masks, BoundaryWithout32,
            Words{1u, 0x40000000u, 0x40000002u, 0x40000002u}, 0u,
            "caster-only edit must not alter receiver words");
        Require(casterEdit.frameBytes == valueEdit.frameBytes && revision == 4u,
            "category-only caster edit altered FRAME or revision");
        lights[32].lightLinkCategory = "r032";
        lights[33].shadowLinkCategory = "s033";
        state.ReplaceLight(lights[32]);
        state.ReplaceLight(lights[33]);
        bytes = state.BuildPage(&revision, &count);
        const RetainedPage beforeRemove = ReadRetainedPage(bytes, count);
        RequireOrder(beforeRemove, {1u, 8u}, "category restoration");
        Require(beforeRemove.linkBytes == baseline.linkBytes && beforeRemove.frameBytes == valueEdit.frameBytes &&
                revision == 5u,
            "category restoration did not restore the original wide table");

        state.RemoveLight(lights[removedIdentity].path);
        bytes = state.BuildPage(&revision, &count);
        const RetainedPage removed = ReadRetainedPage(bytes, count);
        const std::string label = "remove L" + std::to_string(removedIdentity);
        RequireOrder(removed, {1u, 8u, 9u}, label);
        Require(revision == 6u && removed.frame.directLightCount == 127u &&
                removed.links.directCount == 127u && removed.links.rows.size() == 1 &&
                removed.links.reasons == 0u,
            label + ": wrong count or table identity after removal");
        const Words expectedB = removedIdentity == 0
            ? Words{0xc0000000u, 0xc0000000u, 0xc0000000u, 0x40000000u}
            : Words{0x80000000u, 0xc0000000u, 0xc0000000u, 0x40000000u};
        const Words expectedS = removedIdentity == 0
            ? Words{0u, 0x20000001u, 0x20000001u, 0x20000001u}
            : Words{1u, 0x20000001u, 0x20000001u, 0x20000001u};
        RequireMasks(removed.links.rows[0].masks, expectedB, expectedS, 0u, label);
        for (uint32_t slot = 0; slot < 127; ++slot)
        {
            const uint32_t identity = slot < removedIdentity ? slot : slot + 1;
            Require(removed.frame.directLights[slot].intensity ==
                    (identity == 42 ? 432.0f : static_cast<float>(identity + 1)) &&
                    removed.frame.directLights[slot].color == std::array<float, 3>{0.25f, 0.5f, 0.75f} &&
                    removed.frame.directLights[slot].exposure == 1.0f &&
                    removed.frame.directLights[slot].diffuse == 0.25f &&
                    removed.frame.directLights[slot].specular == 0.75f,
                label + ": sorted FRAME identity/control shifted incorrectly at " + std::to_string(slot));
        }
        RequireUnusedDirectSlots(removed.frame, 127, label);
        const std::vector<uint32_t> expectedIndices = removedIdentity == 0
            ? std::vector<uint32_t>{30u, 63u, 95u, 126u}
            : std::vector<uint32_t>{63u, 95u, 126u};
        RequireShadowHeader(removed, expectedIndices.size(), 127u, 0u, label);
        for (size_t map = 0; map < expectedIndices.size(); ++map)
        {
            RequireShadow(removed.shadows.maps[map], expectedIndices[map], static_cast<uint32_t>(map),
                3u, expectedIndices[map] == 126 ? X3 : Z3, label + " remapped descriptor");
        }
        state.ReplaceLight(lights[removedIdentity]);
        bytes = state.BuildPage(&revision, &count);
        const RetainedPage reinserted = ReadRetainedPage(bytes, count);
        RequireOrder(reinserted, {1u, 8u, 9u}, "light reinsert");
        Require(revision == 7u && reinserted.frame.directLightCount == 128u &&
                reinserted.frameBytes == beforeRemove.frameBytes &&
                reinserted.linkBytes == baseline.linkBytes && reinserted.shadowBytes == baseline.shadowBytes,
            "same-identity light reinsert did not restore all sorted masks/descriptors");

        HdSilkSceneState reverse;
        for (size_t remaining = lights.size(); remaining > 0; --remaining)
        {
            reverse.ReplaceLight(lights[remaining - 1]);
        }
        reverse.ReplaceMeshInstances(mesh.path, {mesh});
        reverse.SetCategoryMemberships({HdSilkCategoryMembership{mesh.path, -1, ReceiverBShadowS}}, false);
        uint64_t reverseRevision = 0;
        bytes = reverse.BuildPage(&reverseRevision, &count);
        const RetainedPage reversed = ReadRetainedPage(bytes, count);
        RequireOrder(reversed, {1u, 8u, 9u, 2u}, "reverse insertion");
        Require(reverseRevision == 1u && revision == 7u &&
                reversed.frameBytes == reinserted.frameBytes &&
                reversed.linkBytes == reinserted.linkBytes &&
                reversed.shadowBytes == reinserted.shadowBytes,
            "insertion history, rather than sorted identities, controls FRAME/link/shadow contents");
    }

    HdSilkSceneState utf8;
    const std::array<std::string, 4> paths{
        "/Lights/A", "/Lights/Z", "/Lights/a", "/Lights/\xc3\xa9"};
    for (size_t remaining = paths.size(); remaining > 0; --remaining)
    {
        const size_t index = remaining - 1;
        HdSilkLightRecord light;
        light.path = paths[index];
        light.type = OPENUSD_SILK_LIGHT_SPHERE;
        light.intensity = static_cast<float>(11 * (index + 1));
        light.radius = 0.375f;
        utf8.ReplaceLight(light);
    }
    uint64_t revision = 0;
    uint32_t count = 0;
    const std::vector<uint8_t> bytes = utf8.BuildPage(&revision, &count);
    const RetainedPage ordered = ReadRetainedPage(bytes, count);
    RequireOrder(ordered, {1u}, "unsigned UTF-8 light order");
    Require(revision == 1u && ordered.frame.directLightCount == 4u,
        "UTF-8 ordering changed cardinality");
    const std::array<float, 4> intensities{11.0f, 22.0f, 33.0f, 44.0f};
    for (size_t index = 0; index < paths.size(); ++index)
    {
        Require(ordered.frame.directLights[index].intensity == intensities[index] &&
                ordered.frame.directLights[index].type == 2u && ordered.frame.directLights[index].radius == 0.375f,
            "expected unsigned order A, Z, a, C3 A9 at slot " + std::to_string(index));
    }
    RequireUnusedDirectSlots(ordered.frame, 4, "UTF-8 unused slots");
}

void VerifyNestedCompositeIdentityLinks()
{
    // Precisely supplied composition -> retained memberships -> wire evidence.
    // This does NOT claim Hydra supplies non-root per-instance categories.
    HdSilkSceneState state;
    std::vector<std::string> allCategories;
    for (uint32_t index = 0; index < 128; ++index)
    {
        const std::string digits = std::to_string(index);
        const std::string suffix = std::string(3 - digits.size(), '0') + digits;
        HdSilkLightRecord light;
        light.path = "/Lights/L" + suffix;
        light.type = OPENUSD_SILK_LIGHT_DISTANT;
        light.intensity = static_cast<float>(index + 1);
        light.lightLinkCategory = "r" + suffix;
        light.shadowLinkCategory = "s" + suffix;
        state.ReplaceLight(light);
        allCategories.push_back("r" + suffix);
        allCategories.push_back("s" + suffix);
    }
    struct CompositeCase
    {
        int32_t identity;
        int32_t outer;
        int32_t inner;
        Words receiver;
        Words caster;
    };
    const CompositeCase composites[] = {
        {5, 1, 1, {0x80000000u, 0x80000000u, 0u, 1u}, {0u, 1u, 1u, 0x80000000u}},
        {7, 1, 3, {0x80000000u, 0x80000000u, 0u, 0x80000000u}, {0x80000000u, 1u, 1u, 0u}},
        {13, 3, 1, {0x80000000u, 0u, 0x80000000u, 1u}, {0u, 1u, 0u, 0x80000001u}},
        {15, 3, 3, {0x80000000u, 0u, 0x80000000u, 0x80000000u}, {0x80000000u, 1u, 0u, 1u}}};
    std::vector<HdSilkMeshRecord> instances;
    for (const CompositeCase& row : composites)
    {
        HdSilkMeshRecord instance;
        instance.path = "/Geom/Nested";
        instance.primId = 71;
        instance.instanceId = 100 + row.identity;
        instance.instanceIndex = row.identity;
        instance.topologyRevision = 1;
        instance.instancerPath = "/Outer/Inner";
        instance.instancerContext = {
            HdSilkInstancerContextEntry{"/Outer", row.outer},
            HdSilkInstancerContextEntry{"/Outer/Inner", row.inner}};
        instance.transform[12] = static_cast<double>(row.identity);
        if (row.identity == 5)
        {
            instance.points = CasterPoints3;
            instance.indices = {0u, 1u, 2u};
            instance.triangleSubprims = {0u};
        }
        instances.push_back(instance);
    }
    state.ReplaceMeshInstances("/Geom/Nested", instances);
    HdSilkInstancerLevel outer;
    outer.path = "/Outer";
    outer.instanceCount = 4;
    outer.publishedIndices = {1, 3};
    outer.instanceCategories.resize(4);
    outer.instanceCategories[1] = {"r063", "s064"};
    outer.instanceCategories[3] = {"r095", "s096"};
    HdSilkInstancerLevel inner;
    inner.path = "/Outer/Inner";
    inner.instanceCount = 4;
    inner.publishedIndices = {1, 3};
    inner.instanceCategories.resize(4);
    inner.instanceCategories[1] = {"r096", "s127"};
    inner.instanceCategories[3] = {"r127", "s031"};
    const std::vector<std::string> prototypeCategories{"r000"};
    const std::vector<std::string> sharedCategories{"r031", "s032"};
    std::vector<HdSilkCategoryMembership> memberships;
    HdSilkNestedLinkDiagnostics diagnostics;
    Require(HdSilkAppendPathMemberships("/Geom/Nested", prototypeCategories, sharedCategories,
                {outer, inner}, HdSilkMaxCollectedInstanceRows, &memberships, &diagnostics) &&
            !diagnostics.Any() && memberships.size() == 5,
        "nested precise input must produce fallback plus four resolvable composite identities");
    state.SetCategoryMemberships(std::move(memberships), false);
    uint64_t revision = 0;
    uint32_t count = 0;
    std::vector<uint8_t> bytes = state.BuildPage(&revision, &count);
    const RetainedPage baseline = ReadRetainedPage(bytes, count);
    RequireOrder(baseline, {1u, 8u, 2u, 2u, 2u, 2u}, "nested baseline");
    Require(revision == 1u && baseline.frame.directLightCount == 128u &&
            baseline.links.directCount == 128u && baseline.links.reasons == 0u &&
            baseline.links.rows.size() == 5 && baseline.meshes.size() == 4,
        "nested baseline: wrong concrete meshes or link table");
    RequireMasks(baseline.links.rows[0].masks, Words{0x80000001u, 0u, 0u, 0u},
        Words{0u, 1u, 0u, 0u}, 0u, "prototype fallback includes r000 and shared categories");
    Require(baseline.links.rows[0].instance == -1, "nested fallback must precede exact identities");
    for (size_t index = 0; index < 4; ++index)
    {
        const CompositeCase& expected = composites[index];
        const MeshView& mesh = baseline.meshes[index];
        const LinkRow& link = baseline.links.rows[index + 1];
        Require(mesh.path == "/Geom/Nested" && mesh.primId == 71 &&
                mesh.instance == expected.identity && mesh.instanceId == 100 + expected.identity &&
                mesh.instancerPath == "/Outer/Inner" &&
                mesh.context == std::vector<std::pair<std::string, int32_t>>{
                    {"/Outer", expected.outer}, {"/Outer/Inner", expected.inner}} &&
                mesh.topology == 1u && mesh.topologyRevision == 1u &&
                mesh.hash == baseline.meshes[0].hash && mesh.materialHash == 0u && mesh.materialPath.empty(),
            "nested mesh context/identity " + std::to_string(expected.identity));
        std::array<double, 16> transform = IdentityTransform;
        transform[12] = static_cast<double>(expected.identity);
        Require(mesh.transform == transform &&
                mesh.points == (index == 0 ? CasterPoints3 : std::vector<float>{}) &&
                mesh.indices == (index == 0 ? std::vector<uint32_t>{0u, 1u, 2u} : std::vector<uint32_t>{}) &&
                mesh.subprims == (index == 0 ? std::vector<uint32_t>{0u} : std::vector<uint32_t>{}),
            "nested first payload must be 5; later geometry-eliding references must retain their own transforms");
        Require(link.path == mesh.path && link.instance == mesh.instance &&
                link.instance != 1 && link.instance != 3,
            "nested override names an uncomposed leaf index or an unpublished geometry identity");
        RequireMasks(link.masks, expected.receiver, expected.caster, 0u,
            "nested composite " + std::to_string(expected.identity));
        Require((link.masks.light[0] & 1u) == 0u,
            "reported leaf categories must replace, not union, prototype-only r000");
    }
    RequireMasks(LookupPublished(baseline.links, "/Geom/Nested", 99),
        Words{0x80000001u, 0u, 0u, 0u}, Words{0u, 1u, 0u, 0u}, 0u, "nested path inheritance");

    outer.instanceCategories[3] = {"r032", "s095"};
    memberships.clear();
    Require(HdSilkAppendPathMemberships("/Geom/Nested", prototypeCategories, sharedCategories,
                {outer, inner}, HdSilkMaxCollectedInstanceRows, &memberships, &diagnostics) &&
            !diagnostics.Any(), "nested ancestor edit must remain precise");
    state.SetCategoryMemberships(std::move(memberships), false);
    bytes = state.BuildPage(&revision, &count);
    const RetainedPage ancestorEdit = ReadRetainedPage(bytes, count);
    RequireOrder(ancestorEdit, {1u, 8u}, "nested ancestor category edit");
    Require(revision == 2u && ancestorEdit.links.rows.size() == 5 &&
            ancestorEdit.frameBytes == baseline.frameBytes,
        "nested category edit changed geometry/frame or lost an identity");
    for (size_t index = 0; index < 2; ++index)
    {
        RequireMasks(LookupPublished(ancestorEdit.links, "/Geom/Nested", composites[index].identity),
            composites[index].receiver, composites[index].caster, 0u, "unaffected outer-1 subtree");
    }
    RequireMasks(LookupPublished(ancestorEdit.links, "/Geom/Nested", 13),
        Words{0x80000000u, 1u, 0u, 1u}, Words{0u, 1u, 0x80000000u, 0x80000000u}, 0u,
        "outer-3 edit reaches composite 13");
    RequireMasks(LookupPublished(ancestorEdit.links, "/Geom/Nested", 15),
        Words{0x80000000u, 1u, 0u, 0x80000000u}, Words{0x80000000u, 1u, 0x80000000u, 0u}, 0u,
        "outer-3 edit reaches composite 15, not raw leaf 3");

    inner.instanceCategories[3] = allCategories;
    memberships.clear();
    Require(HdSilkAppendPathMemberships("/Geom/Nested", prototypeCategories, sharedCategories,
                {outer, inner}, HdSilkMaxCollectedInstanceRows, &memberships, &diagnostics) &&
            !diagnostics.Any(), "nested full opt-in collection");
    state.SetCategoryMemberships(std::move(memberships), false);
    bytes = state.BuildPage(&revision, &count);
    const RetainedPage optIn = ReadRetainedPage(bytes, count);
    RequireOrder(optIn, {1u, 8u}, "nested full opt-in");
    Require(revision == 3u && optIn.links.rows.size() == 5 &&
            optIn.links.rows[2].instance == 7 && optIn.links.rows[4].instance == 15,
        "full masks under a restricted nested path must remain explicit for both composed identities");
    RequireMasks(optIn.links.rows[2].masks, FullWords, FullWords, 0u, "nested explicit full identity 7");
    RequireMasks(optIn.links.rows[4].masks, FullWords, FullWords, 0u, "nested explicit full identity 15");
    RequireMasks(optIn.links.rows[1].masks, composites[0].receiver, composites[0].caster, 0u,
        "inner-1 identity 5 unaffected by inner-3 opt-in");
    RequireMasks(optIn.links.rows[3].masks, Words{0x80000000u, 1u, 0u, 1u},
        Words{0u, 1u, 0x80000000u, 0x80000000u}, 0u, "inner-1 identity 13 unaffected by opt-in");

    outer.publishedIndices = {1};
    instances.resize(2);
    state.ReplaceMeshInstances("/Geom/Nested", instances);
    memberships.clear();
    Require(HdSilkAppendPathMemberships("/Geom/Nested", prototypeCategories, sharedCategories,
                {outer, inner}, HdSilkMaxCollectedInstanceRows, &memberships, &diagnostics) &&
            !diagnostics.Any(), "nested ancestor retirement collection");
    state.SetCategoryMemberships(std::move(memberships), false);
    bytes = state.BuildPage(&revision, &count);
    const RetainedPage retired = ReadRetainedPage(bytes, count);
    RequireOrder(retired, {1u, 8u, 2u, 2u, 3u, 3u}, "nested ancestor retirement");
    Require(revision == 4u && retired.links.rows.size() == 3 &&
            retired.links.rows[0].instance == -1 && retired.links.rows[1].instance == 5 &&
            retired.links.rows[2].instance == 7 &&
            retired.meshes[0].instance == 5 && retired.meshes[1].instance == 7,
        "nested ancestor removal did not retire both 13 and 15 overrides and geometry");
    for (size_t index = 0; index < 2; ++index)
    {
        const RemovalView& removal = retired.meshRemovals[index];
        Require(removal.path == "/Geom/Nested" && removal.instance == (index == 0 ? 13 : 15) &&
                removal.hash == baseline.meshes[0].hash,
            "nested ancestor retirement removed a wrong composite identity");
    }
    RequireMasks(retired.links.rows[1].masks, composites[0].receiver, composites[0].caster, 0u,
        "nested surviving restricted 5");
    RequireMasks(retired.links.rows[2].masks, FullWords, FullWords, 0u, "nested surviving full 7");
    bytes = state.BuildPage(&revision, &count);
    const RetainedPage quiet = ReadRetainedPage(bytes, count);
    RequireOrder(quiet, {1u}, "nested retirement settles");
    Require(revision == 5u && quiet.frameBytes == retired.frameBytes,
        "nested ancestor retirement repeated or changed the next FRAME");
}

void VerifyNestedFallbackAndCollectionLimits()
{
    struct NestedCase
    {
        const char* name;
        bool nestedOnly;
        std::vector<HdSilkInstancerLevel> levels;
        std::vector<int32_t> precise;
        bool emptyLeaf;
        size_t uncomposable;
        size_t unresolved;
        size_t unrepresentable;
    };
    const std::vector<NestedCase> cases{
        {"path-only", false, {}, {}, false, 0, 0, 0},
        {"nested-empty-levels", true, {}, {}, false, 0, 0, 0},
        {"singleton", false,
            {{"/Leaf", 1, {0}, {{"r096", "s127"}}}}, {0}, false, 0, 0, 0},
        {"normalization-and-negative-index", true,
            {{"/Leaf", 1, {0, -1, 0}, {{"s127", "r096", "r096"}}}}, {0}, false, 0, 0, 0},
        {"absent-arrays-inherit", false,
            {{"/Leaf", 4, {3, 1}, {}}}, {}, false, 0, 0, 0},
        {"present-empty-leaves-replace", false,
            {{"/Leaf", 4, {3, 1}, {{}, {}, {}, {}}}}, {1, 3}, true, 0, 0, 0},
        {"short-array-unknown-is-not-empty", true,
            {{"/Leaf", 2, {0, 1}, {{"r096", "s127"}}}}, {0}, false, 0, 1, 0},
        {"only-negative-is-absent", false,
            {{"/Leaf", 1, {-1}, {{"r096", "s127"}}}}, {}, false, 0, 0, 0},
        {"inner-radix-3-valid-4-invalid", false,
            {{"/Outer", 2, {1}, {}}, {"/Leaf", 4, {3, 4}, {{}, {}, {}, {"r096", "s127"}}}},
            {7}, false, 1, 0, 0},
        {"nonpositive-inner-radix", true,
            {{"/Outer", 2, {1}, {}}, {"/Leaf", 0, {0}, {{"r096", "s127"}}}},
            {}, false, 1, 0, 0},
        {"unknown-ancestor-prunes-subtree", false,
            {{"/Outer", 2, {1}, {{}}}, {"/Leaf", 4, {1, 3}, {{}, {"r096"}, {}, {"s127"}}}},
            {}, false, 0, 1, 0},
        {"signed-int32-last-identity", false,
            {{"/Outer", 536870912, {536870911}, {}},
             {"/Leaf", 4, {3}, {{}, {}, {}, {"r096", "s127"}}}},
            {2147483647}, false, 0, 0, 0},
        {"signed-int32-overflow-pruned", true,
            {{"/Outer", 536870913, {536870912}, {}}, {"/Leaf", 4, {0}, {{"r096", "s127"}}}},
            {}, false, 0, 0, 1},
        // With no interesting categories the implementation skips the product
        // entirely. It does not diagnose an overflow it never composes. The
        // preceding two rows use a tiny leaf array, not a huge ancestor array.
        {"uninteresting-huge-product-inherits", false,
            {{"/Outer", 536870913, {536870912}, {}}, {"/Leaf", 4, {0}, {}}},
            {}, false, 0, 0, 0}};
    const Words pathLight{0x80000000u, 0x80000000u, 0u, 0u};
    const Words pathShadow{0u, 1u, 1u, 0u};
    for (const NestedCase& row : cases)
    {
        HdSilkSceneState state;
        for (uint32_t index = 0; index < 128; ++index)
        {
            const std::string digits = std::to_string(index);
            const std::string suffix = std::string(3 - digits.size(), '0') + digits;
            HdSilkLightRecord light;
            light.path = "/Lights/L" + suffix;
            light.type = OPENUSD_SILK_LIGHT_DISTANT;
            light.intensity = static_cast<float>(index + 1);
            light.lightLinkCategory = "r" + suffix;
            light.shadowLinkCategory = "s" + suffix;
            state.ReplaceLight(light);
        }
        const std::vector<std::string> prim{"s064", "r063", "r063"};
        const std::vector<std::string> shared{"s032", "r031", "s032"};
        std::vector<HdSilkCategoryMembership> memberships;
        HdSilkNestedLinkDiagnostics diagnostics;
        bool fitted = false;
        if (row.nestedOnly)
        {
            memberships.push_back(HdSilkCategoryMembership{
                "/Geom/Fallback", -1, {"r031", "r063", "s032", "s064"}});
            fitted = HdSilkAppendNestedInstanceMemberships(
                "/Geom/Fallback", prim, shared, row.levels, 32, &memberships, &diagnostics);
        }
        else
        {
            fitted = HdSilkAppendPathMemberships(
                "/Geom/Fallback", prim, shared, row.levels, 32, &memberships, &diagnostics);
        }
        const std::string label = row.name;
        Require(fitted && memberships.size() == 1 + row.precise.size() &&
                diagnostics.uncomposableIndices == row.uncomposable &&
                diagnostics.unresolvedIndices == row.unresolved &&
                diagnostics.unrepresentableIndices == row.unrepresentable &&
                diagnostics.Any() == (row.uncomposable + row.unresolved + row.unrepresentable != 0),
            label + ": wrong raw count or exact named composition diagnostics");
        Require(memberships[0].instanceIndex == -1 &&
                memberships[0].categories == std::vector<std::string>{"r031", "r063", "s032", "s064"},
            label + ": shared/prototype categories were not normalized as a set");
        for (size_t index = 0; index < row.precise.size(); ++index)
        {
            Require(memberships[index + 1].instanceIndex == row.precise[index] &&
                    memberships[index + 1].categories == (row.emptyLeaf
                        ? std::vector<std::string>{"r031", "s032"}
                        : std::vector<std::string>{"r031", "r096", "s032", "s127"}),
                label + ": precise leaf replacement/shared union is wrong");
        }
        state.SetCategoryMemberships(std::move(memberships), false);
        uint64_t revision = 0;
        uint32_t count = 0;
        const std::vector<uint8_t> bytes = state.BuildPage(&revision, &count);
        const RetainedPage page = ReadRetainedPage(bytes, count);
        RequireOrder(page, {1u, 8u}, label);
        Require(revision == 1u && page.frame.directLightCount == 128u &&
                page.links.directCount == 128u && page.links.reasons == 0u &&
                page.links.rows.size() == 1 + row.precise.size(),
            label + ": helper rows did not reach the actual retained wire");
        RequireMasks(page.links.rows[0].masks, pathLight, pathShadow, 0u, label + " fallback");
        for (size_t index = 0; index < row.precise.size(); ++index)
        {
            Require(page.links.rows[index + 1].instance == row.precise[index] &&
                    page.links.rows[index + 1].path == "/Geom/Fallback",
                label + ": spurious, wrapped or wrongly sorted wire identity");
            RequireMasks(page.links.rows[index + 1].masks,
                row.emptyLeaf ? Words{0x80000000u, 0u, 0u, 0u} : Words{0x80000000u, 0u, 0u, 1u},
                row.emptyLeaf ? Words{0u, 1u, 0u, 0u} : Words{0u, 1u, 0u, 0x80000000u},
                0u, label + " precise wire masks");
        }
        // 99 is never supplied, and short-array identity 1 is specifically
        // unknown. Both inherit the path, not an invented empty exact row.
        RequireMasks(LookupPublished(page.links, "/Geom/Fallback", 99), pathLight, pathShadow, 0u,
            label + " unresolved path inheritance");
        if (row.unresolved != 0)
        {
            RequireMasks(LookupPublished(page.links, "/Geom/Fallback", 1), pathLight, pathShadow, 0u,
                label + " short-array unknown identity");
        }
    }

    static_assert(HdSilkMaxCollectedInstanceRows == 65536);
    struct CollectionCase
    {
        size_t overrides;
        size_t limit;
        bool fits;
    };
    const CollectionCase limits[] = {
        {0, 0, true}, {2, 2, true}, {3, 2, false},
        {65534, 65536, true}, {65535, 65536, true},
        {65536, 65536, true}, {65537, 65536, false}};
    for (const CollectionCase& row : limits)
    {
        HdSilkSceneState state;
        for (uint32_t index = 0; index < 128; ++index)
        {
            const std::string digits = std::to_string(index);
            HdSilkLightRecord light;
            light.path = "/Lights/L" + std::string(3 - digits.size(), '0') + digits;
            light.type = OPENUSD_SILK_LIGHT_DISTANT;
            light.intensity = static_cast<float>(index + 1);
            light.lightLinkCategory = index == 31 ? "r031" : "";
            state.ReplaceLight(light);
        }
        HdSilkInstancerLevel leaf;
        leaf.path = "/Leaf";
        leaf.instanceCount = static_cast<int64_t>(row.overrides);
        leaf.instanceCategories.resize(row.overrides, std::vector<std::string>{"irrelevant"});
        for (size_t index = 0; index < row.overrides; ++index)
        {
            leaf.publishedIndices.push_back(static_cast<int>(index));
        }
        std::vector<HdSilkCategoryMembership> memberships{
            HdSilkCategoryMembership{"/A/Earlier", -1, {}}};
        HdSilkNestedLinkDiagnostics diagnostics;
        const bool fitted = HdSilkAppendPathMemberships(
            "/Geom/Collected", {}, {}, {leaf}, row.limit, &memberships, &diagnostics);
        const std::string label = "raw total " + std::to_string(row.overrides + 1) +
            " at override limit " + std::to_string(row.limit);
        // Source-confirmed deviation from a total-row interpretation: the
        // wrapper grants rowLimit ADDITIONAL overrides after its path row.
        // Thus total candidates 65535/65536/65537 fit; 65538 rolls back.
        Require(fitted == row.fits && !diagnostics.Any() &&
                memberships.size() == (row.fits ? row.overrides + 2 : 1) &&
                memberships[0].path == "/A/Earlier" && memberships[0].instanceIndex == -1 &&
                memberships[0].categories.empty(),
            label + ": raw limit/whole-path rollback damaged an earlier contribution");
        if (row.fits)
        {
            Require(memberships[1].instanceIndex == -1 && memberships[1].categories.empty(),
                label + ": path fallback must be counted separately from overrides");
            if (row.overrides != 0)
            {
                Require(memberships.back().instanceIndex == static_cast<int32_t>(row.overrides - 1) &&
                        memberships.back().categories == std::vector<std::string>{"irrelevant"},
                    label + ": exact-limit collection dropped its last raw identity");
            }
        }
        state.SetCategoryMemberships(std::move(memberships), !fitted);
        uint64_t revision = 0;
        uint32_t count = 0;
        const std::vector<uint8_t> bytes = state.BuildPage(&revision, &count);
        const RetainedPage page = ReadRetainedPage(bytes, count);
        RequireOrder(page, {1u, 8u}, label);
        Require(revision == 1u && page.links.rows.size() == (row.fits ? 2u : 1u) &&
                page.links.directCount == 128u && page.links.reasons == (row.fits ? 0u : 1u),
            label + ": raw category differences were charged as published entries, or truncation was unnamed");
        const Words without31{0x7fffffffu, 0xffffffffu, 0xffffffffu, 0xffffffffu};
        RequireMasks(LookupPublished(page.links, "/A/Earlier", 0), without31, FullWords, 0u,
            label + " preserved earlier wire row");
        RequireMasks(LookupPublished(page.links, "/Geom/Collected", 0),
            row.fits ? without31 : FullWords, FullWords, 0u,
            label + " whole-path admission versus fail-open rollback");
    }

    // Both public append APIs accept an omitted output/diagnostic sink. Keep
    // the observable earlier row and then publish it, rather than testing only
    // the helper's return code.
    std::vector<HdSilkCategoryMembership> preserved{{"/Geom/Preserved", -1, {}}};
    HdSilkNestedLinkDiagnostics diagnostics;
    HdSilkInstancerLevel singleton{"/Leaf", 1, {0}, {{"r127"}}};
    Require(HdSilkAppendPathMemberships("/Geom/Unused", {}, {}, {singleton}, 0, nullptr, &diagnostics) &&
            HdSilkAppendNestedInstanceMemberships("/Geom/Unused", {}, {}, {singleton}, 0, nullptr, nullptr) &&
            !diagnostics.Any(), "null collection output must be a harmless no-op");
    Require(HdSilkAppendPathMemberships("/Geom/Default", {}, {}, {}, 0, &preserved, nullptr),
        "path-only collection with no diagnostic sink");
    HdSilkSceneState state;
    HdSilkLightRecord linked;
    linked.path = "/Lights/Only";
    linked.type = OPENUSD_SILK_LIGHT_DISTANT;
    linked.lightLinkCategory = "r127";
    state.ReplaceLight(linked);
    state.SetCategoryMemberships(std::move(preserved), false);
    uint32_t count = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &count);
    const RetainedPage page = ReadRetainedPage(bytes, count);
    RequireOrder(page, {1u, 8u}, "optional collection sinks");
    Require(page.links.rows.size() == 2 && page.links.rows[0].path == "/Geom/Default" &&
            page.links.rows[1].path == "/Geom/Preserved" && page.links.reasons == 0u,
        "optional sinks changed an earlier row or invented /Geom/Unused");
    RequireMasks(page.links.rows[1].masks, ZeroWords, Words{1u, 0u, 0u, 0u}, 0u,
        "optional sinks still publish the exact preserved restriction");
}

void VerifyPublishedLinkBudgetAtomicity()
{
    static_assert(OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_TRUNCATED == 1u);
    static_assert(OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_DOME_BUDGET == 2u);
    struct BudgetCase
    {
        size_t earlier;
        bool finalGroup;
        size_t admitted;
        size_t dropped;
    };
    const BudgetCase cases[] = {
        {4095, false, 4095, 0}, {4096, false, 4096, 0}, {4097, false, 4096, 1},
        {4093, true, 4096, 0}, {4094, true, 4094, 3}};
    for (const BudgetCase& row : cases)
    {
        HdSilkSceneState state;
        std::vector<std::string> full;
        for (uint32_t index = 0; index < 128; ++index)
        {
            const std::string digits = std::to_string(index);
            const std::string suffix = std::string(3 - digits.size(), '0') + digits;
            HdSilkLightRecord light;
            light.path = "/Lights/L" + suffix;
            light.type = OPENUSD_SILK_LIGHT_DISTANT;
            light.intensity = static_cast<float>(index + 1);
            light.lightLinkCategory = "r" + suffix;
            light.shadowLinkCategory = "s" + suffix;
            state.ReplaceLight(light);
            full.push_back("r" + suffix);
            full.push_back("s" + suffix);
        }
        for (uint32_t index = 0; index < 3; ++index)
        {
            HdSilkLightRecord dome;
            dome.path = "/Domes/D" + std::to_string(index);
            dome.ambientOnly = true;
            dome.lightLinkCategory = "d" + std::to_string(index);
            state.ReplaceLight(dome);
            full.push_back(dome.lightLinkCategory);
        }
        std::vector<std::string> restricted = ReceiverBShadowS;
        restricted.insert(restricted.end(), {"d0", "d2"});
        std::vector<std::string> alternate = ReceiverSShadowB;
        alternate.push_back("d1");
        std::vector<HdSilkCategoryMembership> memberships;
        for (size_t index = 0; index < row.earlier; ++index)
        {
            const std::string digits = std::to_string(index);
            HdSilkCategoryMembership membership;
            membership.path = "/Budget/P" + std::string(4 - digits.size(), '0') + digits;
            membership.categories = restricted;
            memberships.push_back(membership);
        }
        if (row.finalGroup)
        {
            memberships.push_back(HdSilkCategoryMembership{"/Budget/ZLast", 15, full});
            memberships.push_back(HdSilkCategoryMembership{"/Budget/ZLast", -1, restricted});
            memberships.push_back(HdSilkCategoryMembership{"/Budget/ZLast", 5, alternate});
        }
        state.SetCategoryMemberships(std::move(memberships), false);
        uint64_t revision = 0;
        uint32_t count = 0;
        const uint64_t truncatedBefore = HdSilkSceneState::GetTruncatedLinkCount();
        std::vector<uint8_t> bytes = state.BuildPage(&revision, &count);
        const RetainedPage page = ReadRetainedPage(bytes, count);
        const std::string label = "published budget " + std::to_string(row.earlier) +
            (row.finalGroup ? " plus whole three-row path" : " singleton paths");
        RequireOrder(page, {1u, 8u}, label);
        Require(revision == 1u && page.frame.directLightCount == 128u && page.frame.domeCount == 3u &&
                page.links.directCount == 128u && page.links.domeCount == 3u &&
                page.links.rows.size() == row.admitted && page.links.reasons == (row.dropped == 0 ? 0u : 1u) &&
                HdSilkSceneState::GetTruncatedLinkCount() - truncatedBefore == row.dropped,
            label + ": wrong post-canonicalization capacity or exact truncation reason/count");
        const size_t admittedEarlier = std::min(row.earlier, size_t{4096});
        for (size_t index = 0; index < admittedEarlier; ++index)
        {
            const std::string digits = std::to_string(index);
            const LinkRow& actual = page.links.rows[index];
            Require(actual.path == "/Budget/P" + std::string(4 - digits.size(), '0') + digits &&
                    actual.instance == -1,
                label + ": earlier admitted path order/identity changed");
            RequireMasks(actual.masks, BoundaryWords, OtherWords, 5u, label + " admitted earlier masks");
        }
        if (row.finalGroup && row.dropped == 0)
        {
            const LinkRow& fallback = page.links.rows[row.earlier];
            const LinkRow& exact = page.links.rows[row.earlier + 1];
            const LinkRow& optIn = page.links.rows[row.earlier + 2];
            Require(fallback.path == "/Budget/ZLast" && fallback.instance == -1 &&
                    exact.path == fallback.path && exact.instance == 5 &&
                    optIn.path == fallback.path && optIn.instance == 15,
                label + ": exact-fit group must be admitted whole and sorted by signed instance");
            RequireMasks(fallback.masks, BoundaryWords, OtherWords, 5u, label + " path");
            RequireMasks(exact.masks, OtherWords, BoundaryWords, 2u, label + " override");
            RequireMasks(optIn.masks, FullWords, FullWords, 7u, label + " explicit full opt-in");
        }
        else if (row.dropped != 0)
        {
            const std::string omitted = row.finalGroup ? "/Budget/ZLast" : "/Budget/P4096";
            Require(std::none_of(page.links.rows.begin(), page.links.rows.end(),
                    [&](const LinkRow& entry) { return entry.path == omitted; }),
                label + ": over-budget path was partially admitted");
            for (int32_t instance : {-1, 5, 15, 99})
            {
                RequireMasks(LookupPublished(page.links, omitted, instance, FullWords, 7u),
                    FullWords, FullWords, 7u, label + " dropped path fails open");
            }
        }
        state.SetCategoryMemberships({}, false);
        bytes = state.BuildPage(&revision, &count);
        const RetainedPage retired = ReadRetainedPage(bytes, count);
        RequireOrder(retired, {1u, 8u}, label + " retirement");
        Require(revision == 2u && retired.links.rows.empty() && retired.links.reasons == 0u &&
                retired.links.directCount == 0u && retired.links.domeCount == 0u &&
                retired.frameBytes == page.frameBytes,
            label + ": final meaningful restriction did not retire canonically");
        bytes = state.BuildPage(&revision, &count);
        const RetainedPage quiet = ReadRetainedPage(bytes, count);
        RequireOrder(quiet, {1u}, label + " retirement settles");
        Require(revision == 3u && quiet.frameBytes == retired.frameBytes,
            label + ": retired table repeated");
    }

    // 5000 differing raw category sets, but only ONE meaningful published row.
    // No dense per-row full-128 category copies are needed: the other 127
    // receiver collections and every caster collection have empty identities.
    HdSilkSceneState state;
    for (uint32_t index = 0; index < 128; ++index)
    {
        const std::string digits = std::to_string(index);
        HdSilkLightRecord light;
        light.path = "/Lights/L" + std::string(3 - digits.size(), '0') + digits;
        light.type = OPENUSD_SILK_LIGHT_DISTANT;
        light.intensity = static_cast<float>(index + 1);
        light.lightLinkCategory = index == 31 ? "r031" : "";
        state.ReplaceLight(light);
    }
    std::vector<HdSilkCategoryMembership> raw;
    for (size_t index = 0; index < 5000; ++index)
    {
        HdSilkCategoryMembership membership;
        membership.path = "/Default/P" + std::to_string(index);
        membership.categories = {"r031", "irrelevant-" + std::to_string(index)};
        raw.push_back(membership);
    }
    raw.push_back(HdSilkCategoryMembership{"/Meaningful/Last", -1, {}});
    state.SetCategoryMemberships(std::move(raw), false);
    uint64_t revision = 0;
    uint32_t count = 0;
    const uint64_t truncatedBefore = HdSilkSceneState::GetTruncatedLinkCount();
    const std::vector<uint8_t> bytes = state.BuildPage(&revision, &count);
    const RetainedPage page = ReadRetainedPage(bytes, count);
    RequireOrder(page, {1u, 8u}, "irrelevant raw category budget");
    Require(revision == 1u && page.links.rows.size() == 1 &&
            page.links.rows[0].path == "/Meaningful/Last" && page.links.reasons == 0u &&
            page.links.directCount == 128u && HdSilkSceneState::GetTruncatedLinkCount() == truncatedBefore,
        "irrelevant categories crowded out the only meaningful restriction or falsely reported truncation");
    RequireMasks(page.links.rows[0].masks,
        Words{0x7fffffffu, 0xffffffffu, 0xffffffffu, 0xffffffffu}, FullWords, 0u,
        "post-canonicalization meaningful wide restriction");
    RequireMasks(LookupPublished(page.links, "/Default/P4999", 0), FullWords, FullWords, 0u,
        "irrelevant row remains default full-128");
}

void RequireEnvironment(
    const EnvironmentView& environment, const std::string& path, const std::string& texture,
    uint32_t domeIndex, uint32_t reasons, float intensity, const std::string& label)
{
    Require(environment.path == path && environment.hash != 0u && environment.texture == texture &&
            environment.domeIndex == domeIndex && environment.reasons == reasons &&
            environment.format == 1u && environment.colorSpace == 1u,
        label + ": wrong environment identity, association, metadata or texture identifier");
    Require(environment.color == std::array<float, 3>{0.25f, 0.5f, 1.0f} &&
            environment.intensity == intensity && environment.exposure == 1.0f &&
            environment.diffuse == 0.5f && environment.specular == 0.75f &&
            environment.transform == IdentityTransform,
        label + ": environment emission/transform did not survive dome-table fallback");
}

void VerifyDomeBudgetAmbientAndEnvironment()
{
    for (uint32_t directCount : {0u, 128u})
    {
        for (uint32_t domeCount : {0u, 1u, 8u, 9u})
        {
            HdSilkSceneState state;
            for (uint32_t index = 0; index < directCount; ++index)
            {
                const std::string digits = std::to_string(index);
                HdSilkLightRecord light;
                light.path = "/Lights/L" + std::string(3 - digits.size(), '0') + digits;
                light.type = OPENUSD_SILK_LIGHT_DISTANT;
                light.intensity = static_cast<float>(index + 1);
                state.ReplaceLight(light);
            }
            for (uint32_t remaining = domeCount; remaining > 0; --remaining)
            {
                const uint32_t index = remaining - 1;
                HdSilkLightRecord dome;
                dome.path = "/Domes/D" + std::to_string(index);
                dome.ambientOnly = true;
                dome.color[0] = 0.25f;
                dome.color[1] = 0.5f;
                dome.color[2] = 1.0f;
                dome.intensity = static_cast<float>(index + 1);
                dome.exposure = 1.0f;
                dome.diffuse = 0.5f;
                state.ReplaceLight(dome);
            }
            uint64_t revision = 0;
            uint32_t count = 0;
            const std::vector<uint8_t> bytes = state.BuildPage(&revision, &count);
            const RetainedPage page = ReadRetainedPage(bytes, count);
            const std::string label = "domes " + std::to_string(domeCount) +
                " alongside direct " + std::to_string(directCount);
            if (domeCount == 9)
            {
                RequireOrder(page, {1u, 8u}, label);
                Require(page.links.rows.empty() && page.links.reasons == 2u &&
                        page.links.directCount == 0u && page.links.domeCount == 0u,
                    label + ": unlinked dome-budget diagnostic must retain canonical zero counts");
            }
            else
            {
                RequireOrder(page, {1u}, label);
            }
            Require(revision == 1u && page.frame.directLightCount == directCount &&
                    page.frame.lightingFlags == (directCount == 0 ? 0u : 1u) &&
                    page.frame.domeCount == (domeCount == 9 ? 0u : domeCount) &&
                    page.frame.ambientIntensity == (domeCount == 0 ? 0.0f : 1.0f),
                label + ": dome capacity changed effective direct capacity or authored/ambient flags");
            std::array<float, 3> ambient{};
            for (uint32_t index = 0; index < domeCount; ++index)
            {
                // Independent sorted-order arithmetic, not a decoded sum or
                // a call to selection/snapshot helpers. Exposure 1 gives 2.
                const float exposed = 0.96f * static_cast<float>(index + 1) * 2.0f * 0.5f;
                const std::array<float, 3> contribution{0.25f * exposed, 0.5f * exposed, exposed};
                for (size_t channel = 0; channel < 3; ++channel)
                {
                    ambient[channel] += contribution[channel];
                }
                if (domeCount <= 8)
                {
                    Require(page.frame.domes[index].ambientColor == contribution &&
                            page.frame.domes[index].flags == 1u,
                        label + ": wrong sorted dome summand/identity");
                }
            }
            Require(page.frame.ambientColor == ambient, label + ": ninth dome or sorted ambient accumulation was lost");
            for (size_t index = page.frame.domeCount; index < 8; ++index)
            {
                Require(page.frame.domes[index].flags == 0u &&
                        page.frame.domes[index].ambientColor == std::array<float, 3>{0.0f, 0.0f, 0.0f},
                    label + ": ninth dome must clear the whole table, not keep a prefix");
            }
            for (uint32_t index = 0; index < directCount; ++index)
            {
                Require(page.frame.directLights[index].intensity == static_cast<float>(index + 1) &&
                        page.frame.directLights[index].type == 1u && page.frame.directLights[index].shadowEnabled == 0u,
                    label + ": dome capacity shifted direct identities");
            }
            RequireUnusedDirectSlots(page.frame, directCount, label);
        }
    }

    struct DomePolicyCase
    {
        const char* name;
        bool visible;
        float intensity;
        std::array<float, 3> color;
        float exposure;
        float diffuse;
        std::array<float, 3> contribution;
    };
    const DomePolicyCase policies[] = {
        {"hidden-nonzero", false, 2.0f, {1.0f, 0.5f, 0.25f}, 1.0f, 0.5f, {1.92f, 0.96f, 0.48f}},
        {"zero-intensity", true, 0.0f, {1.0f, 0.5f, 0.25f}, 1.0f, 0.5f, {0.0f, 0.0f, 0.0f}},
        {"zero-rgb", true, 2.0f, {0.0f, 0.0f, 0.0f}, 1.0f, 0.5f, {0.0f, 0.0f, 0.0f}},
        {"zero-diffuse", true, 2.0f, {1.0f, 0.5f, 0.25f}, 1.0f, 0.0f, {0.0f, 0.0f, 0.0f}},
        {"finite-underflow", true, 1.0f, {1.0f, 0.5f, 0.25f}, -200.0f, 1.0f, {0.0f, 0.0f, 0.0f}}};
    for (const DomePolicyCase& policy : policies)
    {
        for (uint32_t domeCount : {8u, 9u})
        {
            HdSilkSceneState state;
            for (uint32_t index = 0; index < 128; ++index)
            {
                const std::string digits = std::to_string(index);
                HdSilkLightRecord light;
                light.path = "/Lights/L" + std::string(3 - digits.size(), '0') + digits;
                light.type = OPENUSD_SILK_LIGHT_DISTANT;
                light.intensity = static_cast<float>(index + 1);
                state.ReplaceLight(light);
            }
            std::array<float, 3> ambient{};
            for (uint32_t index = 0; index < domeCount; ++index)
            {
                HdSilkLightRecord dome;
                dome.path = "/Domes/D" + std::to_string(index);
                dome.ambientOnly = true;
                dome.color[0] = 0.25f;
                dome.color[1] = 0.5f;
                dome.color[2] = 1.0f;
                dome.intensity = static_cast<float>(index + 1);
                dome.exposure = 1.0f;
                dome.diffuse = 0.5f;
                std::array<float, 3> contribution{};
                if (index + 1 == domeCount)
                {
                    dome.visible = policy.visible;
                    dome.intensity = policy.intensity;
                    std::copy(policy.color.begin(), policy.color.end(), dome.color);
                    dome.exposure = policy.exposure;
                    dome.diffuse = policy.diffuse;
                    contribution = policy.contribution;
                }
                else
                {
                    const float exposed = 0.96f * static_cast<float>(index + 1) * 2.0f * 0.5f;
                    contribution = {0.25f * exposed, 0.5f * exposed, exposed};
                }
                for (size_t channel = 0; channel < 3; ++channel)
                {
                    ambient[channel] += contribution[channel];
                }
                state.ReplaceLight(dome);
            }
            uint32_t count = 0;
            const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &count);
            const RetainedPage page = ReadRetainedPage(bytes, count);
            const std::string label = std::string(policy.name) + " dome " + std::to_string(domeCount);
            Require(page.frame.directLightCount == 128u && page.frame.directLights[127].intensity == 128.0f &&
                    page.frame.domeCount == (domeCount == 8 ? 8u : 0u) &&
                    page.frame.ambientIntensity == 1.0f && page.frame.ambientColor == ambient,
                label + ": direct eligibility was incorrectly applied to dome count/contribution");
            if (domeCount == 8)
            {
                RequireOrder(page, {1u}, label);
                Require(page.frame.domes[7].flags == 1u &&
                        page.frame.domes[7].ambientColor == policy.contribution,
                    label + ": hidden/dark eighth dome must still occupy its slot");
            }
            else
            {
                RequireOrder(page, {1u, 8u}, label);
                Require(page.links.reasons == 2u && page.links.rows.empty(),
                    label + ": hidden/dark ninth dome must still exceed the independent budget");
                for (const DomeSlot& slot : page.frame.domes)
                {
                    Require(slot.flags == 0u && slot.ambientColor == std::array<float, 3>{0.0f, 0.0f, 0.0f},
                        label + ": a partial dome table survived fallback");
                }
            }
        }
    }

    HdSilkSceneState mixed;
    for (uint32_t index = 0; index < 128; ++index)
    {
        const std::string digits = std::to_string(index);
        const std::string suffix = std::string(3 - digits.size(), '0') + digits;
        HdSilkLightRecord light;
        light.path = "/Lights/L" + suffix;
        light.type = OPENUSD_SILK_LIGHT_DISTANT;
        light.intensity = static_cast<float>(index + 1);
        light.lightLinkCategory = "r" + suffix;
        light.shadowLinkCategory = "s" + suffix;
        mixed.ReplaceLight(light);
    }
    std::vector<HdSilkLightRecord> domes;
    std::vector<std::string> categories = ReceiverBShadowS;
    std::array<float, 3> ambient{};
    for (uint32_t index = 0; index < 9; ++index)
    {
        const std::string suffix = "00" + std::to_string(index);
        HdSilkLightRecord dome;
        dome.path = "/Domes/D" + suffix;
        dome.ambientOnly = true;
        dome.color[0] = 0.25f;
        dome.color[1] = 0.5f;
        dome.color[2] = 1.0f;
        dome.intensity = static_cast<float>(index + 1);
        dome.exposure = 1.0f;
        dome.diffuse = 0.5f;
        dome.specular = 0.75f;
        dome.lightLinkCategory = "d" + suffix;
        if (index % 2 != 0 || index == 8)
        {
            dome.textureAsset = "opaque:direct-light/dome-" + suffix + ".exr";
            dome.textureFormat = OPENUSD_SILK_DOME_TEXTURE_LATLONG;
            dome.sourceColorSpace = OPENUSD_SILK_COLOR_SPACE_RAW;
            if (index != 8)
            {
                categories.push_back(dome.lightLinkCategory);
            }
        }
        else
        {
            const float exposed = 0.96f * static_cast<float>(index + 1) * 2.0f * 0.5f;
            ambient[0] += 0.25f * exposed;
            ambient[1] += 0.5f * exposed;
            ambient[2] += exposed;
        }
        if (index < 8)
        {
            mixed.ReplaceLight(dome);
        }
        domes.push_back(dome);
    }
    mixed.SetCategoryMemberships({HdSilkCategoryMembership{"/Geom/Mixed", -1, categories}}, false);
    uint64_t revision = 0;
    uint32_t count = 0;
    std::vector<uint8_t> bytes = mixed.BuildPage(&revision, &count);
    const RetainedPage eight = ReadRetainedPage(bytes, count);
    RequireOrder(eight, {1u, 8u, 6u, 6u, 6u, 6u}, "eight mixed domes");
    Require(revision == 1u && eight.frame.directLightCount == 128u && eight.frame.domeCount == 8u &&
            eight.frame.ambientColor == ambient && eight.frame.ambientIntensity == 1.0f,
        "eight mixed domes: textured domes must not double-count ambient");
    RequireMasks(eight.links.rows.at(0).masks, BoundaryWords, OtherWords, 0xaau, "eight mixed masks");
    for (size_t index = 0; index < 8; ++index)
    {
        const bool textured = index % 2 != 0;
        const float exposed = 0.96f * static_cast<float>(index + 1) * 2.0f * 0.5f;
        Require(eight.frame.domes[index].flags == (textured ? 3u : 1u) &&
                eight.frame.domes[index].ambientColor == (textured
                    ? std::array<float, 3>{0.0f, 0.0f, 0.0f}
                    : std::array<float, 3>{0.25f * exposed, 0.5f * exposed, exposed}),
            "mixed dome slot must distinguish textured from untextured contribution");
    }
    for (size_t index = 0; index < 4; ++index)
    {
        const size_t identity = 2 * index + 1;
        RequireEnvironment(eight.environments[index], domes[identity].path, domes[identity].textureAsset,
            static_cast<uint32_t>(identity), 0u, static_cast<float>(identity + 1), "eight association");
    }
    mixed.ReplaceLight(domes[8]);
    bytes = mixed.BuildPage(&revision, &count);
    const RetainedPage nine = ReadRetainedPage(bytes, count);
    RequireOrder(nine, {1u, 8u, 6u, 6u, 6u, 6u, 6u}, "ninth mixed dome");
    Require(revision == 2u && nine.frame.directLightCount == 128u && nine.frame.domeCount == 0u &&
            nine.frame.ambientColor == ambient && nine.frame.ambientIntensity == 1.0f &&
            nine.links.directCount == 128u && nine.links.domeCount == 0u && nine.links.reasons == 2u,
        "ninth mixed dome must preserve direct/ambient lighting and name only dome-budget fallback");
    RequireMasks(nine.links.rows.at(0).masks, BoundaryWords, OtherWords, 0u, "ninth dome keeps wide masks");
    for (const DomeSlot& slot : nine.frame.domes)
    {
        Require(slot.flags == 0u && slot.ambientColor == std::array<float, 3>{0.0f, 0.0f, 0.0f},
            "ninth mixed dome left a partial table");
    }
    for (size_t index = 0; index < 5; ++index)
    {
        const size_t identity = index == 4 ? 8 : 2 * index + 1;
        RequireEnvironment(nine.environments[index], domes[identity].path, domes[identity].textureAsset,
            0xffffffffu, 4u, static_cast<float>(identity + 1), "nine keeps every environment, unassociated");
        if (index < 4)
        {
            Require(nine.environments[index].hash == eight.environments[index].hash,
                "dome-index fallback must not rotate an environment identity");
        }
    }
    mixed.RemoveLight(domes[8].path);
    bytes = mixed.BuildPage(&revision, &count);
    const RetainedPage restored = ReadRetainedPage(bytes, count);
    RequireOrder(restored, {1u, 8u, 6u, 6u, 6u, 6u, 7u}, "remove ninth mixed dome");
    Require(revision == 3u && restored.frameBytes == eight.frameBytes && restored.linkBytes == eight.linkBytes &&
            restored.environmentRemovals[0].path == "/Domes/D008" &&
            restored.environmentRemovals[0].hash == nine.environments[4].hash,
        "removing ninth did not restore eight associations and retire exactly the ninth environment");
    for (size_t index = 0; index < 4; ++index)
    {
        const size_t identity = 2 * index + 1;
        RequireEnvironment(restored.environments[index], domes[identity].path, domes[identity].textureAsset,
            static_cast<uint32_t>(identity), 0u, static_cast<float>(identity + 1), "restored eight association");
        Require(restored.environments[index].hash == eight.environments[index].hash,
            "restored environment identity changed");
    }
    bytes = mixed.BuildPage(&revision, &count);
    const RetainedPage quiet = ReadRetainedPage(bytes, count);
    RequireOrder(quiet, {1u}, "environment reassociation settles");
    Require(revision == 4u && quiet.frameBytes == eight.frameBytes, "environment reassociation did not settle");

    domes[7].textureAsset.clear();
    mixed.ReplaceLight(domes[7]);
    bytes = mixed.BuildPage(&revision, &count);
    const RetainedPage untextured = ReadRetainedPage(bytes, count);
    RequireOrder(untextured, {1u, 7u}, "textured to untextured dome retirement");
    const float added = 0.96f * 8.0f * 2.0f * 0.5f;
    Require(revision == 5u && untextured.environmentRemovals[0].path == "/Domes/D007" &&
            untextured.environmentRemovals[0].hash == eight.environments[3].hash &&
            untextured.frame.domes[7].flags == 1u && untextured.frame.domeCount == 8u &&
            untextured.frame.ambientColor ==
                std::array<float, 3>{ambient[0] + 0.25f * added, ambient[1] + 0.5f * added, ambient[2] + added},
        "retired texture must become its own ambient summand without losing the dome/link identity");
    bytes = mixed.BuildPage(&revision, &count);
    const RetainedPage retiredQuiet = ReadRetainedPage(bytes, count);
    RequireOrder(retiredQuiet, {1u}, "environment retirement settles");
    Require(revision == 6u && retiredQuiet.frameBytes == untextured.frameBytes,
        "environment retirement was repeated");
}

void VerifyIndependentDomeLinks()
{
    HdSilkSceneState state;
    std::vector<HdSilkLightRecord> lights;
    for (uint32_t index = 0; index < 128; ++index)
    {
        const std::string digits = std::to_string(index);
        const std::string suffix = std::string(3 - digits.size(), '0') + digits;
        HdSilkLightRecord light;
        light.path = "/Lights/L" + suffix;
        light.type = OPENUSD_SILK_LIGHT_DISTANT;
        light.intensity = static_cast<float>(index + 1);
        light.lightLinkCategory = "r" + suffix;
        light.shadowLinkCategory = "s" + suffix;
        state.ReplaceLight(light);
        lights.push_back(light);
    }
    std::vector<HdSilkLightRecord> domes;
    std::vector<std::string> allDomes;
    for (uint32_t index = 0; index < 9; ++index)
    {
        HdSilkLightRecord dome;
        dome.path = "/Domes/D" + std::to_string(index);
        dome.ambientOnly = true;
        dome.intensity = static_cast<float>(index + 1);
        dome.lightLinkCategory = "d" + std::to_string(index);
        if (index < 8)
        {
            state.ReplaceLight(dome);
            allDomes.push_back(dome.lightLinkCategory);
        }
        domes.push_back(dome);
    }
    struct DomeMaskCase
    {
        const char* path;
        std::vector<std::string> categories;
        uint32_t mask;
    };
    const DomeMaskCase masks[] = {
        {"/Geom/D0", {}, 0u}, {"/Geom/D1", {"d0"}, 1u},
        {"/Geom/D81", {"d0", "d7"}, 0x81u}, {"/Geom/Dff", allDomes, 0xffu}};
    std::vector<HdSilkCategoryMembership> memberships;
    for (const DomeMaskCase& row : masks)
    {
        HdSilkCategoryMembership membership;
        membership.path = row.path;
        membership.categories = ReceiverBShadowS;
        membership.categories.insert(membership.categories.end(), row.categories.begin(), row.categories.end());
        memberships.push_back(membership);
    }
    const std::vector<std::vector<std::string>> instanceDomeCategories{
        {"d0", "d7"}, {"d0"}, allDomes};
    const int32_t instances[] = {-1, 3, 5};
    for (size_t index = 0; index < 3; ++index)
    {
        HdSilkCategoryMembership membership;
        membership.path = "/Geom/Restricted";
        membership.instanceIndex = instances[index];
        membership.categories = ReceiverBShadowS;
        membership.categories.insert(membership.categories.end(),
            instanceDomeCategories[index].begin(), instanceDomeCategories[index].end());
        memberships.push_back(membership);
    }
    state.SetCategoryMemberships(std::move(memberships), false);
    uint64_t revision = 0;
    uint32_t count = 0;
    std::vector<uint8_t> bytes = state.BuildPage(&revision, &count);
    const RetainedPage baseline = ReadRetainedPage(bytes, count);
    RequireOrder(baseline, {1u, 8u}, "independent dome words");
    Require(revision == 1u && baseline.frame.directLightCount == 128u && baseline.frame.domeCount == 8u &&
            baseline.links.directCount == 128u && baseline.links.domeCount == 8u &&
            baseline.links.reasons == 0u && baseline.links.rows.size() == 7 && state.HasLightLinks(),
        "independent dome words: wrong table/header/cardinality");
    for (const DomeMaskCase& row : masks)
    {
        RequireMasks(LookupPublished(baseline.links, row.path, 0, FullWords, 0xffu),
            BoundaryWords, OtherWords, row.mask, row.path);
    }
    RequireMasks(LookupPublished(baseline.links, "/Geom/Restricted", 99, FullWords, 0xffu),
        BoundaryWords, OtherWords, 0x81u, "dome path inheritance independent of direct words");
    RequireMasks(LookupPublished(baseline.links, "/Geom/Restricted", 3, FullWords, 0xffu),
        BoundaryWords, OtherWords, 1u, "dome-only exact override");
    RequireMasks(LookupPublished(baseline.links, "/Geom/Restricted", 5, FullWords, 0xffu),
        BoundaryWords, OtherWords, 0xffu, "dome-only full opt-in remains explicit");
    RequireMasks(LookupPublished(baseline.links, "/Geom/Omitted", 3, FullWords, 0xffu),
        FullWords, FullWords, 0xffu, "independent global dome defaults");

    lights[32].lightLinkCategory = "unmatched-receiver";
    state.ReplaceLight(lights[32]);
    bytes = state.BuildPage(&revision, &count);
    const RetainedPage receiverEdit = ReadRetainedPage(bytes, count);
    RequireOrder(receiverEdit, {1u, 8u}, "direct edit preserves dome masks");
    Require(revision == 2u && receiverEdit.frameBytes == baseline.frameBytes,
        "receiver-only collection edit must not change FRAME");
    for (const DomeMaskCase& row : masks)
    {
        RequireMasks(LookupPublished(receiverEdit.links, row.path, 0, FullWords, 0xffu),
            BoundaryWithout32, OtherWords, row.mask, "direct edit independent dome " + std::string(row.path));
    }
    domes[7].lightLinkCategory = "unmatched-dome";
    state.ReplaceLight(domes[7]);
    bytes = state.BuildPage(&revision, &count);
    const RetainedPage domeEdit = ReadRetainedPage(bytes, count);
    RequireOrder(domeEdit, {1u, 8u}, "dome edit preserves both direct channels");
    Require(revision == 3u && domeEdit.frameBytes == baseline.frameBytes && domeEdit.links.rows.size() == 6,
        "dome edit should elide exact instance 3 when it becomes equal to its path");
    for (const DomeMaskCase& row : masks)
    {
        RequireMasks(LookupPublished(domeEdit.links, row.path, 0, FullWords, 0xffu),
            BoundaryWithout32, OtherWords, row.mask & 0x7fu, "dome edit independent direct " + std::string(row.path));
    }
    RequireMasks(LookupPublished(domeEdit.links, "/Geom/Restricted", 3, FullWords, 0xffu),
        BoundaryWithout32, OtherWords, 1u, "canonicalized exact dome override inherits its own path");
    lights[32].lightLinkCategory = "r032";
    domes[7].lightLinkCategory = "d7";
    state.ReplaceLight(lights[32]);
    state.ReplaceLight(domes[7]);
    bytes = state.BuildPage(&revision, &count);
    const RetainedPage restored = ReadRetainedPage(bytes, count);
    RequireOrder(restored, {1u, 8u}, "independent categories restored");
    Require(revision == 4u && restored.linkBytes == baseline.linkBytes && restored.frameBytes == baseline.frameBytes,
        "restoring independent receiver/dome categories failed to restore the original table");
    state.ReplaceLight(domes[8]);
    bytes = state.BuildPage(&revision, &count);
    const RetainedPage nine = ReadRetainedPage(bytes, count);
    RequireOrder(nine, {1u, 8u}, "independent masks at nine domes");
    Require(revision == 5u && nine.frame.directLightCount == 128u && nine.frame.domeCount == 0u &&
            nine.links.directCount == 128u && nine.links.domeCount == 0u && nine.links.reasons == 2u &&
            nine.links.rows.size() == 5,
        "ninth dome must retire all dome-only overrides but retain every wide direct restriction");
    for (const DomeMaskCase& row : masks)
    {
        RequireMasks(LookupPublished(nine.links, row.path, 0), BoundaryWords, OtherWords, 0u,
            "ninth dome direct masks " + std::string(row.path));
    }
    RequireMasks(LookupPublished(nine.links, "/Geom/Restricted", 5), BoundaryWords, OtherWords, 0u,
        "ninth dome retires exact dome opt-in without changing direct masks");
    state.RemoveLight(domes[8].path);
    bytes = state.BuildPage(&revision, &count);
    const RetainedPage eightAgain = ReadRetainedPage(bytes, count);
    RequireOrder(eightAgain, {1u, 8u}, "eight independent dome masks restored");
    Require(revision == 6u && eightAgain.linkBytes == baseline.linkBytes && eightAgain.frameBytes == baseline.frameBytes,
        "removing ninth did not restore the independent low-eight masks");
    bytes = state.BuildPage(&revision, &count);
    const RetainedPage quiet = ReadRetainedPage(bytes, count);
    RequireOrder(quiet, {1u}, "independent dome table settles");
    Require(revision == 7u && quiet.frameBytes == baseline.frameBytes, "dome restoration did not settle");

    struct CollectionCase { bool receiver; bool caster; };
    const CollectionCase collections[] = {{false, false}, {true, false}, {false, true}, {true, true}};
    for (const CollectionCase& row : collections)
    {
        HdSilkSceneState domeOnly;
        HdSilkLightRecord hidden;
        hidden.path = "/Lights/Hidden";
        hidden.type = OPENUSD_SILK_LIGHT_SPHERE;
        hidden.visible = false;
        hidden.intensity = 2.0f;
        hidden.lightLinkCategory = "ignored-hidden-receiver";
        hidden.shadowLinkCategory = "ignored-hidden-caster";
        domeOnly.ReplaceLight(hidden);
        HdSilkLightRecord dome;
        dome.path = "/Domes/Only";
        dome.ambientOnly = true;
        dome.textureAsset = "opaque:direct-light/only.exr";
        dome.textureFormat = OPENUSD_SILK_DOME_TEXTURE_LATLONG;
        dome.sourceColorSpace = OPENUSD_SILK_COLOR_SPACE_RAW;
        dome.color[0] = 0.25f;
        dome.color[1] = 0.5f;
        dome.color[2] = 1.0f;
        dome.intensity = 2.0f;
        dome.exposure = 1.0f;
        dome.diffuse = 0.5f;
        dome.specular = 0.75f;
        dome.lightLinkCategory = row.receiver ? "dome-receivers" : "";
        dome.shadowLinkCategory = row.caster ? "dome-casters" : "";
        // This is the adapter's retained diagnostic, not a claim that
        // ReplaceLight itself infers diagnostics from a collection name.
        dome.unsupportedFeatures = row.caster ? OPENUSD_SILK_ENVIRONMENT_UNSUPPORTED_SHADOW_COLLECTION : 0u;
        domeOnly.ReplaceLight(dome);
        domeOnly.SetCategoryMemberships({
            HdSilkCategoryMembership{"/Geom/DomeOnly", -1, {}},
            HdSilkCategoryMembership{"/Geom/DomeOnly", 2, {"dome-receivers"}}}, false);
        Require(domeOnly.HasLightLinks() == row.receiver,
            "a dome receiver collection activates linking; a dome caster collection alone must not");
        bytes = domeOnly.BuildPage(&revision, &count);
        const RetainedPage page = ReadRetainedPage(bytes, count);
        if (row.receiver)
        {
            RequireOrder(page, {1u, 8u, 6u}, "dome receiver with no effective direct");
            Require(page.links.rows.size() == 2 && page.links.directCount == 0u &&
                    page.links.domeCount == 1u && page.links.reasons == 0u,
                "dome receiver-only table has wrong counts/reasons");
            RequireMasks(LookupPublished(page.links, "/Geom/DomeOnly", 99, ZeroWords, 1u),
                ZeroWords, ZeroWords, 0u, "dome restricted path without direct lights");
            RequireMasks(LookupPublished(page.links, "/Geom/DomeOnly", 2, ZeroWords, 1u),
                ZeroWords, ZeroWords, 1u, "dome exact opt-in without direct lights");
        }
        else
        {
            RequireOrder(page, {1u, 6u}, "default or caster-only dome");
            Require(!page.hasLinks, "dome caster collection was incorrectly applied as a receiver table");
            RequireMasks(LookupPublished(page.links, "/Geom/DomeOnly", 99, ZeroWords, 1u),
                ZeroWords, ZeroWords, 1u, "empty receiver category means all prims, even with a caster collection");
        }
        Require(revision == 1u && page.frame.directLightCount == 0u && page.frame.lightingFlags == 1u &&
                page.frame.domeCount == 1u && page.frame.domes[0].flags == 3u &&
                page.frame.ambientColor == std::array<float, 3>{0.0f, 0.0f, 0.0f} &&
                page.frame.ambientIntensity == 0.0f,
            "textured-only dome must keep its environment, without hidden-direct or ambient double counting");
        RequireEnvironment(page.environments[0], "/Domes/Only", "opaque:direct-light/only.exr",
            0u, row.caster ? 8u : 0u, 2.0f, "preserved named dome shadow-collection diagnostic");
        RequireUnusedDirectSlots(page.frame, 0, "dome-only direct slots");
        bytes = domeOnly.BuildPage(&revision, &count);
        const RetainedPage settled = ReadRetainedPage(bytes, count);
        RequireOrder(settled, {1u}, "dome collection metadata settles");
        Require(revision == 2u && settled.frameBytes == page.frameBytes,
            "dome collection diagnostics/environment state republished without a change");
    }
}

void VerifyHighIndexShadowDescriptors()
{
    HdSilkSceneState state;
    std::vector<std::string> full;
    for (uint32_t index = 0; index < 128; ++index)
    {
        const std::string digits = std::to_string(index);
        const std::string suffix = std::string(3 - digits.size(), '0') + digits;
        HdSilkLightRecord light;
        light.path = "/Lights/L" + suffix;
        light.type = OPENUSD_SILK_LIGHT_DISTANT;
        light.intensity = static_cast<float>(index + 1);
        light.shadowEnabled = index == 31 || index == 64 || index == 96 || index == 127 ? 1u : 0u;
        light.lightLinkCategory = "r" + suffix;
        light.shadowLinkCategory = "s" + suffix;
        if (index == 127)
        {
            std::copy(TowardXTransform.begin(), TowardXTransform.end(), light.transform);
        }
        state.ReplaceLight(light);
        full.push_back("r" + suffix);
        full.push_back("s" + suffix);
    }
    HdSilkMeshRecord mesh;
    mesh.path = "/Geom/Caster";
    mesh.primId = 81;
    mesh.topologyRevision = 1;
    mesh.points = CasterPoints3;
    mesh.indices = {0u, 1u, 2u};
    mesh.triangleSubprims = {0u};
    state.ReplaceMeshInstances(mesh.path, {mesh});
    state.SetCategoryMemberships({HdSilkCategoryMembership{mesh.path, -1, full}}, false);
    uint64_t revision = 0;
    uint32_t count = 0;
    std::vector<uint8_t> bytes = state.BuildPage(&revision, &count);
    const RetainedPage baseline = ReadRetainedPage(bytes, count);
    RequireOrder(baseline, {1u, 9u, 2u}, "high shadow baseline");
    Require(revision == 1u && baseline.frame.directLightCount == 128u && !baseline.hasLinks,
        "full memberships should not emit a sparse link table");
    RequireShadowHeader(baseline, 4, 128u, 0u, "high shadow baseline");
    const std::array<uint32_t, 4> indices{31u, 64u, 96u, 127u};
    for (size_t map = 0; map < 4; ++map)
    {
        Require(baseline.frame.directLights[indices[map]].intensity == static_cast<float>(indices[map] + 1) &&
                baseline.frame.directLights[indices[map]].shadowEnabled == 1u,
            "high descriptor must name the same enabled identity in FRAME");
        RequireShadow(baseline.shadows.maps[map], indices[map], static_cast<uint32_t>(map),
            1u, map == 3 ? X3 : Z3, "high shadow full numerical baseline");
    }
    RequireTriangle(baseline.meshes[0], mesh.path, 81, 1, CasterPoints3, IdentityTransform,
        "", 0u, "actual retained caster bounds source");

    struct ExclusionCase
    {
        const char* category;
        Words receiver;
        Words caster;
        uint32_t highFlags;
        uint64_t revision;
    };
    const ExclusionCase exclusions[] = {
        {"s127", FullWords, Without127, 3u, 2u},
        {"r127", Without127, FullWords, 1u, 3u}};
    for (const ExclusionCase& row : exclusions)
    {
        std::vector<std::string> categories = full;
        categories.erase(std::remove(categories.begin(), categories.end(), row.category), categories.end());
        state.SetCategoryMemberships({HdSilkCategoryMembership{mesh.path, -1, categories}}, false);
        bytes = state.BuildPage(&revision, &count);
        const RetainedPage page = ReadRetainedPage(bytes, count);
        const std::string label = std::string("independent high-127 exclusion ") + row.category;
        RequireOrder(page, {1u, 8u, 9u}, label);
        Require(revision == row.revision && page.frameBytes == baseline.frameBytes &&
                page.links.rows.size() == 1 && page.links.rows[0].path == mesh.path &&
                page.links.rows[0].instance == -1,
            label + ": membership edit must not alter FRAME or invent identities");
        RequireMasks(page.links.rows[0].masks, row.receiver, row.caster, 0u, label);
        RequireShadowHeader(page, 4, 128u, 0u, label);
        for (size_t map = 0; map < 4; ++map)
        {
            RequireShadow(page.shadows.maps[map], indices[map], static_cast<uint32_t>(map),
                map == 3 ? row.highFlags : 1u, map == 3 ? X3 : Z3, label);
        }
        Require(page.shadows.maps[0].flags == 1u && page.links.rows[0].masks.shadow[0] == 0xffffffffu,
            label + ": bit 127 must not alias same-position bit 31");
        // Even excluding the only caster from high 127 must not prefilter
        // retained geometry out of the numerical radius-3 projection.
    }
    mesh.points = CasterPoints6;
    mesh.topologyRevision = 2;
    std::copy(TranslatedTransform.begin(), TranslatedTransform.end(), mesh.transform);
    state.ReplaceMeshInstances(mesh.path, {mesh});
    bytes = state.BuildPage(&revision, &count);
    const RetainedPage edited = ReadRetainedPage(bytes, count);
    RequireOrder(edited, {1u, 9u, 2u}, "translated radius-six caster");
    Require(revision == 4u && edited.frameBytes == baseline.frameBytes &&
            edited.shadowBytes != baseline.shadowBytes,
        "changed retained bounds must update shadows without inventing light/link changes");
    RequireShadowHeader(edited, 4, 128u, 0u, "translated radius-six caster");
    for (size_t map = 0; map < 4; ++map)
    {
        RequireShadow(edited.shadows.maps[map], indices[map], static_cast<uint32_t>(map),
            1u, map == 3 ? X6 : Z6, "translated center (4,5,6), radius 6");
    }
    RequireTriangle(edited.meshes[0], mesh.path, 81, 2, CasterPoints6, TranslatedTransform,
        "", 0u, "edited caster geometry");
    bytes = state.BuildPage(&revision, &count);
    const RetainedPage quiet = ReadRetainedPage(bytes, count);
    RequireOrder(quiet, {1u}, "high shadow steady page");
    Require(revision == 5u && quiet.frameBytes == edited.frameBytes,
        "unchanged high shadows republished or FRAME changed");
}

void VerifyShadowBudgetAndUnsupportedTypes()
{
    static_assert(OPENUSD_SILK_MAX_SHADOW_MAPS == 4u);
    static_assert(OPENUSD_SILK_SHADOW_UNSUPPORTED_LIGHT_TYPE == 1u);
    static_assert(OPENUSD_SILK_SHADOW_UNSUPPORTED_MAP_BUDGET == 2u);
    static_assert(OPENUSD_SILK_SHADOW_UNSUPPORTED_NO_CASTERS == 4u);
    struct MapCase
    {
        std::vector<uint32_t> enabled;
        std::vector<uint32_t> winners;
        uint32_t reasons;
        bool zeroLastDirection;
    };
    const std::vector<MapCase> mapCases{
        {{}, {}, 0u, false},
        {{127u}, {127u}, 0u, false},
        {{31u, 64u, 96u, 127u}, {31u, 64u, 96u, 127u}, 0u, false},
        {{30u, 31u, 64u, 96u, 127u}, {30u, 31u, 64u, 96u}, 2u, false},
        {{31u, 64u, 96u, 127u}, {31u, 64u, 96u, 127u}, 0u, false},
        // Budget is checked BEFORE direction eligibility: the fifth invalid
        // direction is diagnosed as budget overflow, not as NO_CASTERS.
        {{30u, 31u, 64u, 96u, 127u}, {30u, 31u, 64u, 96u}, 2u, true},
        {{31u, 64u, 96u, 127u}, {31u, 64u, 96u}, 4u, true},
        {{31u, 64u, 96u, 127u}, {31u, 64u, 96u, 127u}, 0u, false}};
    HdSilkSceneState state;
    HdSilkMeshRecord caster;
    caster.path = "/Geom/MapBudget";
    caster.primId = 91;
    caster.topologyRevision = 1;
    caster.points = CasterPoints3;
    caster.indices = {0u, 1u, 2u};
    caster.triangleSubprims = {0u};
    state.ReplaceMeshInstances(caster.path, {caster});
    uint64_t revision = 0;
    uint32_t count = 0;
    std::vector<uint8_t> originalFour;
    for (size_t step = 0; step < mapCases.size(); ++step)
    {
        const MapCase& row = mapCases[step];
        for (uint32_t index = 0; index < 128; ++index)
        {
            const std::string digits = std::to_string(index);
            HdSilkLightRecord light;
            light.path = "/Lights/L" + std::string(3 - digits.size(), '0') + digits;
            light.type = OPENUSD_SILK_LIGHT_DISTANT;
            light.intensity = static_cast<float>(index + 1);
            light.shadowEnabled = std::find(row.enabled.begin(), row.enabled.end(), index) != row.enabled.end() ? 1u : 0u;
            if (index == 127 && row.zeroLastDirection)
            {
                light.transform[10] = 0.0;
            }
            state.ReplaceLight(light);
        }
        const std::vector<uint8_t> bytes = state.BuildPage(&revision, &count);
        const RetainedPage page = ReadRetainedPage(bytes, count);
        const std::string label = "shadow budget step " + std::to_string(step);
        Require(revision == step + 1 && page.frame.directLightCount == 128u &&
                page.frame.directLights[127].intensity == 128.0f,
            label + ": map eligibility must not become another direct-light capacity/filter");
        if (step == 0)
        {
            RequireOrder(page, {1u, 2u}, label);
            Require(!page.hasShadows, "zero enabled maps must not emit an active shadow failure");
        }
        else
        {
            RequireOrder(page, {1u, 9u}, label);
            RequireShadowHeader(page, row.winners.size(), 128u, row.reasons, label);
            for (size_t map = 0; map < row.winners.size(); ++map)
            {
                RequireShadow(page.shadows.maps[map], row.winners[map], static_cast<uint32_t>(map),
                    1u, Z3, label);
            }
        }
        if (step == 2)
        {
            originalFour = page.shadowBytes;
        }
        if (step == 4 || step == 7)
        {
            Require(page.shadowBytes == originalFour,
                "disabling earlier fifth/restoring direction must restore maps 31/64/96/127 with no reason bits");
        }
    }

    struct ShapeCase { const char* name; uint32_t type; uint32_t wireType; };
    const ShapeCase shapes[] = {
        {"RECT", OPENUSD_SILK_LIGHT_RECT, 3u}, {"Sphere", OPENUSD_SILK_LIGHT_SPHERE, 2u},
        {"Disk", OPENUSD_SILK_LIGHT_DISK, 4u}, {"Cylinder", OPENUSD_SILK_LIGHT_CYLINDER, 5u}};
    for (const ShapeCase& shape : shapes)
    {
        for (bool enabled : {false, true})
        {
            for (bool fifth : {false, true})
            {
                HdSilkSceneState mixed;
                HdSilkMeshRecord mesh;
                mesh.path = "/Geom/Unsupported";
                mesh.primId = 92;
                mesh.topologyRevision = 1;
                mesh.points = CasterPoints3;
                mesh.indices = {0u, 1u, 2u};
                mesh.triangleSubprims = {0u};
                mixed.ReplaceMeshInstances(mesh.path, {mesh});
                for (uint32_t index = 0; index < 128; ++index)
                {
                    const std::string digits = std::to_string(index);
                    HdSilkLightRecord light;
                    light.path = "/Lights/L" + std::string(3 - digits.size(), '0') + digits;
                    light.type = index == 0 ? shape.type : OPENUSD_SILK_LIGHT_DISTANT;
                    light.intensity = static_cast<float>(index + 1);
                    light.shadowEnabled = index == 0 ? (enabled ? 1u : 0u) :
                        ((index == 30 && fifth) || index == 31 || index == 64 || index == 96 || index == 127 ? 1u : 0u);
                    if (index == 0)
                    {
                        light.radius = 0.375f;
                        light.shapeX = shape.type == OPENUSD_SILK_LIGHT_RECT ? 4.0f :
                            (shape.type == OPENUSD_SILK_LIGHT_CYLINDER ? 8.0f : 0.0f);
                        light.shapeY = shape.type == OPENUSD_SILK_LIGHT_RECT ? 6.0f : 0.0f;
                    }
                    mixed.ReplaceLight(light);
                }
                const std::vector<uint8_t> bytes = mixed.BuildPage(&revision, &count);
                const RetainedPage page = ReadRetainedPage(bytes, count);
                const std::string label = std::string(shape.name) + (enabled ? " shadow-on" : " shadow-off") +
                    (fifth ? " plus fifth distant" : " plus four distant");
                RequireOrder(page, {1u, 9u, 2u}, label);
                Require(revision == 1u && page.frame.directLightCount == 128u &&
                        page.frame.directLights[0].type == shape.wireType &&
                        page.frame.directLights[0].shadowEnabled == (enabled ? 1u : 0u) &&
                        page.frame.directLights[0].radius == 0.375f &&
                        page.frame.directLights[0].shapeX == (shape.wireType == 3u ? 4.0f : (shape.wireType == 5u ? 8.0f : 0.0f)) &&
                        page.frame.directLights[0].shapeY == (shape.wireType == 3u ? 6.0f : 0.0f),
                    label + ": unsupported SHADOW shape must remain an intact direct wire light");
                const uint32_t reasons = (enabled ? 1u : 0u) | (fifth ? 2u : 0u);
                RequireShadowHeader(page, 4, 128u, reasons, label);
                const std::array<uint32_t, 4> winners = fifth
                    ? std::array<uint32_t, 4>{30u, 31u, 64u, 96u}
                    : std::array<uint32_t, 4>{31u, 64u, 96u, 127u};
                for (size_t map = 0; map < 4; ++map)
                {
                    RequireShadow(page.shadows.maps[map], winners[map], static_cast<uint32_t>(map), 1u, Z3, label);
                }
            }
        }
    }

    struct EmptyCasterCase { bool distant; bool rect; uint32_t reasons; };
    const EmptyCasterCase emptyCases[] = {
        {false, false, 0u}, {true, false, 4u}, {false, true, 1u}, {true, true, 5u}};
    for (const EmptyCasterCase& row : emptyCases)
    {
        HdSilkSceneState empty;
        for (uint32_t index = 0; index < 128; ++index)
        {
            const std::string digits = std::to_string(index);
            HdSilkLightRecord light;
            light.path = "/Lights/L" + std::string(3 - digits.size(), '0') + digits;
            light.type = index == 0 ? OPENUSD_SILK_LIGHT_RECT : OPENUSD_SILK_LIGHT_DISTANT;
            light.intensity = static_cast<float>(index + 1);
            light.shadowEnabled = (index == 0 && row.rect) || (index == 127 && row.distant) ? 1u : 0u;
            empty.ReplaceLight(light);
        }
        const std::vector<uint8_t> bytes = empty.BuildPage(&revision, &count);
        const RetainedPage page = ReadRetainedPage(bytes, count);
        if (row.reasons == 0u)
        {
            RequireOrder(page, {1u}, "no enabled shadow light, no casters");
            Require(!page.hasShadows, "lack of geometry alone must not invent an active shadow diagnostic");
        }
        else
        {
            RequireOrder(page, {1u, 9u}, "no caster geometry");
            RequireShadowHeader(page, 0, 128u, row.reasons, "type reason precedes caster eligibility");
        }
        Require(revision == 1u && page.frame.directLightCount == 128u &&
                page.frame.directLights[0].type == 3u &&
                page.frame.directLights[127].shadowEnabled == (row.distant ? 1u : 0u),
            "no-caster/type diagnostic changed the selected direct identities");
    }
}

void VerifyShadowEligibilityBoundaries()
{
    // The representable value immediately above 1e-6. Its near/far doubles
    // are 1.0000010000000001 / 1.000003. Literal OpenGL depth coefficients
    // include their subtraction rounding, rather than substituting -1/r.
    constexpr ShadowOracle tinyAbove{
        {1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0,
         0.0, 0.0, 1.0, 0.0, 0.0, 0.0, -1.000002, 1.0},
        {999999.9999999999, 0.0, 0.0, 0.0,
         0.0, 999999.9999999999, 0.0, 0.0,
         0.0, 0.0, -1000000.0000822666, 0.0,
         0.0, 0.0, -1000002.0000822669, 1.0},
        2.9296875e-9f};
    struct GeometryCase
    {
        const char* name;
        int kind; // 0 absent, 1 zero-radius triangle, 2 scaled line-shaped triangle, 3 radius-three box.
        double scale;
        int relation;
        const ShadowOracle* oracle;
    };
    const GeometryCase geometry[] = {
        {"empty", 0, 0.0, -1, nullptr},
        {"zero-radius", 1, 0.0, -1, nullptr},
        {"radius-below", 2, std::nextafter(1e-6, 0.0), -1, nullptr},
        {"radius-exact", 2, 1e-6, 0, nullptr},
        {"radius-above", 2, std::nextafter(1e-6, 1.0), 1, &tinyAbove},
        {"usable-radius-three", 3, 1.0, 1, &Z3}};
    for (const GeometryCase& row : geometry)
    {
        HdSilkSceneState state;
        for (uint32_t index = 0; index < 128; ++index)
        {
            const std::string digits = std::to_string(index);
            HdSilkLightRecord light;
            light.path = "/Lights/L" + std::string(3 - digits.size(), '0') + digits;
            light.type = OPENUSD_SILK_LIGHT_DISTANT;
            light.intensity = static_cast<float>(index + 1);
            light.shadowEnabled = index == 127 ? 1u : 0u;
            state.ReplaceLight(light);
        }
        if (row.kind != 0)
        {
            HdSilkMeshRecord mesh;
            mesh.path = "/Geom/Cutoff";
            mesh.primId = 101;
            mesh.topologyRevision = 1;
            mesh.indices = {0u, 1u, 2u};
            mesh.triangleSubprims = {0u};
            if (row.kind == 1)
            {
                mesh.points = {0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f};
            }
            else if (row.kind == 2)
            {
                mesh.points = {-1.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f};
                mesh.transform[0] = row.scale;
                // Double transforms avoid float-point rounding at the cutoff.
                // The one nonzero half-extent is scale, so this is the actual
                // fixture radius, independently checked before publication.
                const double radius = std::sqrt(row.scale * row.scale);
                Require(radius == row.scale && (row.relation < 0 ? radius < 1e-6 :
                        (row.relation == 0 ? radius == 1e-6 : radius > 1e-6)),
                    std::string(row.name) + ": fixture rounded to the wrong side of the radius cutoff");
            }
            else
            {
                mesh.points = CasterPoints3;
            }
            state.ReplaceMeshInstances(mesh.path, {mesh});
        }
        uint64_t revision = 0;
        uint32_t count = 0;
        const std::vector<uint8_t> bytes = state.BuildPage(&revision, &count);
        const RetainedPage page = ReadRetainedPage(bytes, count);
        const std::string label = row.name;
        if (row.kind == 0)
        {
            RequireOrder(page, {1u, 9u}, label);
        }
        else
        {
            RequireOrder(page, {1u, 9u, 2u}, label);
            Require(page.meshes[0].path == "/Geom/Cutoff" && page.meshes[0].primId == 101 &&
                    page.meshes[0].topologyRevision == 1u && page.meshes[0].points.size() == 9 &&
                    page.meshes[0].indices == std::vector<uint32_t>{0u, 1u, 2u},
                label + ": finite cutoff geometry was unexpectedly skipped or changed");
        }
        Require(revision == 1u && page.frame.directLightCount == 128u && page.frame.lightingFlags == 1u &&
                page.frame.directLights[127].intensity == 128.0f && page.frame.directLights[127].shadowEnabled == 1u,
            label + ": shadow eligibility must not drop the high direct slot");
        RequireShadowHeader(page, row.oracle == nullptr ? 0 : 1, 128u, row.oracle == nullptr ? 4u : 0u, label);
        if (row.oracle != nullptr)
        {
            RequireShadow(page.shadows.maps[0], 127u, 0u, 1u, *row.oracle, label);
        }
    }

    // +Y exercises the alternate world-up basis; -Z and a non-unit +Z
    // exercise signed direction and normalization, without numerical snapshots.
    constexpr ShadowOracle y3{
        {0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0,
         1.0, 0.0, 0.0, 0.0, 0.0, 0.0, -7.0, 1.0},
        Projection3, 0.0087890625f};
    constexpr ShadowOracle negativeZ3{
        {-1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0,
         0.0, 0.0, -1.0, 0.0, 0.0, 0.0, -7.0, 1.0},
        Projection3, 0.0087890625f};
    struct DirectionCase
    {
        const char* name;
        std::array<double, 3> direction;
        int squaredRelation;
        const ShadowOracle* oracle;
    };
    const DirectionCase directions[] = {
        {"direction-zero", {0.0, 0.0, 0.0}, -1, nullptr},
        {"direction-below", {0.0, 0.0, std::nextafter(1e-6, 0.0)}, -1, nullptr},
        {"direction-exact", {0.0, 0.0, 1e-6}, 0, nullptr},
        {"direction-above", {0.0, 0.0, std::nextafter(1e-6, 1.0)}, 1, &Z3},
        {"direction-nonunit", {0.0, 0.0, 7.0}, 1, &Z3},
        {"direction-positive-x", {1.0, 0.0, 0.0}, 1, &X3},
        {"direction-up-fallback", {0.0, 1.0, 0.0}, 1, &y3},
        {"direction-negative-z", {0.0, 0.0, -1.0}, 1, &negativeZ3}};
    for (const DirectionCase& row : directions)
    {
        const double squared = row.direction[0] * row.direction[0] +
            row.direction[1] * row.direction[1] + row.direction[2] * row.direction[2];
        Require(row.squaredRelation < 0 ? squared < 1e-12 :
                (row.squaredRelation == 0 ? squared == 1e-12 : squared > 1e-12),
            std::string(row.name) + ": fixture rounded to the wrong side of the squared-direction cutoff");
        HdSilkSceneState state;
        for (uint32_t index = 0; index < 128; ++index)
        {
            const std::string digits = std::to_string(index);
            HdSilkLightRecord light;
            light.path = "/Lights/L" + std::string(3 - digits.size(), '0') + digits;
            light.type = OPENUSD_SILK_LIGHT_DISTANT;
            light.intensity = static_cast<float>(index + 1);
            light.shadowEnabled = index == 127 ? 1u : 0u;
            if (index == 127)
            {
                light.transform[8] = row.direction[0];
                light.transform[9] = row.direction[1];
                light.transform[10] = row.direction[2];
            }
            state.ReplaceLight(light);
        }
        HdSilkMeshRecord mesh;
        mesh.path = "/Geom/Direction";
        mesh.primId = 102;
        mesh.topologyRevision = 1;
        mesh.points = CasterPoints3;
        mesh.indices = {0u, 1u, 2u};
        mesh.triangleSubprims = {0u};
        state.ReplaceMeshInstances(mesh.path, {mesh});
        uint64_t revision = 0;
        uint32_t count = 0;
        const std::vector<uint8_t> bytes = state.BuildPage(&revision, &count);
        const RetainedPage page = ReadRetainedPage(bytes, count);
        const std::string label = row.name;
        RequireOrder(page, {1u, 9u, 2u}, label);
        Require(revision == 1u && page.frame.directLightCount == 128u &&
                page.frame.directLights[127].type == 1u && page.frame.directLights[127].shadowEnabled == 1u &&
                page.frame.directLights[127].transform[8] == row.direction[0] &&
                page.frame.directLights[127].transform[9] == row.direction[1] &&
                page.frame.directLights[127].transform[10] == row.direction[2],
            label + ": direction rejection must not become a direct selection filter");
        // Current source reuses NO_CASTERS (4) for a direction at/below
        // 1e-12; there is no separate invented direction-reason bit.
        RequireShadowHeader(page, row.oracle == nullptr ? 0 : 1, 128u, row.oracle == nullptr ? 4u : 0u, label);
        RequireTriangle(page.meshes[0], mesh.path, 102, 1, CasterPoints3, IdentityTransform,
            "", 0u, label + " usable caster control");
        if (row.oracle != nullptr)
        {
            RequireShadow(page.shadows.maps[0], 127u, 0u, 1u, *row.oracle, label);
        }
    }
}
}

int main(int argc, char* argv[])
{
    if (argc != 2)
    {
        std::cerr << "hdsilk_direct_lights_probe: expected one selector: masks, wire, selection\n";
        std::cerr << "additional selectors: transaction, links, domes-shadows\n";
        return 2;
    }
    struct TestCase
    {
        const char* selector;
        const char* name;
        void (*run)();
    };
    const TestCase cases[] = {
        {"masks", "VerifyLightMaskConstructionAndEquality", VerifyLightMaskConstructionAndEquality},
        {"masks", "VerifyLightMaskSetAndContainsBoundaries", VerifyLightMaskSetAndContainsBoundaries},
        {"masks", "VerifyLightMaskFirstBoundaries", VerifyLightMaskFirstBoundaries},
        {"wire", "VerifyNativeFrameAbiAndCounts", VerifyNativeFrameAbiAndCounts},
        {"wire", "VerifyDirectShapeAndControlWire", VerifyDirectShapeAndControlWire},
        {"selection", "VerifyDirectEligibilityPartitions", VerifyDirectEligibilityPartitions},
        {"selection", "VerifyNonFiniteDirectLightsRefuseAndRetry", VerifyNonFiniteDirectLightsRefuseAndRetry},
        {"selection", "VerifyAuthoredFlagsAndEffectiveCapacity", VerifyAuthoredFlagsAndEffectiveCapacity},
        {"transaction", "VerifyOverflowPreservesRetainedTransaction", VerifyOverflowPreservesRetainedTransaction},
        {"transaction", "VerifyRemoveReinsertCancelsPendingRemovals", VerifyRemoveReinsertCancelsPendingRemovals},
        {"transaction", "VerifySteadyPagesAndTableRetirement", VerifySteadyPagesAndTableRetirement},
        {"links", "VerifyWideLinkWordsAndSparseFallbacks", VerifyWideLinkWordsAndSparseFallbacks},
        {"links", "VerifySortedLightRemapping", VerifySortedLightRemapping},
        {"links", "VerifyNestedCompositeIdentityLinks", VerifyNestedCompositeIdentityLinks},
        {"links", "VerifyNestedFallbackAndCollectionLimits", VerifyNestedFallbackAndCollectionLimits},
        {"links", "VerifyPublishedLinkBudgetAtomicity", VerifyPublishedLinkBudgetAtomicity},
        {"domes-shadows", "VerifyDomeBudgetAmbientAndEnvironment", VerifyDomeBudgetAmbientAndEnvironment},
        {"domes-shadows", "VerifyIndependentDomeLinks", VerifyIndependentDomeLinks},
        {"domes-shadows", "VerifyHighIndexShadowDescriptors", VerifyHighIndexShadowDescriptors},
        {"domes-shadows", "VerifyShadowBudgetAndUnsupportedTypes", VerifyShadowBudgetAndUnsupportedTypes},
        {"domes-shadows", "VerifyShadowEligibilityBoundaries", VerifyShadowEligibilityBoundaries}};

    const std::string selector = argv[1];
    bool matched = false;
    for (const TestCase& test : cases)
    {
        if (selector != test.selector)
        {
            continue;
        }
        matched = true;
        try
        {
            test.run();
        }
        catch (const std::exception& error)
        {
            std::cerr << "hdsilk_direct_lights_probe: " << test.name << ": " << error.what() << '\n';
            return 1;
        }
        catch (...)
        {
            std::cerr << "hdsilk_direct_lights_probe: " << test.name << ": non-standard exception\n";
            return 1;
        }
        std::cout << test.name << " passed\n";
    }
    if (!matched)
    {
        std::cerr << "hdsilk_direct_lights_probe: unknown selector '" << selector
                  << "'; expected masks, wire, selection\n";
        std::cerr << "additional selectors: transaction, links, domes-shadows\n";
        return 2;
    }
    return 0;
}
