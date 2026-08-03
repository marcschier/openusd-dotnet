#include "openusd_cesium.h"

#include <array>
#include <cmath>
#include <chrono>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <iostream>
#include <string>
#include <string_view>
#include <thread>
#include <vector>

namespace {

struct ProbeState {
    int requests = 0;
    int loadThreadCalls = 0;
    int mainThreadCalls = 0;
    int freeCalls = 0;
    int meshPrimitiveCalls = 0;
    int rootLoadCalls = 0;
    int childLoadCalls = 0;
    int gltfMeshCountInLoadResult = 0;
    int gltfContentCalls = 0;
    int externalContentCalls = 0;
    int emptyContentCalls = 0;
    bool sawDoubleTransform = false;
    bool sawExpectedMesh = false;
    bool sawRootSelection = false;
    bool sawChildSelection = false;
    bool failAsset = false;
    bool failRenderer = false;
};

void appendU32(std::vector<uint8_t>& bytes, uint32_t value) {
    bytes.push_back(static_cast<uint8_t>(value & 0xFF));
    bytes.push_back(static_cast<uint8_t>((value >> 8) & 0xFF));
    bytes.push_back(static_cast<uint8_t>((value >> 16) & 0xFF));
    bytes.push_back(static_cast<uint8_t>((value >> 24) & 0xFF));
}

void appendBytes(std::vector<uint8_t>& bytes, const void* value, size_t size) {
    const auto* first = static_cast<const uint8_t*>(value);
    bytes.insert(bytes.end(), first, first + size);
}

void appendF32(std::vector<uint8_t>& bytes, float value) {
    appendBytes(bytes, &value, sizeof(value));
}

void appendU16(std::vector<uint8_t>& bytes, uint16_t value) {
    appendBytes(bytes, &value, sizeof(value));
}

std::vector<uint8_t> makeBinary(float scale) {
    std::vector<uint8_t> binary;
    appendF32(binary, 0.0f); appendF32(binary, 0.0f); appendF32(binary, 0.0f);
    appendF32(binary, scale); appendF32(binary, 0.0f); appendF32(binary, 0.0f);
    appendF32(binary, 0.0f); appendF32(binary, scale); appendF32(binary, 0.0f);
    appendU16(binary, 0); appendU16(binary, 1); appendU16(binary, 2);
    while ((binary.size() % 4) != 0) {
        binary.push_back(0);
    }
    return binary;
}

std::string makeGltf(float scale) {
    const char* data = scale == 2.0f
        ? "AAAAAAAAAAAAAAAAAAAAQAAAAAAAAAAAAAAAAAAAAEAAAAAAAAABAAIAAAA="
        : "AAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAgD8AAAAAAAABAAIAAAA=";
    return std::string(R"({"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],)") +
        R"("nodes":[{"mesh":0}],"meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1}]}],)" +
        R"("buffers":[{"uri":"data:application/octet-stream;base64,)" + data + R"(","byteLength":44}],)" +
        R"("bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36,"target":34962},)" +
        R"({"buffer":0,"byteOffset":36,"byteLength":6,"target":34963}],)" +
        R"("accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3",)" +
        R"("min":[0,0,0],"max":[1,1,0]},{"bufferView":1,"componentType":5123,"count":3,"type":"SCALAR"}]})";
}

