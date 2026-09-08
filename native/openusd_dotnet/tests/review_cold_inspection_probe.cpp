// Copyright (c) marcschier. Licensed under the MIT License.
#include "openusd_review_document.h"

#include <array>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <map>
#include <stdexcept>
#include <string>
#include <vector>

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#include <bcrypt.h>
#endif

namespace
{
#if defined(_WIN32)
using Bytes = std::vector<uint8_t>;
using Fields = std::map<std::string, Bytes>;
struct Property
{
    uint32_t type;
    Fields fields;
};
using Properties = std::map<std::string, Property>;

void Require(bool value, const char* message)
{
    if (!value) { throw std::runtime_error(message); }
}
void U32(Bytes& bytes, uint32_t value)
{
    for (size_t i = 0; i < 4; ++i) { bytes.push_back(static_cast<uint8_t>(value >> (i * 8))); }
}
void U64(Bytes& bytes, uint64_t value)
{
    for (size_t i = 0; i < 8; ++i) { bytes.push_back(static_cast<uint8_t>(value >> (i * 8))); }
}
void Append(Bytes& bytes, const Bytes& value)
{
    bytes.insert(bytes.end(), value.begin(), value.end());
}
void Text(Bytes& bytes, const std::string& value)
{
    U32(bytes, static_cast<uint32_t>(value.size()));
    bytes.insert(bytes.end(), value.begin(), value.end());
}
void Blob(Bytes& bytes, const Bytes& value)
{
    U32(bytes, static_cast<uint32_t>(value.size()));
    Append(bytes, value);
}
Bytes Words(uint32_t tag, std::initializer_list<uint32_t> values)
{
    Bytes bytes;
    U32(bytes, tag);
    for (const auto value : values) { U32(bytes, value); }
    return bytes;
}
Bytes WideWords(uint32_t tag, std::initializer_list<uint64_t> values)
{
    Bytes bytes;
    U32(bytes, tag);
    for (const auto value : values) { U64(bytes, value); }
    return bytes;
}
Bytes TextValue(uint32_t tag, const std::string& value)
{
    Bytes bytes;
    U32(bytes, tag); Text(bytes, value);
    return bytes;
}
Bytes Strings(uint32_t tag, const std::vector<std::string>& values)
{
    Bytes bytes;
    U32(bytes, tag); U32(bytes, static_cast<uint32_t>(values.size()));
    for (const auto& value : values) { Text(bytes, value); }
    return bytes;
}
void WriteFields(Bytes& bytes, const Fields& fields)
{
    U32(bytes, static_cast<uint32_t>(fields.size()));
    for (const auto& field : fields) { Text(bytes, field.first); Append(bytes, field.second); }
}
Bytes Dictionary(const Fields& fields)
{
    Bytes bytes;
    U32(bytes, 22); WriteFields(bytes, fields);
    return bytes;
}
Bytes PathList(const std::string& path)
{
    Bytes bytes = Words(16, {1, 1});
    Text(bytes, path);
    for (int i = 0; i < 5; ++i) { U32(bytes, 0); }
    return bytes;
}
Bytes Asset(const std::string& path)
{
    Bytes bytes = TextValue(9, path);
    Text(bytes, ""); Text(bytes, "");
    return bytes;
}
Property Attribute(const std::string& type, Bytes value = {})
{
    Property property{1, {{"typeName", TextValue(7, type)}}};
    if (!value.empty()) { property.fields.emplace("default", std::move(value)); }
    return property;
}
void Spec(Bytes& bytes, const std::string& path, uint32_t type, const Fields& fields)
{
    Text(bytes, path); U32(bytes, type); WriteFields(bytes, fields);
}
Bytes Review(Fields root = {}, const Properties& properties = {})
{
    Bytes review;
    U32(review, 0x31575652); U32(review, 1);
    U32(review, properties.empty() ? 1 : 2 + static_cast<uint32_t>(properties.size()));
    if (!properties.empty()) { root.emplace("primChildren", Strings(17, {"World"})); }
    Spec(review, "/", 7, root);
    if (!properties.empty())
    {
        std::vector<std::string> names;
        for (const auto& property : properties) { names.push_back(property.first); }
        Spec(review, "/World", 6, {{"properties", Strings(17, names)}, {"specifier", Words(19, {1})}});
        for (const auto& property : properties)
        {
            Spec(review, "/World." + property.first, property.second.type, property.second.fields);
        }
    }
    return review;
}
Bytes TypedReview()
{
    Properties properties{
        {"asset", Attribute("asset", Asset("//review-inspection.invalid/share/texture.bin"))},
        {"half", Attribute("half", Words(43, {0xfe01}))},
        {"index", Attribute("PointIndex", Words(3, {42}))},
        {"number", Attribute("double", WideWords(6, {0x8000000000000000}))},
        {"quaternion", Attribute("quatd", WideWords(56, {
            0x3ff0000000000000, 0, 0x8000000000000000, 0x4008000000000000}))},
        {"relationship", Property{8, {{"targetPaths", PathList("/World")}}}},
        {"time", Attribute("timecode", WideWords(44, {0x8000000000000000}))},
        {"unsigned", Attribute("uint64", WideWords(41, {0xf123456789abcdef}))},
        {"vector", Attribute("color3f", Words(11, {0x3f800000, 0x80000000, 0x40400000}))},
        {"words", Attribute("string[]", Strings(264, {"zero", "one"}))}};
    Bytes matrix;
    U32(matrix, 15);
    for (int i = 0; i < 16; ++i) { U64(matrix, i % 5 == 0 ? uint64_t{0x3ff0000000000000} : 0); }
    properties.emplace("matrix", Attribute("Transform", matrix));
    Bytes vectors = Words(267, {2});
    Append(vectors, Words(11, {0x3f800000, 0x80000000, 0x40400000}));
    Append(vectors, Words(11, {0x40800000, 0x40a00000, 0x40c00000}));
    properties.emplace("vectors", Attribute("PointFloat[]", vectors));
    Bytes samples = Words(21, {3});
    U64(samples, 0x3ff0000000000000); Append(samples, WideWords(6, {0x7ff8000000000123}));
    U64(samples, 0x4000000000000000); Append(samples, WideWords(6, {0x8000000000000000}));
    U64(samples, 0x4008000000000000); Append(samples, Words(1, {}));
    properties.at("number").fields.emplace("timeSamples", samples);
    return Review({{"customLayerData", Dictionary({
        {"nested", Dictionary({{"recorded", TextValue(8, "claim")}})},
        {"words", Strings(264, {"zero", "one"})}})}}, properties);
}
Bytes Metadata()
{
    Bytes payload;
    const std::string root = "Z:/never-open-cold-inspection/source.usda";
    const std::string fingerprint(64, 'a');
    Text(payload, "158868de-43ce-44dc-9373-bbf7e1124b4c");
    Text(payload, root); Text(payload, fingerprint); Text(payload, "untrusted-original-target");
    Text(payload, "//review-inspection.invalid/share/review.urd");
    Text(payload, "Z:/never-open-cold-inspection");
    U32(payload, 1); Text(payload, root); Text(payload, fingerprint);
    U32(payload, 1); U32(payload, 0); U32(payload, 0);
    return payload;
}
Bytes Document(const Bytes& review)
{
    Bytes payload = Metadata();
    Blob(payload, review);
    std::array<uint8_t, 32> digest{};
    Require(BCryptHash(BCRYPT_SHA256_ALG_HANDLE, nullptr, 0, payload.data(),
        static_cast<ULONG>(payload.size()), digest.data(), static_cast<ULONG>(digest.size())) >= 0,
        "Independent fixture SHA-256 failed.");
    Bytes document;
    U32(document, 0x31445255); U32(document, 1); U32(document, static_cast<uint32_t>(payload.size())); U32(document, 0);
    document.insert(document.end(), digest.begin(), digest.end());
    document.insert(document.end(), payload.begin(), payload.end());
    return document;
}
struct Fixture
{
    Bytes document;
    bool accepted = true;
};
Fixture Input(const std::string& mode)
{
    Bytes review;
    bool accepted = false;
    if (mode == "field")
    {
        review = Review({{"inspectionProbeValue", TextValue(8, "recorded metadata")}});
        accepted = true;
    }
    else if (mode == "empty") { review = Review(); accepted = true; }
    else if (mode == "typed") { review = TypedReview(); accepted = true; }
    else if (mode == "declarations")
    {
        Properties properties;
        for (const auto* type : {"color3d", "frame4d", "Transform[]", "PointIndex[]", "group", "opaque"})
        {
            properties.emplace("value" + std::to_string(properties.size()), Attribute(type));
        }
        review = Review({}, properties); accepted = true;
    }
    else if (mode == "fps-positive")
    {
        review = Review({{"framesPerSecond", WideWords(6, {0x4038000000000000})}}); accepted = true;
    }
    else if (mode == "fps-zero") { review = Review({{"framesPerSecond", WideWords(6, {0})}}); }
    else if (mode == "fps-negative")
    {
        review = Review({{"framesPerSecond", WideWords(6, {0xbff0000000000000})}});
    }
    else if (mode == "fps-nan")
    {
        review = Review({{"framesPerSecond", WideWords(6, {0x7ff8000000000123})}});
    }
    else if (mode == "fps-type") { review = Review({{"framesPerSecond", Words(5, {0x41c00000})}}); }
    else if (mode == "field-spec") { review = Review({{"typeName", TextValue(7, "Xform")}}); }
    else if (mode == "field-type") { review = Review({{"documentation", WideWords(6, {0})}}); }
    else if (mode == "declaration-mismatch")
    {
        review = Review({}, {{"value", Attribute("double", Words(5, {0x3f800000}))}});
    }
    else if (mode == "array-mismatch")
    {
        review = Review({}, {{"value", Attribute("float[]", Strings(264, {"not a float"}))}});
    }
    else if (mode == "sample-mismatch")
    {
        auto property = Attribute("double");
        Bytes samples = Words(21, {1});
        U64(samples, 0x3ff0000000000000); Append(samples, Words(5, {0x3f800000}));
        property.fields.emplace("timeSamples", samples);
        review = Review({}, {{"value", property}});
    }
    else if (mode == "unknown-type") { review = Review({}, {{"value", Attribute("group[]")}}); }
    else if (mode == "default-scene-value")
    {
        review = Review({}, {{"value", Property{8, {
            {"default", Dictionary({{"notSceneData", TextValue(37, "/World")}})}}}}});
    }
    else if (mode == "clips") { review = Review({{"clips", Dictionary({})}}); }
    else if (mode == "path-depth")
    {
        std::string path;
        for (int i = 0; i < 33; ++i) { path += "/Child"; }
        review = Review({{"inspectionProbeValue", PathList(path)}});
    }
    else if (mode == "value-depth")
    {
        Bytes value = TextValue(8, "nested claim");
        for (int i = 0; i < 16; ++i) { value = Dictionary({{"child", value}}); }
        review = Review({{"inspectionProbeValue", value}});
    }
    else if (mode == "array-budget") { review = Review({{"inspectionProbeValue", Words(259, {4097})}}); }
    else if (mode == "missing-child") { review = Review({{"primChildren", Strings(17, {"Missing"})}}); }
    else if (mode == "utf8")
    {
        review = Review({{"inspectionProbeValue", TextValue(8, std::string("\xc0\xaf", 2))}});
    }
    else if (mode == "checksum" || mode == "extent") { review = Review(); }
    else { throw std::runtime_error("Unknown cold inspection case."); }
    auto document = Document(review);
    if (mode == "checksum") { document.back() ^= 1; }
    if (mode == "extent") { document.push_back(0); }
    return Fixture{std::move(document), accepted};
}
using IoCounts = std::array<uint64_t, 6>;
IoCounts Counters()
{
    IO_COUNTERS counters{};
    Require(GetProcessIoCounters(GetCurrentProcess(), &counters) != 0, "Could not observe process I/O.");
    return {counters.ReadOperationCount, counters.WriteOperationCount, counters.OtherOperationCount,
        counters.ReadTransferCount, counters.WriteTransferCount, counters.OtherTransferCount};
}
void PrintDelta(const char* label, const IoCounts& before, const IoCounts& after)
{
    std::cout << label << " [reads,writes,other,readBytes,writeBytes,otherBytes]=";
    for (size_t i = 0; i < before.size(); ++i) { std::cout << (i ? "," : "") << after[i] - before[i]; }
    std::cout << '\n';
}
void Inspect(const Fixture& fixture)
{
    const auto& document = fixture.document;
    Bytes expected = Words(0x31494452, {1, static_cast<uint32_t>(document.size())});
    Append(expected, Metadata());
    char message[4096]{};
    openusd_error_buffer error{message, sizeof(message), 0};
    auto* const poison = reinterpret_cast<openusd_edit_buffer*>(uintptr_t{1});
    openusd_edit_buffer* owner = poison;
    openusd_edit_buffer_view view{reinterpret_cast<const uint8_t*>(uintptr_t{1}), 123};
    const auto before = Counters();
    const auto status = openusd_review_document_inspect(document.data(), document.size(), &owner, &view, &error);
    const auto after = Counters();
    const bool outputValid = fixture.accepted
        ? owner != nullptr && owner != poison && view.data != nullptr && view.size == expected.size()
            && std::memcmp(view.data, expected.data(), expected.size()) == 0
        : owner == nullptr && view.data == nullptr && view.size == 0 && message[0] != '\0';
    if (owner != poison) { openusd_edit_buffer_release(owner); }
    // Include destruction and a quiet interval so deferred SDK workers cannot
    // move their filesystem activity just outside the observed Inspect call.
    Sleep(25);
    const auto settled = Counters();
    PrintDelta("call", before, after);
    PrintDelta("call+release+settled", before, settled);
    Require(status == (fixture.accepted ? OPENUSD_STATUS_OK : OPENUSD_STATUS_NATIVE_ERROR), message);
    Require(outputValid, "Inspection must return exact metadata only or clear every refused output.");
    Require(before == after && before == settled,
        "Cold inspection performed filesystem/plugin discovery I/O.");
}
#endif
}

int main(int argc, char** argv)
{
    try
    {
#if defined(_WIN32)
        const std::string mode = argc > 1 ? argv[1] : "field";
        const auto fixture = Input(mode);
        const auto first = Counters();
        Sleep(25);
        const auto control = Counters();
        Require(first == control, "I/O observation control is not quiet.");
        // Deliberately no stage, schema access, plugin registration or search-path suppression.
        Inspect(fixture);
        Inspect(fixture);
        std::cout << "PASS cold inspection " << mode
            << ": native admission and metadata-only output, all six I/O counters remain zero\n";
        return 0;
#else
        static_cast<void>(argc); static_cast<void>(argv);
        std::cout << "SKIP cold OS I/O counters: Windows-only observation\n";
        return 77;
#endif
    }
    catch (const std::exception& error)
    {
        std::cerr << "FAIL cold inspection: " << error.what() << '\n';
        return 1;
    }
}
