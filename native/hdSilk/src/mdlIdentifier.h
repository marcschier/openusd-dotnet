// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef HDSILK_MDL_IDENTIFIER_H
#define HDSILK_MDL_IDENTIFIER_H

#include "pxr/pxr.h"

#include <string>
#include <string_view>

PXR_NAMESPACE_OPEN_SCOPE

inline std::string
HdSilkEncodeMdlIdentifier(const std::string& module, const std::string& material)
{
    // Length framing preserves colons in both module URIs and SDK type names.
    return "mdl:len:" + std::to_string(module.size()) + ":" + module + material;
}

inline bool
HdSilkDecodeMdlIdentifier(
    std::string_view identifier, std::string* module, std::string* material)
{
    constexpr std::string_view prefix = "mdl:";
    constexpr std::string_view framedPrefix = "mdl:len:";
    if (identifier.substr(0, prefix.size()) != prefix) { return false; }
    if (identifier.substr(0, framedPrefix.size()) == framedPrefix)
    {
        const size_t separator = identifier.find(':', framedPrefix.size());
        if (separator == std::string_view::npos || separator == framedPrefix.size()) { return false; }
        const size_t remaining = identifier.size() - separator - 1;
        size_t length = 0;
        for (size_t index = framedPrefix.size(); index < separator; ++index)
        {
            const char character = identifier[index];
            if (character < '0' || character > '9') { return false; }
            const size_t digit = static_cast<size_t>(character - '0');
            if (length > remaining / 10 ||
                (length == remaining / 10 && digit > remaining % 10)) { return false; }
            length = length * 10 + digit;
        }
        if (length == 0) { return false; }
        *module = std::string(identifier.substr(separator + 1, length));
        *material = std::string(identifier.substr(separator + 1 + length));
        return true;
    }

    // Retain the older synthetic form, but do not split a qualified type inside
    // its trailing signature. New producer output never needs this inference.
    const std::string_view body = identifier.substr(prefix.size());
    size_t end = body.size();
    if (end != 0 && body.back() == ')')
    {
        size_t depth = 0;
        for (size_t index = end; index != 0; --index)
        {
            const char character = body[index - 1];
            if (character == ')') { ++depth; }
            if (character == '(' && --depth == 0)
            {
                end = index - 1;
                break;
            }
        }
        if (depth != 0) { return false; }
    }
    const size_t separator = end == 0 ? std::string_view::npos : body.rfind(':', end - 1);
    const std::string_view decodedModule = separator == std::string_view::npos
        ? body : body.substr(0, separator);
    if (decodedModule.empty()) { return false; }
    *module = std::string(decodedModule);
    *material = separator == std::string_view::npos ? std::string() : std::string(body.substr(separator + 1));
    return true;
}

PXR_NAMESPACE_CLOSE_SCOPE

#endif