std::vector<uint8_t> makeGlb(float scale) {
    static_cast<void>(scale);
    static constexpr std::array<uint8_t, 1188> box = {{
        0x67, 0x6C, 0x54, 0x46, 0x02, 0x00, 0x00, 0x00, 0xA4, 0x04, 0x00, 0x00, 0x80, 0x03, 0x00, 0x00,
        0x4A, 0x53, 0x4F, 0x4E, 0x7B, 0x22, 0x61, 0x73, 0x73, 0x65, 0x74, 0x22, 0x3A, 0x7B, 0x22, 0x67,
        0x65, 0x6E, 0x65, 0x72, 0x61, 0x74, 0x6F, 0x72, 0x22, 0x3A, 0x22, 0x4B, 0x68, 0x72, 0x6F, 0x6E,
        0x6F, 0x73, 0x20, 0x67, 0x6C, 0x54, 0x46, 0x20, 0x42, 0x6C, 0x65, 0x6E, 0x64, 0x65, 0x72, 0x20,
        0x49, 0x2F, 0x4F, 0x20, 0x76, 0x31, 0x2E, 0x37, 0x2E, 0x33, 0x33, 0x22, 0x2C, 0x22, 0x76, 0x65,
        0x72, 0x73, 0x69, 0x6F, 0x6E, 0x22, 0x3A, 0x22, 0x32, 0x2E, 0x30, 0x22, 0x7D, 0x2C, 0x22, 0x73,
        0x63, 0x65, 0x6E, 0x65, 0x22, 0x3A, 0x30, 0x2C, 0x22, 0x73, 0x63, 0x65, 0x6E, 0x65, 0x73, 0x22,
        0x3A, 0x5B, 0x7B, 0x22, 0x6E, 0x61, 0x6D, 0x65, 0x22, 0x3A, 0x22, 0x53, 0x63, 0x65, 0x6E, 0x65,
        0x22, 0x2C, 0x22, 0x6E, 0x6F, 0x64, 0x65, 0x73, 0x22, 0x3A, 0x5B, 0x30, 0x5D, 0x7D, 0x5D, 0x2C,
        0x22, 0x6E, 0x6F, 0x64, 0x65, 0x73, 0x22, 0x3A, 0x5B, 0x7B, 0x22, 0x6D, 0x65, 0x73, 0x68, 0x22,
        0x3A, 0x30, 0x2C, 0x22, 0x6E, 0x61, 0x6D, 0x65, 0x22, 0x3A, 0x22, 0x43, 0x75, 0x62, 0x65, 0x22,
        0x7D, 0x5D, 0x2C, 0x22, 0x6D, 0x61, 0x74, 0x65, 0x72, 0x69, 0x61, 0x6C, 0x73, 0x22, 0x3A, 0x5B,
        0x7B, 0x22, 0x64, 0x6F, 0x75, 0x62, 0x6C, 0x65, 0x53, 0x69, 0x64, 0x65, 0x64, 0x22, 0x3A, 0x74,
        0x72, 0x75, 0x65, 0x2C, 0x22, 0x6E, 0x61, 0x6D, 0x65, 0x22, 0x3A, 0x22, 0x4D, 0x61, 0x74, 0x65,
        0x72, 0x69, 0x61, 0x6C, 0x22, 0x2C, 0x22, 0x70, 0x62, 0x72, 0x4D, 0x65, 0x74, 0x61, 0x6C, 0x6C,
        0x69, 0x63, 0x52, 0x6F, 0x75, 0x67, 0x68, 0x6E, 0x65, 0x73, 0x73, 0x22, 0x3A, 0x7B, 0x22, 0x62,
        0x61, 0x73, 0x65, 0x43, 0x6F, 0x6C, 0x6F, 0x72, 0x46, 0x61, 0x63, 0x74, 0x6F, 0x72, 0x22, 0x3A,
        0x5B, 0x30, 0x2E, 0x38, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x31, 0x31, 0x39, 0x32, 0x30, 0x39,
        0x32, 0x39, 0x2C, 0x30, 0x2E, 0x38, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x31, 0x31, 0x39, 0x32,
        0x30, 0x39, 0x32, 0x39, 0x2C, 0x30, 0x2E, 0x38, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x31, 0x31,
        0x39, 0x32, 0x30, 0x39, 0x32, 0x39, 0x2C, 0x31, 0x5D, 0x2C, 0x22, 0x6D, 0x65, 0x74, 0x61, 0x6C,
        0x6C, 0x69, 0x63, 0x46, 0x61, 0x63, 0x74, 0x6F, 0x72, 0x22, 0x3A, 0x30, 0x2C, 0x22, 0x72, 0x6F,
        0x75, 0x67, 0x68, 0x6E, 0x65, 0x73, 0x73, 0x46, 0x61, 0x63, 0x74, 0x6F, 0x72, 0x22, 0x3A, 0x30,
        0x2E, 0x34, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x35, 0x39, 0x36, 0x30, 0x34, 0x36, 0x34,
        0x35, 0x7D, 0x7D, 0x5D, 0x2C, 0x22, 0x6D, 0x65, 0x73, 0x68, 0x65, 0x73, 0x22, 0x3A, 0x5B, 0x7B,
        0x22, 0x6E, 0x61, 0x6D, 0x65, 0x22, 0x3A, 0x22, 0x43, 0x75, 0x62, 0x65, 0x22, 0x2C, 0x22, 0x70,
        0x72, 0x69, 0x6D, 0x69, 0x74, 0x69, 0x76, 0x65, 0x73, 0x22, 0x3A, 0x5B, 0x7B, 0x22, 0x61, 0x74,
        0x74, 0x72, 0x69, 0x62, 0x75, 0x74, 0x65, 0x73, 0x22, 0x3A, 0x7B, 0x22, 0x50, 0x4F, 0x53, 0x49,
        0x54, 0x49, 0x4F, 0x4E, 0x22, 0x3A, 0x30, 0x2C, 0x22, 0x4E, 0x4F, 0x52, 0x4D, 0x41, 0x4C, 0x22,
        0x3A, 0x31, 0x7D, 0x2C, 0x22, 0x69, 0x6E, 0x64, 0x69, 0x63, 0x65, 0x73, 0x22, 0x3A, 0x32, 0x2C,
        0x22, 0x6D, 0x61, 0x74, 0x65, 0x72, 0x69, 0x61, 0x6C, 0x22, 0x3A, 0x30, 0x7D, 0x5D, 0x7D, 0x5D,
        0x2C, 0x22, 0x61, 0x63, 0x63, 0x65, 0x73, 0x73, 0x6F, 0x72, 0x73, 0x22, 0x3A, 0x5B, 0x7B, 0x22,
        0x62, 0x75, 0x66, 0x66, 0x65, 0x72, 0x56, 0x69, 0x65, 0x77, 0x22, 0x3A, 0x30, 0x2C, 0x22, 0x63,
        0x6F, 0x6D, 0x70, 0x6F, 0x6E, 0x65, 0x6E, 0x74, 0x54, 0x79, 0x70, 0x65, 0x22, 0x3A, 0x35, 0x31,
        0x32, 0x36, 0x2C, 0x22, 0x63, 0x6F, 0x75, 0x6E, 0x74, 0x22, 0x3A, 0x38, 0x2C, 0x22, 0x6D, 0x61,
        0x78, 0x22, 0x3A, 0x5B, 0x31, 0x2C, 0x31, 0x2C, 0x31, 0x5D, 0x2C, 0x22, 0x6D, 0x69, 0x6E, 0x22,
        0x3A, 0x5B, 0x2D, 0x31, 0x2C, 0x2D, 0x31, 0x2C, 0x2D, 0x31, 0x5D, 0x2C, 0x22, 0x74, 0x79, 0x70,
        0x65, 0x22, 0x3A, 0x22, 0x56, 0x45, 0x43, 0x33, 0x22, 0x7D, 0x2C, 0x7B, 0x22, 0x62, 0x75, 0x66,
        0x66, 0x65, 0x72, 0x56, 0x69, 0x65, 0x77, 0x22, 0x3A, 0x31, 0x2C, 0x22, 0x63, 0x6F, 0x6D, 0x70,
        0x6F, 0x6E, 0x65, 0x6E, 0x74, 0x54, 0x79, 0x70, 0x65, 0x22, 0x3A, 0x35, 0x31, 0x32, 0x36, 0x2C,
        0x22, 0x63, 0x6F, 0x75, 0x6E, 0x74, 0x22, 0x3A, 0x38, 0x2C, 0x22, 0x74, 0x79, 0x70, 0x65, 0x22,
        0x3A, 0x22, 0x56, 0x45, 0x43, 0x33, 0x22, 0x7D, 0x2C, 0x7B, 0x22, 0x62, 0x75, 0x66, 0x66, 0x65,
        0x72, 0x56, 0x69, 0x65, 0x77, 0x22, 0x3A, 0x32, 0x2C, 0x22, 0x63, 0x6F, 0x6D, 0x70, 0x6F, 0x6E,
        0x65, 0x6E, 0x74, 0x54, 0x79, 0x70, 0x65, 0x22, 0x3A, 0x35, 0x31, 0x32, 0x33, 0x2C, 0x22, 0x63,
        0x6F, 0x75, 0x6E, 0x74, 0x22, 0x3A, 0x33, 0x36, 0x2C, 0x22, 0x74, 0x79, 0x70, 0x65, 0x22, 0x3A,
        0x22, 0x53, 0x43, 0x41, 0x4C, 0x41, 0x52, 0x22, 0x7D, 0x5D, 0x2C, 0x22, 0x62, 0x75, 0x66, 0x66,
        0x65, 0x72, 0x56, 0x69, 0x65, 0x77, 0x73, 0x22, 0x3A, 0x5B, 0x7B, 0x22, 0x62, 0x75, 0x66, 0x66,
        0x65, 0x72, 0x22, 0x3A, 0x30, 0x2C, 0x22, 0x62, 0x79, 0x74, 0x65, 0x4C, 0x65, 0x6E, 0x67, 0x74,
        0x68, 0x22, 0x3A, 0x39, 0x36, 0x2C, 0x22, 0x62, 0x79, 0x74, 0x65, 0x4F, 0x66, 0x66, 0x73, 0x65,
        0x74, 0x22, 0x3A, 0x30, 0x7D, 0x2C, 0x7B, 0x22, 0x62, 0x75, 0x66, 0x66, 0x65, 0x72, 0x22, 0x3A,
        0x30, 0x2C, 0x22, 0x62, 0x79, 0x74, 0x65, 0x4C, 0x65, 0x6E, 0x67, 0x74, 0x68, 0x22, 0x3A, 0x39,
        0x36, 0x2C, 0x22, 0x62, 0x79, 0x74, 0x65, 0x4F, 0x66, 0x66, 0x73, 0x65, 0x74, 0x22, 0x3A, 0x39,
        0x36, 0x7D, 0x2C, 0x7B, 0x22, 0x62, 0x75, 0x66, 0x66, 0x65, 0x72, 0x22, 0x3A, 0x30, 0x2C, 0x22,
        0x62, 0x79, 0x74, 0x65, 0x4C, 0x65, 0x6E, 0x67, 0x74, 0x68, 0x22, 0x3A, 0x37, 0x32, 0x2C, 0x22,
        0x62, 0x79, 0x74, 0x65, 0x4F, 0x66, 0x66, 0x73, 0x65, 0x74, 0x22, 0x3A, 0x31, 0x39, 0x32, 0x7D,
        0x5D, 0x2C, 0x22, 0x62, 0x75, 0x66, 0x66, 0x65, 0x72, 0x73, 0x22, 0x3A, 0x5B, 0x7B, 0x22, 0x62,
        0x79, 0x74, 0x65, 0x4C, 0x65, 0x6E, 0x67, 0x74, 0x68, 0x22, 0x3A, 0x32, 0x36, 0x34, 0x7D, 0x5D,
        0x7D, 0x20, 0x20, 0x20, 0x08, 0x01, 0x00, 0x00, 0x42, 0x49, 0x4E, 0x00, 0x00, 0x00, 0x80, 0x3F,
        0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x80, 0x3F,
        0x00, 0x00, 0x80, 0xBF, 0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x80, 0xBF, 0x00, 0x00, 0x80, 0x3F,
        0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x80, 0xBF, 0x00, 0x00, 0x80, 0xBF, 0x00, 0x00, 0x80, 0xBF,
        0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x80, 0xBF, 0x00, 0x00, 0x80, 0x3F,
        0x00, 0x00, 0x80, 0xBF, 0x00, 0x00, 0x80, 0xBF, 0x00, 0x00, 0x80, 0xBF, 0x00, 0x00, 0x80, 0x3F,
        0x00, 0x00, 0x80, 0xBF, 0x00, 0x00, 0x80, 0xBF, 0x00, 0x00, 0x80, 0xBF, 0x6C, 0x06, 0x51, 0x3F,
        0xE5, 0x07, 0xD1, 0x3E, 0xF7, 0x01, 0xD1, 0x3E, 0x1A, 0xAE, 0xAA, 0x3E, 0xD7, 0xA9, 0x2A, 0x3F,
        0xA5, 0xAA, 0x2A, 0xBF, 0x19, 0xAE, 0xAA, 0x3E, 0x6F, 0xA9, 0x2A, 0xBF, 0x0C, 0xAB, 0x2A, 0x3F,
        0x6D, 0x06, 0x51, 0x3F, 0xEE, 0x05, 0xD1, 0xBE, 0xEE, 0x03, 0xD1, 0xBE, 0x1A, 0xAE, 0xAA, 0xBE,
        0xD7, 0xA9, 0x2A, 0x3F, 0xA5, 0xAA, 0x2A, 0x3F, 0x6C, 0x06, 0x51, 0xBF, 0xF2, 0x03, 0xD1, 0x3E,
        0xEB, 0x05, 0xD1, 0xBE, 0x6C, 0x06, 0x51, 0xBF, 0xF0, 0x04, 0xD1, 0xBE, 0xF0, 0x04, 0xD1, 0x3E,
        0x1A, 0xAE, 0xAA, 0xBE, 0x3D, 0xAA, 0x2A, 0xBF, 0x3D, 0xAA, 0x2A, 0xBF, 0x04, 0x00, 0x02, 0x00,
        0x00, 0x00, 0x02, 0x00, 0x07, 0x00, 0x03, 0x00, 0x06, 0x00, 0x05, 0x00, 0x07, 0x00, 0x01, 0x00,
        0x07, 0x00, 0x05, 0x00, 0x00, 0x00, 0x03, 0x00, 0x01, 0x00, 0x04, 0x00, 0x01, 0x00, 0x05, 0x00,
        0x04, 0x00, 0x06, 0x00, 0x02, 0x00, 0x02, 0x00, 0x06, 0x00, 0x07, 0x00, 0x06, 0x00, 0x04, 0x00,
        0x05, 0x00, 0x01, 0x00, 0x03, 0x00, 0x07, 0x00, 0x00, 0x00, 0x02, 0x00, 0x03, 0x00, 0x04, 0x00,
        0x00, 0x00, 0x01, 0x00,
    }};
    return std::vector<uint8_t>(box.begin(), box.end());
}
std::vector<uint8_t> makeB3dm(float scale) {
    const std::string featureTable = R"({"BATCH_LENGTH":0})";
    std::vector<uint8_t> featureTableJson(featureTable.begin(), featureTable.end());
    while ((featureTableJson.size() % 8) != 0) {
        featureTableJson.push_back(0x20);
    }
    std::vector<uint8_t> glb = makeGlb(scale);
    std::vector<uint8_t> bytes;
    appendBytes(bytes, "b3dm", 4);
    appendU32(bytes, 1u);
    appendU32(bytes, static_cast<uint32_t>(28 + featureTableJson.size() + glb.size()));
    appendU32(bytes, static_cast<uint32_t>(featureTableJson.size()));
    appendU32(bytes, 0u);
    appendU32(bytes, 0u);
    appendU32(bytes, 0u);
    bytes.insert(bytes.end(), featureTableJson.begin(), featureTableJson.end());
    bytes.insert(bytes.end(), glb.begin(), glb.end());
    return bytes;
}

