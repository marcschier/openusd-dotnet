// Copyright (c) marcschier. Licensed under the MIT License.

#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/gf/vec2i.h"
#include "pxr/base/gf/vec4d.h"
#include "pxr/base/plug/registry.h"
#include "pxr/base/tf/errorMark.h"
#include "pxr/imaging/garch/glApi.h"
#include "pxr/imaging/glf/contextCaps.h"
#include "pxr/imaging/hd/renderBuffer.h"
#include "pxr/imaging/hd/tokens.h"
#include "pxr/usd/usd/prim.h"
#include "pxr/usd/usd/stage.h"
#include "pxr/usdImaging/usdImagingGL/engine.h"
#include "pxr/usdImaging/usdImagingGL/renderParams.h"

#include "storm_aov_wgl_context.h"

#include <array>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <stdexcept>
#include <string>
#include <vector>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
constexpr uint32_t Width = 64;
constexpr uint32_t Height = 64;
constexpr uint64_t MaxBytes = 1048576;

void Require(bool condition, const char* message)
{
    if (!condition)
    {
        throw std::runtime_error(message);
    }
}

class BufferMap final
{
public:
    explicit BufferMap(HdRenderBuffer& buffer) : _buffer(buffer)
    {
        _buffer.Resolve();
        _data = _buffer.Map();
    }

    ~BufferMap()
    {
        _buffer.Unmap();
    }

    const void* Data() const
    {
        return _data;
    }

    BufferMap(const BufferMap&) = delete;
    BufferMap& operator=(const BufferMap&) = delete;

private:
    HdRenderBuffer& _buffer;
    const void* _data = nullptr;
};

struct Output
{
    TfToken name;
    HdRenderBuffer* buffer = nullptr;
    uint32_t bytes_per_pixel = 0;
    std::vector<uint8_t> data;
};

template <class TValue>
TValue Pixel(const Output& output, uint32_t x, uint32_t y)
{
    const size_t offset = (static_cast<size_t>(y) * Width + x) *
        output.bytes_per_pixel;
    Require(offset + sizeof(TValue) <= output.data.size(), "Pixel out of bounds.");
    TValue result{};
    std::memcpy(&result, output.data.data() + offset, sizeof(result));
    return result;
}

void VerifyDecoded(
    UsdImagingGLEngine& engine,
    const Output& prim_ids,
    const Output& instance_ids,
    uint32_t x,
    uint32_t y,
    const char* expected_path,
    int expected_instance)
{
    const int prim_id = Pixel<int32_t>(prim_ids, x, y);
    const int instance_id = Pixel<int32_t>(instance_ids, x, y);
    Require(prim_id >= 0, "Expected non-background prim coverage.");
    SdfPath prim_path;
    SdfPath instancer_path;
    int instance_index = -1;
    HdInstancerContext context;
    Require(
        engine.DecodeIntersection(
            prim_id, instance_id, &prim_path, &instancer_path,
            &instance_index, &context),
        "The native integer ID decoder failed.");
    std::cout << "DECODE=" << prim_id << "," << instance_id << ","
              << prim_path << "," << instancer_path << ","
              << instance_index << ",contexts=" << context.size() << "\n";
    for (const auto& entry : context)
    {
        std::cout << "CONTEXT=" << entry.first << "," << entry.second << "\n";
    }
    Require(prim_path.GetString() == expected_path, "Decoded path mismatch.");
    if (expected_instance >= 0)
    {
        Require(
            instance_index == expected_instance &&
                instancer_path == SdfPath("/World/Instances") &&
                context.size() == 1 &&
                context.front().first == SdfPath("/World/Instances") &&
                context.front().second == expected_instance,
            "Point instances do not have distinct canonical decoded identity.");
    }
}

