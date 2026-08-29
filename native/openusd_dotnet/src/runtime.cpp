// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

namespace
{
template <typename T>
T ReadImageComponent(const uint8_t* source)
{
    T value{};
    std::memcpy(&value, source, sizeof(value));
    return value;
}

float ConvertImageComponent(const uint8_t* source, HioType type)
{
    switch (type)
    {
        case HioTypeUnsignedByte:
        case HioTypeUnsignedByteSRGB:
            return static_cast<float>(*source) / 255.0f;
        case HioTypeSignedByte:
            return std::max(
                static_cast<float>(ReadImageComponent<int8_t>(source)) / 127.0f,
                -1.0f);
        case HioTypeUnsignedShort:
            return static_cast<float>(ReadImageComponent<uint16_t>(source));
        case HioTypeSignedShort:
            return static_cast<float>(ReadImageComponent<int16_t>(source));
        case HioTypeUnsignedInt:
            return static_cast<float>(ReadImageComponent<uint32_t>(source));
        case HioTypeInt:
            return static_cast<float>(ReadImageComponent<int32_t>(source));
        case HioTypeHalfFloat:
        {
            GfHalf value;
            value.setBits(ReadImageComponent<uint16_t>(source));
            return static_cast<float>(value);
        }
        case HioTypeFloat:
            return ReadImageComponent<float>(source);
        case HioTypeDouble:
            return static_cast<float>(ReadImageComponent<double>(source));
        default:
            return std::numeric_limits<float>::quiet_NaN();
    }
}

float SrgbToLinear(float value)
{
    return value <= 0.04045f
        ? value / 12.92f
        : std::pow((value + 0.055f) / 1.055f, 2.4f);
}
}

uint32_t openusd_get_abi_version(void)
{
    return DataAbiVersion;
}

uint64_t openusd_get_capabilities(void)
{
    return DataCapabilities;
}

openusd_status openusd_get_version(
    char* buffer,
    size_t capacity,
    size_t* required)
{
    // OUTER_ABI_GUARD
    return Guard(nullptr, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiStringOutput(buffer, capacity);
        ResetAbiOutput(required);
        const uint32_t major = PXR_VERSION / 100;
        const uint32_t minor = PXR_VERSION % 100;
        char version[16];
        const int length = std::snprintf(version, sizeof(version), "%u.%02u", major, minor);
        if (length < 0 || static_cast<size_t>(length) >= sizeof(version))
        {
            return OPENUSD_STATUS_NATIVE_ERROR;
        }

        if (required == nullptr)
        {
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        *required = static_cast<size_t>(length) + 1;
        if (buffer == nullptr || capacity < *required)
        {
            return OPENUSD_STATUS_BUFFER_TOO_SMALL;
        }

        std::memcpy(buffer, version, *required);
        return OPENUSD_STATUS_OK;

    });
}

openusd_status openusd_register_plugins(
    const char* path,
    size_t* plugin_count,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        // ABI_OUTPUT_INITIALIZATION
        ResetAbiOutput(plugin_count);
        if (path == nullptr || plugin_count == nullptr)
        {
            WriteError(error, "Plugin path and count are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            const PlugPluginPtrVector plugins = PlugRegistry::GetInstance().RegisterPlugins(path);
            if (!mark.IsClean())
            {
                WriteError(error, ConsumeErrors(mark));
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            *plugin_count = plugins.size();
            return OPENUSD_STATUS_OK;
        });

    });
}