std::vector<uint8_t> copyBytes(const void* source, size_t size) {
    std::vector<uint8_t> result(size);
    std::memcpy(result.data(), source, size);
    return result;
}

bool hasDropLoadCallbackFailpoint() {
#if defined(_WIN32)
    char* value = nullptr;
    size_t valueSize = 0;
    const bool present =
        _dupenv_s(&value, &valueSize, "OPENUSD_CESIUM_PROBE_DROP_LOAD_CALLBACK") == 0 && value != nullptr;
    std::free(value);
    return present;
#else
    return std::getenv("OPENUSD_CESIUM_PROBE_DROP_LOAD_CALLBACK") != nullptr;
#endif
}

void freeData(void*, const uint8_t* data, size_t) {
    std::free(const_cast<uint8_t*>(data));
}

openusd_cesium_status assetRequest(
    void* userData,
    const char*,
    const char* url,
    const uint8_t*,
    size_t,
    openusd_cesium_asset_response* response,
    openusd_cesium_error_buffer*) {
    auto* state = static_cast<ProbeState*>(userData);
    ++state->requests;
    if (state->failAsset) {
        response->status_code = 404;
        response->content_type = "text/plain";
        return OPENUSD_CESIUM_STATUS_OK;
    }

    std::vector<uint8_t> payload;
    std::string_view urlView(url != nullptr ? url : "");
    if (urlView.find("tileset.json") != std::string_view::npos) {
        const std::string tileset = R"json({
          "asset": { "version": "1.0" },
          "geometricError": 2000,
          "root": {
            "boundingVolume": { "sphere": [0.0, 0.0, 0.0, 2000.0] },
            "geometricError": 2000,
            "refine": "REPLACE",
            "transform": [1,0,0,0, 0,1,0,0, 0,0,1,0, 6378137.123456789,0,0,1],
            "content": { "uri": "root.glb" },
            "children": [{
              "boundingVolume": { "sphere": [0.0, 0.0, 0.0, 1000.0] },
              "geometricError": 0,
              "content": { "uri": "tile.glb" }
            }]
          }
        })json";
        payload = copyBytes(tileset.data(), tileset.size());
        response->content_type = "application/json";
    } else if (urlView.find("root.glb") != std::string_view::npos) {
        payload = makeGlb(2.0f);
        response->content_type = "model/gltf-binary";
    } else if (urlView.find("tile.glb") != std::string_view::npos) {
        payload = makeGlb(1.0f);
        response->content_type = "model/gltf-binary";
    } else {
        response->status_code = 404;
        response->content_type = "text/plain";
        return OPENUSD_CESIUM_STATUS_OK;
    }

    auto* data = static_cast<uint8_t*>(std::malloc(payload.size()));
    if (data == nullptr) {
        return OPENUSD_CESIUM_STATUS_NATIVE_ERROR;
    }
    std::memcpy(data, payload.data(), payload.size());
    response->status_code = 200;
    response->data = data;
    response->data_size = payload.size();
    response->free_data = freeData;
    return OPENUSD_CESIUM_STATUS_OK;
}

