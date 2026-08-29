// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_dotnet.h"

#include <OpenColorIO/OpenColorIO.h>

#include <algorithm>
#include <cmath>
#include <cstring>
#include <memory>
#include <limits>
#include <string>
#include <vector>

namespace OCIO = OCIO_NAMESPACE;

namespace
{

// Half-precision float IEEE 754 to single-precision conversion.
inline float HalfToFloat(uint16_t h)
{
    uint32_t sign = (static_cast<uint32_t>(h) & 0x8000u) << 16;
    int32_t exponent = static_cast<int32_t>((h >> 10) & 0x1Fu);
    uint32_t mantissa = h & 0x03FFu;

    if (exponent == 0)
    {
        if (mantissa == 0)
        {
            uint32_t bits = sign;
            float result;
            std::memcpy(&result, &bits, sizeof(result));
            return result;
        }
        // Subnormal: normalize.
        exponent = 1;
        while ((mantissa & 0x0400u) == 0)
        {
            mantissa <<= 1;
            exponent--;
        }
        mantissa &= 0x03FFu;
        exponent = exponent + (127 - 15);
    }
    else if (exponent == 31)
    {
        // Inf or NaN: preserve.
        exponent = 255;
    }
    else
    {
        exponent = exponent + (127 - 15);
    }

    uint32_t bits =
        sign | (static_cast<uint32_t>(exponent) << 23) | (mantissa << 13);
    float result;
    std::memcpy(&result, &bits, sizeof(result));
    return result;
}

inline void WriteError(openusd_error_buffer* error, const char* message)
{
    if (error == nullptr)
    {
        return;
    }
    size_t length = std::strlen(message);
    error->required = length + 1;
    if (error->data == nullptr || error->capacity == 0)
    {
        return;
    }
    size_t copy = std::min(length, error->capacity - 1);
    std::memcpy(error->data, message, copy);
    error->data[copy] = '\0';
}

inline void ClearError(openusd_error_buffer* error)
{
    if (error == nullptr)
    {
        return;
    }
    error->required = 0;
    if (error->data != nullptr && error->capacity != 0)
    {
        error->data[0] = '\0';
    }
}

}  // namespace

struct openusd_ocio_processor
{
    OCIO::ConstCPUProcessorRcPtr cpuProcessor;
};

