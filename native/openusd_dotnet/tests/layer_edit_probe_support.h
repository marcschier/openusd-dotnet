// Copyright (c) marcschier. Licensed under the MIT License.
#pragma once
#include "openusd_layer_edit.h"
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
extern "C" OPENUSD_DOTNET_API void openusd_layer_edit_test_fail_after(int32_t writes);
extern "C" OPENUSD_DOTNET_API void openusd_layer_edit_test_checkpoint_fail_after(int32_t phase);
extern "C" OPENUSD_DOTNET_API void openusd_layer_edit_test_overlay_fail_after(int32_t phase);
#endif
#include "pxr/usd/sdf/layer.h"

#include <cstring>
#include <initializer_list>
#include <stdexcept>
#include <string>
#include <vector>

namespace EditProbe
{
using Bytes = std::vector<uint8_t>;

inline void Require(bool condition, const char* message)
{
    if (!condition) { throw std::runtime_error(message); }
}

inline void U32(Bytes& bytes, uint32_t value)
{
    for (int i = 0; i < 4; ++i)
    {
        bytes.push_back(static_cast<uint8_t>(value >> (i * 8)));
    }
}

inline void F64(Bytes& bytes, double value)
{
    uint64_t bits;
    std::memcpy(&bits, &value, sizeof(bits));
    U32(bytes, static_cast<uint32_t>(bits));
    U32(bytes, static_cast<uint32_t>(bits >> 32));
}

inline void Text(Bytes& bytes, const std::string& value)
{
    U32(bytes, static_cast<uint32_t>(value.size()));
    bytes.insert(bytes.end(), value.begin(), value.end());
}

inline uint32_t ReadU32(const Bytes& bytes, size_t offset)
{
    Require(offset + 4 <= bytes.size(), "Truncated C ABI output.");
    return static_cast<uint32_t>(bytes[offset])
        | (static_cast<uint32_t>(bytes[offset + 1]) << 8)
        | (static_cast<uint32_t>(bytes[offset + 2]) << 16)
        | (static_cast<uint32_t>(bytes[offset + 3]) << 24);
}

inline uint64_t ReadU64(const Bytes& bytes, size_t offset)
{
    return ReadU32(bytes, offset) | (static_cast<uint64_t>(ReadU32(bytes, offset + 4)) << 32);
}

inline Bytes Header(uint32_t kind)
{
    Bytes bytes;
    U32(bytes, 0x31444555); U32(bytes, 1); U32(bytes, kind);
    return bytes;
}

struct Address
{
    std::string path;
    uint32_t field = 0;
    double time = 0;
    void Write(Bytes& bytes) const
    {
        Text(bytes, path); U32(bytes, field); F64(bytes, time);
    }
};

inline Bytes AddressPacket(const std::vector<Address>& addresses)
{
    auto bytes = Header(1);
    U32(bytes, static_cast<uint32_t>(addresses.size()));
    for (const auto& address : addresses) { address.Write(bytes); }
    return bytes;
}

inline Bytes DoubleValue(double value)
{
    Bytes bytes;
    U32(bytes, 6); F64(bytes, value);
    return bytes;
}

inline Bytes EmptyValue()
{
    Bytes bytes;
    U32(bytes, 0);
    return bytes;
}

struct Mutation
{
    Address address;
    uint32_t operation = 1;
    std::string typeName = "double";
    uint32_t variability = 0;
    uint32_t custom = 0;
    Bytes value = EmptyValue();
};

inline Mutation SetDouble(const Address& address, double value)
{
    return {address, 1, "double", 0, 0, DoubleValue(value)};
}

inline Bytes MutationPacket(const std::vector<Mutation>& mutations)
{
    auto bytes = Header(3);
    U32(bytes, static_cast<uint32_t>(mutations.size()));
    for (const auto& mutation : mutations)
    {
        mutation.address.Write(bytes);
        U32(bytes, mutation.operation);
        Text(bytes, mutation.typeName);
        U32(bytes, mutation.variability);
        U32(bytes, mutation.custom);
        bytes.insert(bytes.end(), mutation.value.begin(), mutation.value.end());
    }
    return bytes;
}

struct Api
{
    char message[2048]{};
    openusd_error_buffer error{message, sizeof(message), 0};

    void Ok(openusd_status status) { Require(status == OPENUSD_STATUS_OK, message); }

    static Bytes Copy(openusd_edit_buffer* owner, const openusd_edit_buffer_view& view)
    {
        Bytes bytes;
        if (view.size) { bytes.assign(view.data, view.data + view.size); }
        openusd_edit_buffer_release(owner);
        return bytes;
    }

    Bytes Capture(openusd_layer* layer, const std::vector<Address>& addresses)
    {
        const auto packet = AddressPacket(addresses);
        openusd_edit_buffer* owner = nullptr;
        openusd_edit_buffer_view view{};
        Ok(openusd_layer_edit_capture(layer, packet.data(), packet.size(), &owner, &view, &error));
        return Copy(owner, view);
    }

    Bytes Apply(openusd_layer* layer, const Bytes& expected,
        const std::vector<Mutation>& mutations, int32_t expectedOutcome = OPENUSD_EDIT_APPLIED)
    {
        const auto packet = MutationPacket(mutations);
        openusd_edit_buffer* owner = nullptr;
        openusd_edit_buffer_view view{};
        int32_t outcome = -1;
        Ok(openusd_layer_edit_apply(layer, expected.data(), expected.size(),
            packet.data(), packet.size(), &outcome, &owner, &view, &error));
        const auto result = Copy(owner, view);
        if (outcome != expectedOutcome)
        {
            throw std::runtime_error("Compare/apply expected " + std::to_string(expectedOutcome)
                + ", actual " + std::to_string(outcome) + ": "
                + (mutations.empty() ? std::string() : mutations[0].address.path) + " " + message);
        }
        Require((outcome == OPENUSD_EDIT_APPLIED) == !result.empty(), "Outcome/output ownership mismatch.");
        return result;
    }

    Bytes Restore(openusd_layer* layer, const Bytes& expected, const Bytes& desired,
        int32_t expectedOutcome = OPENUSD_EDIT_APPLIED)
    {
        openusd_edit_buffer* owner = nullptr;
        openusd_edit_buffer_view view{};
        int32_t outcome = -1;
        Ok(openusd_layer_edit_restore(layer, expected.data(), expected.size(),
            desired.data(), desired.size(), &outcome, &owner, &view, &error));
        const auto result = Copy(owner, view);
        Require(outcome == expectedOutcome, message[0] ? message : "Unexpected compare/restore outcome.");
        return result;
    }

    pxr::SdfLayerHandle Native(openusd_layer* layer)
    {
        char identifier[4097]{};
        size_t required = 0;
        Ok(openusd_layer_get_identifier(layer, identifier, sizeof(identifier), &required, &error));
        const auto result = pxr::SdfLayer::Find(identifier);
        Require(static_cast<bool>(result), "Returned owned layer must remain resident.");
        return result;
    }

    Bytes Checkpoint(openusd_layer* layer)
    {
        openusd_edit_buffer* owner = nullptr;
        openusd_edit_buffer_view view{};
        Ok(openusd_layer_edit_checkpoint(layer, &owner, &view, &error));
        return Copy(owner, view);
    }

    Bytes State(openusd_layer* layer)
    {
        openusd_edit_buffer* owner = nullptr;
        openusd_edit_buffer_view view{};
        Ok(openusd_layer_edit_get_state(layer, &owner, &view, &error));
        return Copy(owner, view);
    }
};
}