void* prepareLoad(
    void* userData,
    const openusd_cesium_tile_load_result* loadResult,
    const openusd_cesium_matrix4d* transform,
    openusd_cesium_error_buffer*) {
    auto* state = static_cast<ProbeState*>(userData);
    ++state->loadThreadCalls;
    if (state->failRenderer) {
        return nullptr;
    }
    if (loadResult != nullptr && loadResult->content_kind == OPENUSD_CESIUM_TILE_CONTENT_GLTF_MODEL &&
        transform != nullptr && std::abs(transform->values[12] - 6378137.123456789) < 0.000001) {
        state->sawDoubleTransform = true;
    }
    if (loadResult != nullptr) {
        state->gltfMeshCountInLoadResult += static_cast<int>(loadResult->gltf_mesh_count);
        if (loadResult->content_kind == OPENUSD_CESIUM_TILE_CONTENT_GLTF_MODEL) {
            ++state->gltfContentCalls;
        } else if (loadResult->content_kind == OPENUSD_CESIUM_TILE_CONTENT_EXTERNAL_TILESET) {
            ++state->externalContentCalls;
        } else if (loadResult->content_kind == OPENUSD_CESIUM_TILE_CONTENT_EMPTY) {
            ++state->emptyContentCalls;
        }
    }
    const std::string_view url(loadResult != nullptr && loadResult->completed_request_url != nullptr
        ? loadResult->completed_request_url
        : "");
    if (url.find("root.glb") != std::string_view::npos) {
        ++state->rootLoadCalls;
    }
    if (url.find("tile.glb") != std::string_view::npos) {
        ++state->childLoadCalls;
    }
    return new int(17);
}

