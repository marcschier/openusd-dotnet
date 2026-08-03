// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

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
            const size_t required =
                static_cast<size_t>(width) * static_cast<size_t>(height) * 4u;
            if (rgba == nullptr || rgba_size < required)
            {
                return OPENUSD_STATUS_BUFFER_TOO_SMALL;
            }

            HioImage::StorageSpec storage;
            storage.width = width;
            storage.height = height;
            storage.depth = 1;
            storage.format = HioFormatUNorm8Vec4;
            storage.flipped = false;
            storage.data = rgba;
            if (!image->Read(storage))
            {
                WriteError(error, std::string("Could not read image: ") + asset_path);
                return OPENUSD_STATUS_NATIVE_ERROR;
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