void Run(const char* plugin_path, const char* stage_path)
{
    StormAovWglContext context;
    Require(context.Create(), "Could not create a hidden WGL context.");
    Require(GarchGLApiLoad(), "Could not load the current GL API.");
    StormAovWglContext::PrintDriver();
    GlfContextCaps::InitInstance();
    PlugRegistry::GetInstance().RegisterPlugins(plugin_path);
    const UsdStageRefPtr stage = UsdStage::Open(stage_path);
    Require(static_cast<bool>(stage), "Could not open the planar fixture.");
    UsdImagingGLEngine::Parameters engine_parameters;
    engine_parameters.rendererPluginId = TfToken("HdStormRendererPlugin");
    UsdImagingGLEngine engine(engine_parameters);
    Require(engine.GetGPUEnabled(), "Storm GPU execution is unavailable.");
    engine.SetEnablePresentation(false);
    std::cout << "HGI=" << engine.GetRendererHgiDisplayName() << "\n";
    for (const TfToken& name : engine.GetRendererAovs())
    {
        std::cout << "SDK_CANDIDATE_AOV=" << name << "\n";
    }

    std::array<Output, 5> outputs{
        Output{HdAovTokens->color},
        Output{HdAovTokens->depth},
        Output{HdAovTokens->primId},
        Output{HdAovTokens->instanceId},
        Output{HdAovTokens->elementId}};
    TfTokenVector names;
    for (const Output& output : outputs)
    {
        names.push_back(output.name);
    }
    Require(engine.SetRendererAovs(names), "SetRendererAovs refused the outputs.");

    // Orthographic window [-4,4] x [-4,4], near 1, far 11; eye looks down -Z.
    const GfMatrix4d projection(
        0.25, 0, 0, 0,
        0, 0.25, 0, 0,
        0, 0, -0.2, 0,
        0, 0, -1.2, 1);
    engine.SetCameraState(GfMatrix4d(1), projection);
    engine.SetRenderBufferSize(GfVec2i(Width, Height));
    engine.SetRenderViewport(GfVec4d(0, 0, Width, Height));
    UsdImagingGLRenderParams parameters;
    parameters.frame = UsdTimeCode(0);
    parameters.showRender = true;
    parameters.enableLighting = false;
    parameters.highlight = false;
    TfErrorMark mark;
    bool converged = false;
    for (uint32_t iteration = 0; iteration < 32; ++iteration)
    {
        engine.Render(stage->GetPseudoRoot(), parameters);
        if (engine.IsConverged())
        {
            converged = true;
            break;
        }
    }
    Require(converged, "The bounded render iteration limit was exhausted.");
    Require(mark.IsClean(), "Storm reported a render error.");
    glFinish();

    uint64_t byte_count = 0;
    for (Output& output : outputs)
    {
        output.buffer = engine.GetAovRenderBuffer(output.name);
        if (output.buffer == nullptr)
        {
            std::cout << "AOV=" << output.name << ",ABSENT\n";
            continue;
        }
        const HdFormat format = output.buffer->GetFormat();
        output.bytes_per_pixel = format == HdFormatFloat16Vec4 ? 8u :
            (format == HdFormatFloat32 || format == HdFormatInt32 ? 4u : 0u);
        Require(
            output.buffer->GetWidth() == Width &&
                output.buffer->GetHeight() == Height &&
                output.buffer->GetDepth() == 1 &&
                output.bytes_per_pixel != 0,
            "AOV descriptor refused before map or allocation.");
        byte_count += static_cast<uint64_t>(Width) * Height *
            output.bytes_per_pixel;
        std::cout << "AOV=" << output.name << ",format=" << format
                  << ",width=" << output.buffer->GetWidth()
                  << ",height=" << output.buffer->GetHeight()
                  << ",depth=" << output.buffer->GetDepth()
                  << ",multisampled=" << output.buffer->IsMultiSampled() << "\n";
    }
    Require(byte_count * 3 <= MaxBytes, "Readback scratch exceeds the fixed budget.");
    for (Output& output : outputs)
    {
        if (output.buffer == nullptr)
        {
            continue;
        }
        output.data.resize(
            static_cast<size_t>(Width) * Height * output.bytes_per_pixel);
        {
            BufferMap map(*output.buffer);
            Require(map.Data() != nullptr, "AOV Map returned no data.");
            std::memcpy(output.data.data(), map.Data(), output.data.size());
        }
        Require(!output.buffer->IsMapped(), "AOV remained mapped after RAII cleanup.");
    }
    Require(!outputs[0].data.empty(), "Color output is absent.");
    Require(!outputs[1].data.empty(), "Depth output is absent.");
    Require(!outputs[2].data.empty(), "Prim ID output is absent.");
    Require(!outputs[3].data.empty(), "Instance ID output is absent.");
    const float near_depth = Pixel<float>(outputs[1], 16, 32);
    const float far_depth = Pixel<float>(outputs[1], 48, 32);
    const float background_depth = Pixel<float>(outputs[1], 0, 0);
    std::cout << "PLANAR_DEPTH=" << near_depth << "," << far_depth << ","
              << background_depth << "\n";
    Require(
        std::abs(near_depth - 0.2f) < 0.00001f &&
            std::abs(far_depth - 0.6f) < 0.00001f &&
            background_depth == 1.0f,
        "Depth is not the literal expected normalized OpenGL window depth.");
    Require(
        Pixel<int32_t>(outputs[2], 0, 0) == -1 &&
            Pixel<int32_t>(outputs[3], 0, 0) == -1,
        "Background integer IDs must remain -1, not zero.");
    VerifyDecoded(engine, outputs[2], outputs[3], 16, 32, "/World/Near", -1);
    VerifyDecoded(engine, outputs[2], outputs[3], 48, 32, "/World/Far", -1);
    VerifyDecoded(
        engine, outputs[2], outputs[3], 24, 48,
        "/World/Instances/Prototype", 0);
    VerifyDecoded(
        engine, outputs[2], outputs[3], 40, 48,
        "/World/Instances/Prototype", 1);
    Require(mark.IsClean(), "Storm reported a readback or identity decode error.");
    std::cout << "SDK_AOV_PLANAR_AND_INSTANCE_PROOF=passed\n";
}
}

int main(int argc, char** argv)
{
    std::cout << std::unitbuf;
    if (argc != 3)
    {
        std::cerr << "Usage: probe <plugin-path> <planar-stage>\n";
        return 2;
    }
    try
    {
        Run(argv[1], argv[2]);
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << "AOV_PROOF_FAILED=" << error.what() << "\n";
        return 1;
    }
}