void* prepareMain(void* userData, void* loadThreadResource, openusd_cesium_error_buffer*) {
    auto* state = static_cast<ProbeState*>(userData);
    ++state->mainThreadCalls;
    int value = loadThreadResource != nullptr ? *static_cast<int*>(loadThreadResource) : 0;
    delete static_cast<int*>(loadThreadResource);
    return new int(value + 1);
}

void freeResources(void* userData, void* loadThreadResource, void* mainThreadResource) {
    auto* state = static_cast<ProbeState*>(userData);
    ++state->freeCalls;
    delete static_cast<int*>(loadThreadResource);
    delete static_cast<int*>(mainThreadResource);
}

void meshPrimitive(void* userData, const openusd_cesium_mesh_primitive* primitive) {
    auto* state = static_cast<ProbeState*>(userData);
    ++state->meshPrimitiveCalls;
    if (primitive == nullptr || primitive->position_count != 24 ||
        primitive->face_count != 12 || primitive->face_vertex_index_count != 36) {
        return;
    }
    const size_t middlePoint = primitive->position_count / 2;
    const size_t lastPoint = primitive->position_count - 1;
    const size_t middleIndex = primitive->face_vertex_index_count / 2;
    const size_t lastIndex = primitive->face_vertex_index_count - 1;
    const bool pointsMatch =
        std::isfinite(primitive->positions[0].x) &&
        std::isfinite(primitive->positions[middlePoint].y) &&
        std::isfinite(primitive->positions[lastPoint].z) &&
        (std::abs(primitive->positions[0].x) +
            std::abs(primitive->positions[middlePoint].y) +
            std::abs(primitive->positions[lastPoint].z)) > 0.0f;
    const bool topologyMatches =
        primitive->face_vertex_counts[0] == 3 &&
        primitive->face_vertex_counts[primitive->face_count / 2] == 3 &&
        primitive->face_vertex_counts[primitive->face_count - 1] == 3 &&
        primitive->face_vertex_indices[0] >= 0 &&
        primitive->face_vertex_indices[middleIndex] >= 0 &&
        primitive->face_vertex_indices[lastIndex] >= 0 &&
        primitive->face_vertex_indices[0] < static_cast<int32_t>(primitive->position_count) &&
        primitive->face_vertex_indices[middleIndex] < static_cast<int32_t>(primitive->position_count) &&
        primitive->face_vertex_indices[lastIndex] < static_cast<int32_t>(primitive->position_count);
    const bool transformMatches =
        primitive->transform != nullptr &&
        std::abs(primitive->transform->values[12] - 6378137.123456789) < 0.000001;
    state->sawExpectedMesh = state->sawExpectedMesh || (pointsMatch && topologyMatches && transformMatches);
}