extern "C"
{

OPENUSD_DOTNET_API openusd_status openusd_ocio_processor_create(
    const char* config_path,
    const char* source_color_space,
    const char* display,
    const char* view,
    const char* looks,
    openusd_ocio_processor** processor,
    openusd_error_buffer* error)
{
    ClearError(error);
    if (processor == nullptr)
    {
        WriteError(error, "processor output pointer must not be null");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    *processor = nullptr;

    if (config_path == nullptr || config_path[0] == '\0')
    {
        WriteError(error, "config_path must not be null or empty");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (source_color_space == nullptr || source_color_space[0] == '\0')
    {
        WriteError(error, "source_color_space must not be null or empty");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    try
    {
        OCIO::ConstConfigRcPtr config = OCIO::Config::CreateFromFile(config_path);

        const char* effectiveDisplay = (display != nullptr && display[0] != '\0')
            ? display
            : config->getDefaultDisplay();
        const char* effectiveView = (view != nullptr && view[0] != '\0')
            ? view
            : config->getDefaultView(effectiveDisplay);

        auto dvt = OCIO::DisplayViewTransform::Create();
        dvt->setSrc(source_color_space);
        dvt->setDisplay(effectiveDisplay);
        dvt->setView(effectiveView);

        OCIO::ConstProcessorRcPtr proc;
        if (looks != nullptr && looks[0] != '\0')
        {
            // Compose looks with the display/view transform via a LookTransform
            // wrapping the display/view pipeline.
            auto lt = OCIO::LookTransform::Create();
            lt->setSrc(source_color_space);
            lt->setDst(source_color_space);
            lt->setLooks(looks);
            lt->setSkipColorSpaceConversion(true);

            auto gt = OCIO::GroupTransform::Create();
            gt->appendTransform(lt);
            gt->appendTransform(dvt);
            proc = config->getProcessor(gt);
        }
        else
        {
            proc = config->getProcessor(dvt);
        }

        OCIO::ConstCPUProcessorRcPtr cpuProc =
            proc->getOptimizedCPUProcessor(OCIO::BIT_DEPTH_F32, OCIO::BIT_DEPTH_UINT8,
                                           OCIO::OPTIMIZATION_DEFAULT);

        auto* result = new openusd_ocio_processor();
        result->cpuProcessor = cpuProc;
        *processor = result;
        return OPENUSD_STATUS_OK;
    }
    catch (const OCIO::Exception& ex)
    {
        WriteError(error, ex.what());
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    catch (const std::exception& ex)
    {
        WriteError(error, ex.what());
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
}

OPENUSD_DOTNET_API openusd_status openusd_ocio_processor_apply_rgba16f_to_rgba8(
    const openusd_ocio_processor* processor,
    const uint8_t* source,
    size_t source_size,
    uint32_t width,
    uint32_t height,
    float exposure,
    uint8_t* destination,
    size_t destination_size,
    openusd_error_buffer* error)
{
    ClearError(error);
    if (processor == nullptr)
    {
        WriteError(error, "processor must not be null");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (source == nullptr)
    {
        WriteError(error, "source must not be null");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (destination == nullptr)
    {
        WriteError(error, "destination must not be null");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (!std::isfinite(exposure))
    {
        WriteError(error, "exposure must be finite");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (width == 0 || height == 0)
    {
        WriteError(error, "width and height must be positive");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    constexpr size_t ChannelsPerPixel = 4;
    constexpr size_t SourceBytesPerPixel = ChannelsPerPixel * sizeof(uint16_t);
    constexpr size_t DestinationBytesPerPixel = ChannelsPerPixel * sizeof(uint8_t);
    const size_t widthValue = static_cast<size_t>(width);
    const size_t heightValue = static_cast<size_t>(height);
    if (widthValue > static_cast<size_t>(std::numeric_limits<long>::max()) ||
        heightValue > static_cast<size_t>(std::numeric_limits<long>::max()))
    {
        WriteError(error, "width and height must fit the OpenColorIO image descriptor");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (heightValue > std::numeric_limits<size_t>::max() / widthValue)
    {
        WriteError(error, "width * height overflows size_t");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    const size_t pixelCount = widthValue * heightValue;
    if (pixelCount > std::numeric_limits<size_t>::max() / SourceBytesPerPixel ||
        pixelCount > std::numeric_limits<size_t>::max() / DestinationBytesPerPixel ||
        pixelCount > std::numeric_limits<size_t>::max() /
            (ChannelsPerPixel * sizeof(float)))
    {
        WriteError(error, "image byte count overflows size_t");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    const size_t expectedSourceSize = pixelCount * SourceBytesPerPixel;
    const size_t expectedDestSize = pixelCount * DestinationBytesPerPixel;

    if (source_size != expectedSourceSize)
    {
        WriteError(error, "source_size does not match width * height * 8");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (destination_size != expectedDestSize)
    {
        WriteError(error, "destination_size does not match width * height * 4");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    try
    {
        const float exposureScale = std::pow(2.0f, exposure);
        if (!std::isfinite(exposureScale))
        {
            WriteError(error, "computed exposure scale is not finite");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        // Convert half RGBA to float RGBA scratch, applying exposure to RGB only.
        std::vector<float> scratch(pixelCount * ChannelsPerPixel);

        for (size_t i = 0; i < pixelCount; ++i)
        {
            size_t si = i * ChannelsPerPixel;
            uint16_t halfChannels[ChannelsPerPixel];
            std::memcpy(
                halfChannels,
                source + (i * SourceBytesPerPixel),
                SourceBytesPerPixel);
            float r = HalfToFloat(halfChannels[0]) * exposureScale;
            float g = HalfToFloat(halfChannels[1]) * exposureScale;
            float b = HalfToFloat(halfChannels[2]) * exposureScale;
            float a = HalfToFloat(halfChannels[3]);
            if (!std::isfinite(r) || !std::isfinite(g) ||
                !std::isfinite(b) || !std::isfinite(a))
            {
                WriteError(error, "source contains a non-finite channel");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            scratch[si] = r;
            scratch[si + 1] = g;
            scratch[si + 2] = b;
            scratch[si + 3] = a;
        }

        // Apply OCIO transform: float RGBA in, uint8 RGBA out.
        // The processor was created with F32->UINT8 bit depths, so the
        // destination PackedImageDesc is UINT8.
        OCIO::PackedImageDesc srcImg(
            scratch.data(),
            static_cast<long>(width),
            static_cast<long>(height),
            4,
            OCIO::BIT_DEPTH_F32,
            sizeof(float),
            sizeof(float) * 4,
            sizeof(float) * 4 * static_cast<long>(width));

        OCIO::PackedImageDesc dstImg(
            destination,
            static_cast<long>(width),
            static_cast<long>(height),
            4,
            OCIO::BIT_DEPTH_UINT8,
            sizeof(uint8_t),
            sizeof(uint8_t) * 4,
            sizeof(uint8_t) * 4 * static_cast<long>(width));

        processor->cpuProcessor->apply(srcImg, dstImg);

        return OPENUSD_STATUS_OK;
    }
    catch (const OCIO::Exception& ex)
    {
        WriteError(error, ex.what());
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    catch (const std::exception& ex)
    {
        WriteError(error, ex.what());
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
}

OPENUSD_DOTNET_API void openusd_ocio_processor_release(
    openusd_ocio_processor* processor)
{
    delete processor;
}

}  // extern "C"
