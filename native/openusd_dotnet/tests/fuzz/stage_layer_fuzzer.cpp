// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_dotnet.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <string>

namespace
{
constexpr size_t MaxInputSize = 1024U * 1024U;

const char* ReadEnvironment(const char* name, std::string* storage)
{
#if defined(_WIN32)
    char* value = nullptr;
    size_t length = 0;
    if (_dupenv_s(&value, &length, name) != 0 || value == nullptr)
    {
        return nullptr;
    }
    storage->assign(value);
    std::free(value);
    return storage->c_str();
#else
    static_cast<void>(storage);
    return std::getenv(name);
#endif
}

uint64_t HashInput(const uint8_t* data, size_t size) noexcept
{
    uint64_t hash = UINT64_C(14695981039346656037);
    for (size_t index = 0; index < size; ++index)
    {
        hash ^= data[index];
        hash *= UINT64_C(1099511628211);
    }
    return hash;
}

class InputFile final
{
public:
    InputFile(const uint8_t* data, size_t size)
    {
        std::string configuredRootStorage;
        const char* configuredRoot =
            ReadEnvironment("OPENUSD_FUZZ_TEMP_DIR", &configuredRootStorage);
        const std::filesystem::path root =
            configuredRoot != nullptr && configuredRoot[0] != '\0'
            ? std::filesystem::path(configuredRoot)
            : std::filesystem::temp_directory_path() / "openusd-dotnet-fuzz";
        std::error_code error;
        std::filesystem::create_directories(root, error);
        if (error)
        {
            return;
        }

        std::array<char, 32> name{};
        std::snprintf(
            name.data(),
            name.size(),
            "stage-%016llx.usda",
            static_cast<unsigned long long>(HashInput(data, size)));
        _path = root / name.data();

        std::ofstream stream(_path, std::ios::binary | std::ios::trunc);
        if (!stream)
        {
            _path.clear();
            return;
        }
        if (size != 0)
        {
            stream.write(reinterpret_cast<const char*>(data), static_cast<std::streamsize>(size));
        }
        if (!stream)
        {
            stream.close();
            std::filesystem::remove(_path, error);
            _path.clear();
        }
    }

    ~InputFile()
    {
        if (!_path.empty())
        {
            std::error_code error;
            std::filesystem::remove(_path, error);
        }
    }

    InputFile(const InputFile&) = delete;
    InputFile& operator=(const InputFile&) = delete;

    bool IsValid() const noexcept
    {
        return !_path.empty();
    }

    std::string String() const
    {
        return _path.string();
    }

private:
    std::filesystem::path _path;
};

bool RegisterPlugins()
{
    static const bool registered = []()
    {
        std::string pluginPathStorage;
        const char* pluginPath =
            ReadEnvironment("OPENUSD_FUZZ_PLUGIN_PATH", &pluginPathStorage);
        if (pluginPath == nullptr || pluginPath[0] == '\0')
        {
            std::fprintf(stderr, "OPENUSD_FUZZ_PLUGIN_PATH is required.\n");
            return false;
        }

        std::array<char, 1024> errorText{};
        openusd_error_buffer error{errorText.data(), errorText.size(), 0};
        size_t pluginCount = 0;
        if (openusd_register_plugins(pluginPath, &pluginCount, &error) !=
            OPENUSD_STATUS_OK)
        {
            std::fprintf(
                stderr,
                "OpenUSD plugin registration failed: %s\n",
                errorText.data());
            return false;
        }
        return true;
    }();
    return registered;
}

bool RequireSuccessfulParse()
{
    std::string valueStorage;
    const char* value =
        ReadEnvironment("OPENUSD_FUZZ_REQUIRE_PARSE", &valueStorage);
    return value != nullptr && value[0] == '1' && value[1] == '\0';
}

void ExerciseParsedStage(openusd_stage* stage, openusd_error_buffer* error)
{
    std::array<char, 4096> text{};
    size_t required = 0;
    openusd_stage_get_root_layer_identifier(stage, text.data(), text.size(), &required, error);
    openusd_stage_get_default_prim_path(stage, text.data(), text.size(), &required, error);

    openusd_layer* layer = nullptr;
    if (openusd_stage_get_root_layer(stage, &layer, error) == OPENUSD_STATUS_OK &&
        layer != nullptr)
    {
        openusd_layer_get_identifier(layer, text.data(), text.size(), &required, error);
    }
    openusd_layer_release(layer);

    openusd_string_list* list = nullptr;
    openusd_string_list_view view{
        static_cast<uint32_t>(sizeof(openusd_string_list_view)),
        nullptr,
        0,
        nullptr,
        0,
        0};
    openusd_stage_get_prim_paths(stage, &list, &view, error);
    openusd_string_list_release(list);

    list = nullptr;
    view = {
        static_cast<uint32_t>(sizeof(openusd_string_list_view)),
        nullptr,
        0,
        nullptr,
        0,
        0};
    openusd_stage_get_layer_stack_identifiers(stage, &list, &view, error);
    openusd_string_list_release(list);
}
}

extern "C" int LLVMFuzzerTestOneInput(const uint8_t* data, size_t size)
{
    if (data == nullptr || size > MaxInputSize)
    {
        return 0;
    }

    if (!RegisterPlugins())
    {
        std::abort();
    }
    const InputFile input(data, size);
    if (!input.IsValid())
    {
        return 0;
    }

    std::array<char, 4096> errorText{};
    openusd_error_buffer error{errorText.data(), errorText.size(), 0};
    openusd_stage* stage = nullptr;
    const std::string path = input.String();
    const openusd_status status = openusd_stage_open(path.c_str(), &stage, &error);
    if (status != OPENUSD_STATUS_OK)
    {
        if (stage != nullptr)
        {
            openusd_stage_release(stage);
            std::abort();
        }
        if (RequireSuccessfulParse())
        {
            std::abort();
        }
        return 0;
    }
    if (stage == nullptr)
    {
        std::abort();
    }
    ExerciseParsedStage(stage, &error);
    openusd_stage_release(stage);
    return 0;
}