void startTask(void*, openusd_cesium_task* task) {
    std::thread([task]() {
        (void)openusd_cesium_task_execute(task);
        openusd_cesium_task_destroy(task);
    }).detach();
}

bool runProbe(bool failAsset, bool failRenderer) {
    ProbeState state{};
    state.failAsset = failAsset;
    state.failRenderer = failRenderer;

    openusd_cesium_asset_accessor asset{};
    asset.struct_size = sizeof(asset);
    asset.version = OPENUSD_CESIUM_ASSET_ACCESSOR_VERSION;
    asset.user_data = &state;
    asset.request = assetRequest;

    openusd_cesium_renderer_callbacks renderer{};
    renderer.struct_size = sizeof(renderer);
    renderer.version = OPENUSD_CESIUM_RENDERER_CALLBACKS_VERSION;
    renderer.user_data = &state;
    renderer.prepare_in_load_thread = hasDropLoadCallbackFailpoint() ? nullptr : prepareLoad;
    renderer.prepare_in_main_thread = prepareMain;
    renderer.free_resources = freeResources;
    renderer.mesh_primitive_in_load_thread = meshPrimitive;

    openusd_cesium_task_processor tasks{};
    tasks.struct_size = sizeof(tasks);
    tasks.version = OPENUSD_CESIUM_TASK_PROCESSOR_VERSION;
    tasks.start_task = startTask;

    openusd_cesium_tileset_options options{};
    options.struct_size = sizeof(options);
    options.version = OPENUSD_CESIUM_TILESET_OPTIONS_VERSION;
    options.maximum_screen_space_error = 16.0;

    char errorData[512]{};
    openusd_cesium_error_buffer error{errorData, sizeof(errorData), 0};
    openusd_cesium_tileset* tileset = nullptr;
    if (openusd_cesium_tileset_create("memory://tileset.json", &asset, &renderer, &tasks, &options, &tileset, &error) != OPENUSD_CESIUM_STATUS_OK) {
        std::cerr << "create failed: " << errorData << '\n';
        return false;
    }

    openusd_cesium_view_state view{};
    view.struct_size = sizeof(view);
    view.version = OPENUSD_CESIUM_VIEW_STATE_VERSION;
    view.position_ecef = {6378137.123456789 + 1000.0, 0.0, 0.0};
    view.direction_ecef = {-1.0, 0.0, 0.0};
    view.up_ecef = {0.0, 0.0, 1.0};
    view.viewport_width = 1024.0;
    view.viewport_height = 768.0;
    view.horizontal_fov_radians = 1.0;
    view.vertical_fov_radians = 0.75;

    openusd_cesium_update_result update{};
    update.struct_size = sizeof(update);
    for (int i = 0; i < 100; ++i) {
        if (openusd_cesium_tileset_update_view(tileset, &view, 1, 0.016f, &update, &error) != OPENUSD_CESIUM_STATUS_OK) {
            std::cerr << "update failed: " << errorData << '\n';
            (void)openusd_cesium_tileset_release(tileset, &error);
            return false;
        }
        if ((failAsset && state.requests > 0 && update.worker_thread_tile_load_queue_length == 0 &&
             update.main_thread_tile_load_queue_length == 0) ||
            (!failAsset && state.mainThreadCalls > 0 && update.worker_thread_tile_load_queue_length == 0 &&
             update.main_thread_tile_load_queue_length == 0)) {
            break;
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }
    state.sawChildSelection = state.childLoadCalls > 0;

    view.position_ecef = {6378137.123456789 + 10000000.0, 0.0, 0.0};
    for (int i = 0; i < 100 && !failAsset && !failRenderer; ++i) {
        if (openusd_cesium_tileset_update_view(tileset, &view, 1, 0.016f, &update, &error) != OPENUSD_CESIUM_STATUS_OK) {
            std::cerr << "far update failed: " << errorData << '\n';
            (void)openusd_cesium_tileset_release(tileset, &error);
            return false;
        }
        if (state.rootLoadCalls > 0 && update.worker_thread_tile_load_queue_length == 0 &&
            update.main_thread_tile_load_queue_length == 0) {
            break;
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }
    state.sawRootSelection = state.rootLoadCalls > 0;

    size_t messageCount = 0;
    (void)openusd_cesium_tileset_get_message_count(tileset, &messageCount, &error);
    (void)openusd_cesium_tileset_release(tileset, &error);

    if (failAsset) {
        return messageCount > 0 && state.loadThreadCalls == 0;
    }
    if (failRenderer) {
        return state.loadThreadCalls > 0 && state.mainThreadCalls > 0 && !state.sawDoubleTransform;
    }
    std::cout << "requests=" << state.requests
              << " load=" << state.loadThreadCalls
              << " main=" << state.mainThreadCalls
              << " mesh=" << state.meshPrimitiveCalls
              << " gltfMeshes=" << state.gltfMeshCountInLoadResult
              << " gltfContent=" << state.gltfContentCalls
              << " external=" << state.externalContentCalls
              << " empty=" << state.emptyContentCalls
              << " root=" << state.rootLoadCalls
              << " child=" << state.childLoadCalls
              << " free=" << state.freeCalls
              << " render=" << update.tiles_to_render_count
              << " loaded=" << update.loaded_tile_count << '\n';
    return state.requests >= 2 && state.loadThreadCalls > 0 && state.mainThreadCalls > 0 &&
           state.freeCalls > 0 && state.sawDoubleTransform &&
           state.gltfContentCalls > 0 && state.sawRootSelection && state.sawChildSelection &&
           update.loaded_tile_count > 0;
}

} // namespace

int main(int argc, char** argv) {
    const bool failAsset = argc > 1 && std::string_view(argv[1]) == "--expect-asset-failure";
    const bool failRenderer = argc > 1 && std::string_view(argv[1]) == "--expect-renderer-break";
    if (!runProbe(failAsset, failRenderer)) {
        return failAsset || failRenderer ? 3 : 1;
    }
    return 0;
}