openusd_status openusd_decode_image_rgba8(
    const char* asset_path,
    uint32_t convert_srgb_to_linear,
    openusd_image_info* info,
    uint8_t* rgba,
    size_t rgba_size,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        if (asset_path == nullptr || info == nullptr ||
            info->struct_size != sizeof(openusd_image_info) ||
            info->version != OPENUSD_IMAGE_INFO_VERSION)
        {
            WriteError(error, "A valid image asset path and image-info output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        // ABI_OUTPUT_INITIALIZATION
        // The caller supplies struct_size and version, but the decoded extent is
        // an output and must be defined on every failure path, not only on
        // success, so a caller that ignores the status never reads a stale size.
        info->width = 0;
        info->height = 0;

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            HioImageSharedPtr image = HioImage::OpenForReading(
                asset_path,
                0,
                0,
                HioImage::SourceColorSpace::Raw);
            if (!image)
            {
                WriteError(error, std::string("Could not open image: ") + asset_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (!mark.IsClean())
            {
                WriteError(error, ConsumeErrors(mark));
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            const int width = image->GetWidth();
            const int height = image->GetHeight();
            if (width <= 0 || height <= 0)
            {
                WriteError(error, "Image dimensions are invalid.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            info->width = static_cast<uint32_t>(width);
            info->height = static_cast<uint32_t>(height);
            const HioFormat source_format = image->GetFormat();
            if (source_format < HioFormatUNorm8 ||
                source_format >= HioFormatCount ||
                HioIsCompressed(source_format))
            {
                WriteError(
                    error,
                    "Compressed or invalid Hio images are not supported; transcode "
                    "to an uncompressed format first.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            const HioType source_type = HioGetHioType(source_format);
            const int source_components = HioGetComponentCount(source_format);
            if ((source_type != HioTypeUnsignedByte &&
                 source_type != HioTypeUnsignedByteSRGB) ||
                source_components < 1 ||
                source_components > 4)
            {
                WriteError(error, "Only one- through four-channel 8-bit images are supported.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }

            const size_t width_size = static_cast<size_t>(width);
            const size_t height_size = static_cast<size_t>(height);
            if (width_size > std::numeric_limits<size_t>::max() / height_size)
            {
                WriteError(error, "Image dimensions exceed the supported allocation size.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            const size_t pixel_count = width_size * height_size;
            if (pixel_count > std::numeric_limits<size_t>::max() / 4u)
            {
                WriteError(error, "Decoded RGBA image exceeds the supported allocation size.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            const size_t required = pixel_count * 4u;
            if (rgba == nullptr || rgba_size < required)
            {
                return OPENUSD_STATUS_BUFFER_TOO_SMALL;
            }

            std::vector<uint8_t> source(pixel_count * static_cast<size_t>(source_components));
            HioImage::StorageSpec storage;
            storage.width = width;
            storage.height = height;
            storage.depth = 1;
            storage.format = source_format;
            storage.flipped = false;
            storage.data = source.data();
            if (!image->Read(storage))
            {
                WriteError(error, std::string("Could not read image: ") + asset_path);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }

            for (size_t pixel = 0; pixel < pixel_count; ++pixel)
            {
                const uint8_t* input =
                    source.data() + (pixel * static_cast<size_t>(source_components));
                uint8_t* output = rgba + (pixel * 4u);
                if (source_components == 1)
                {
                    output[0] = input[0];
                    output[1] = input[0];
                    output[2] = input[0];
                    output[3] = 255;
                }
                else if (source_components == 2)
                {
                    output[0] = input[0];
                    output[1] = input[0];
                    output[2] = input[0];
                    output[3] = input[1];
                }
                else
                {
                    output[0] = input[0];
                    output[1] = input[1];
                    output[2] = input[2];
                    output[3] = source_components == 4 ? input[3] : 255;
                }
            }
            if (convert_srgb_to_linear != 0)
            {
                for (size_t index = 0; index < required; index += 4)
                {
                    for (size_t component = 0; component < 3; ++component)
                    {
                        const double srgb = static_cast<double>(rgba[index + component]) / 255.0;
                        const double linear = srgb <= 0.04045
                            ? srgb / 12.92
                            : std::pow((srgb + 0.055) / 1.055, 2.4);
                        const long rounded = std::lround(std::max(0.0, std::min(1.0, linear)) * 255.0);
                        rgba[index + component] = static_cast<uint8_t>(rounded);
                    }
                }
            }
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_decode_image_rgba32f(
    const char* asset_path,
    uint32_t convert_srgb_to_linear,
    openusd_image_info* info,
    float* rgba,
    size_t rgba_size,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        if (asset_path == nullptr || info == nullptr ||
            info->struct_size != sizeof(openusd_image_info) ||
            info->version != OPENUSD_IMAGE_INFO_VERSION)
        {
            WriteError(error, "A valid image asset path and image-info output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        // ABI_OUTPUT_INITIALIZATION
        info->width = 0;
        info->height = 0;

        return Guard(error, [&]()
        {
            TfErrorMark mark;
            HioImageSharedPtr image = HioImage::OpenForReading(
                asset_path,
                0,
                0,
                HioImage::SourceColorSpace::Raw);
            if (!image)
            {
                WriteError(error, std::string("Could not open image: ") + asset_path);
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (!mark.IsClean())
            {
                WriteError(error, ConsumeErrors(mark));
                return OPENUSD_STATUS_NATIVE_ERROR;
            }

            const int width = image->GetWidth();
            const int height = image->GetHeight();
            if (width <= 0 || height <= 0)
            {
                WriteError(error, "Image dimensions are invalid.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            info->width = static_cast<uint32_t>(width);
            info->height = static_cast<uint32_t>(height);

            const HioFormat source_format = image->GetFormat();
            if (source_format < HioFormatUNorm8 || source_format >= HioFormatCount)
            {
                WriteError(error, "The Hio image format is unsupported.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            if (HioIsCompressed(source_format))
            {
                WriteError(
                    error,
                    "Compressed Hio images are not supported; transcode to an "
                    "uncompressed format first.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            const HioType source_type = HioGetHioType(source_format);
            const int source_components = HioGetComponentCount(source_format);
            const size_t source_component_size = HioGetDataSizeOfType(source_type);
            const size_t source_pixel_size = HioGetDataSizeOfFormat(source_format);
            if (source_type < HioTypeUnsignedByte ||
                source_type >= HioTypeCount ||
                source_components < 1 ||
                source_components > 4 ||
                source_component_size == 0 ||
                source_pixel_size !=
                    source_component_size * static_cast<size_t>(source_components))
            {
                WriteError(error, "The Hio image format is unsupported.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }

            const size_t width_size = static_cast<size_t>(width);
            const size_t height_size = static_cast<size_t>(height);
            if (width_size > std::numeric_limits<size_t>::max() / height_size)
            {
                WriteError(error, "Image dimensions exceed the supported allocation size.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            const size_t pixel_count = width_size * height_size;
            constexpr size_t output_pixel_size = sizeof(float) * 4u;
            if (pixel_count > std::numeric_limits<size_t>::max() / source_pixel_size ||
                pixel_count > std::numeric_limits<size_t>::max() / output_pixel_size)
            {
                WriteError(error, "Decoded RGBA image exceeds the supported allocation size.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            const size_t source_size = pixel_count * source_pixel_size;
            const size_t required = pixel_count * output_pixel_size;
            if (rgba == nullptr || rgba_size < required)
            {
                return OPENUSD_STATUS_BUFFER_TOO_SMALL;
            }

            std::vector<uint8_t> source(source_size);
            HioImage::StorageSpec storage;
            storage.width = width;
            storage.height = height;
            storage.depth = 1;
            storage.format = source_format;
            storage.flipped = false;
            storage.data = source.data();
            if (!image->Read(storage))
            {
                WriteError(error, std::string("Could not read image: ") + asset_path);
                return OPENUSD_STATUS_NATIVE_ERROR;
            }

            for (size_t pixel = 0; pixel < pixel_count; ++pixel)
            {
                const uint8_t* input = source.data() + (pixel * source_pixel_size);
                float* output = rgba + (pixel * 4u);
                float channels[4]{0.0f, 0.0f, 0.0f, 1.0f};
                for (int component = 0; component < source_components; ++component)
                {
                    channels[component] = ConvertImageComponent(
                        input + (static_cast<size_t>(component) * source_component_size),
                        source_type);
                    if (!std::isfinite(channels[component]))
                    {
                        WriteError(error, "The decoded image contains a non-finite channel.");
                        return OPENUSD_STATUS_NATIVE_ERROR;
                    }
                }
                if (source_components == 1 || source_components == 2)
                {
                    output[0] = channels[0];
                    output[1] = channels[0];
                    output[2] = channels[0];
                    output[3] = source_components == 2 ? channels[1] : 1.0f;
                }
                else
                {
                    output[0] = channels[0];
                    output[1] = channels[1];
                    output[2] = channels[2];
                    output[3] = source_components == 4 ? channels[3] : 1.0f;
                }
                if (convert_srgb_to_linear != 0)
                {
                    output[0] = SrgbToLinear(output[0]);
                    output[1] = SrgbToLinear(output[1]);
                    output[2] = SrgbToLinear(output[2]);
                }
                if (!std::isfinite(output[0]) ||
                    !std::isfinite(output[1]) ||
                    !std::isfinite(output[2]))
                {
                    WriteError(error, "Image color conversion produced a non-finite channel.");
                    return OPENUSD_STATUS_NATIVE_ERROR;
                }
            }
            return OPENUSD_STATUS_OK;
        });
    });
}
