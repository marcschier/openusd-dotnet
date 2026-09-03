// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

#ifdef _WIN32
#include <windows.h>
#endif

#include <OpenColorIO/OpenColorIO.h>
#include <OpenColorIO/OpenColorAppHelpers.h>

#include <algorithm>
#include <atomic>
#include <cctype>
#include <cstdlib>
#include <filesystem>
#include <set>
#include <utility>
#include <cstdio>
#include <fstream>
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

// Bounds on the dependency walk. They are applied after canonical de-duplication, so a
// config that names one file through several aliases is measured once. Exceeding either
// bound does not silently degrade the identity: the walk reports itself as partial and
// the caller refuses to cache against it.
constexpr size_t MaximumDependencyFiles = 256;
constexpr uint64_t MaximumDependencyBytes = 64ull * 1024 * 1024;

// Counts completed dependency walks so a test can prove the managed side performs
// exactly one per revalidation rather than probing for a size and walking twice.
std::atomic<uint64_t> g_dependencyWalks{0};

// A referencing CTF/CLF document is parsed whole, so it is read whole. Sixteen mebibytes
// is far beyond any real transform list and keeps the parse bounded; a document larger
// than this marks the walk partial instead of being scanned in unsafe pieces.
constexpr uint64_t MaximumReferenceDocumentBytes = 16ull * 1024 * 1024;

inline uint64_t HashBytes(uint64_t seed, const void* data, size_t size)
{
    const auto* bytes = static_cast<const unsigned char*>(data);
    uint64_t hash = seed;
    for (size_t i = 0; i < size; ++i)
    {
        hash ^= static_cast<uint64_t>(bytes[i]);
        hash *= 1099511628211ull;
    }
    return hash;
}

inline uint64_t HashString(uint64_t seed, const std::string& value)
{
    return HashBytes(seed, value.data(), value.size());
}

// Opens a file by a UTF-8 path.
//
// On Windows a narrow path is interpreted in the active code page, so a config or LUT
// under a directory with non-ASCII characters simply fails to open -- and a file that
// cannot be opened hashes as missing, which would make an unreadable dependency
// indistinguishable from a deleted one. The path is therefore widened explicitly.
std::ifstream OpenBinary(const std::string& utf8Path)
{
#ifdef _WIN32
    const int required = ::MultiByteToWideChar(
        CP_UTF8,
        0,
        utf8Path.c_str(),
        static_cast<int>(utf8Path.size()),
        nullptr,
        0);
    if (required <= 0)
    {
        return std::ifstream();
    }
    std::wstring wide(static_cast<size_t>(required), L'\0');
    ::MultiByteToWideChar(
        CP_UTF8,
        0,
        utf8Path.c_str(),
        static_cast<int>(utf8Path.size()),
        wide.data(),
        required);
    return std::ifstream(wide, std::ios::binary);
#else
    return std::ifstream(utf8Path, std::ios::binary);
#endif
}

#ifdef _WIN32
std::wstring WidenUtf8(const std::string& value)
{
    if (value.empty())
    {
        return std::wstring();
    }
    const int required = ::MultiByteToWideChar(
        CP_UTF8,
        0,
        value.c_str(),
        static_cast<int>(value.size()),
        nullptr,
        0);
    if (required <= 0)
    {
        return std::wstring();
    }
    std::wstring wide(static_cast<size_t>(required), L'\0');
    ::MultiByteToWideChar(
        CP_UTF8,
        0,
        value.c_str(),
        static_cast<int>(value.size()),
        &wide[0],
        required);
    return wide;
}

std::string NarrowUtf8(const std::wstring& value)
{
    if (value.empty())
    {
        return std::string();
    }
    const int required = ::WideCharToMultiByte(
        CP_UTF8,
        0,
        value.c_str(),
        static_cast<int>(value.size()),
        nullptr,
        0,
        nullptr,
        nullptr);
    if (required <= 0)
    {
        return std::string();
    }
    std::string narrow(static_cast<size_t>(required), '\0');
    ::WideCharToMultiByte(
        CP_UTF8,
        0,
        value.c_str(),
        static_cast<int>(value.size()),
        &narrow[0],
        required,
        nullptr,
        nullptr);
    return narrow;
}
#endif

// Converts a UTF-8 path to and from std::filesystem::path.
//
// On Windows the native representation is UTF-16, and constructing a path from a narrow
// string interprets it in the active code page, so a directory with non-ASCII characters
// resolves to the wrong name -- or to no name at all. Widening explicitly keeps the whole
// dependency walk correct for any path a user can actually create.
std::filesystem::path ToPath(const std::string& utf8Path)
{
#ifdef _WIN32
    return std::filesystem::path(WidenUtf8(utf8Path));
#else
    return std::filesystem::path(utf8Path);
#endif
}

std::string FromPath(const std::filesystem::path& path)
{
#ifdef _WIN32
    return NarrowUtf8(path.wstring());
#else
    return path.string();
#endif
}

// Resolves a path to a stable identity for the visited set.
//
// canonical() resolves symbolic links, junctions, and "." / ".." so two names for one
// file collapse to a single entry; that is what stops an alias cycle from walking
// forever and stops the same LUT being hashed twice under two spellings. A file that
// does not exist has no canonical form, so its lexically normalized absolute path is
// used instead, which still distinguishes it from every other name.
std::string CanonicalKey(const std::filesystem::path& path)
{
    std::error_code code;
    std::filesystem::path resolved = std::filesystem::canonical(path, code);
    if (code)
    {
        resolved = std::filesystem::weakly_canonical(path, code);
        if (code)
        {
            resolved = path.lexically_normal();
        }
    }
    return FromPath(resolved);
}

// A deliberately conservative XML reader for CTF/CLF transform files.
//
// Substring scanning was not good enough and could not be made good enough. "<Reference"
// also matches "<ReferenceList", "path=" also matches "xpath=" and "basePath=", an
// attribute value carrying "&gt;" ends the element early for a scanner that stops at the
// first '>', and a comment or CDATA section containing either token invents references
// that do not exist. Each of those is a wrong identity, and a wrong identity is either a
// stale image or a rebake every frame.
//
// The reader below is correctness-first rather than best-effort, and it does not try to
// be a superset of OpenColorIO's reader. Anything it cannot interpret with certainty --
// a DOCTYPE, a general or unknown entity reference, a namespace-prefixed name, markup it
// cannot parse, or a document past its size bound -- makes it report the file as
// unsupported. The walk then marks the whole identity as non-exhaustive and the managed
// cache refuses the transform with an explicit diagnostic, so an uncertain parse becomes
// a named refusal instead of a silently stale image.
//
// Only a <Reference> element that is a direct child of the root <ProcessList> is a
// reference, which is exactly where OpenColorIO's CTF reader accepts one. A <Reference>
// anywhere else -- inside <Info>, inside <Description>, nested in another element -- is
// not an op and is ignored, matching the reader rather than guessing.
enum class XmlOutcome
{
    // A complete, unambiguous parse.
    Complete,

    // Something the reader will not interpret. The caller must not treat the result as
    // an exhaustive view of the file's dependencies.
    Unsupported,
};

// ASCII case-insensitive comparison, matching OpenColorIO's Platform::Strcasecmp, which
// is what its CTF reader uses for tag and attribute names.
bool EqualsIgnoreAsciiCase(const std::string& left, const char* right)
{
    size_t index = 0;
    for (; index < left.size(); ++index)
    {
        const char other = right[index];
        if (other == '\0')
        {
            return false;
        }
        const int a = std::tolower(static_cast<unsigned char>(left[index]));
        const int b = std::tolower(static_cast<unsigned char>(other));
        if (a != b)
        {
            return false;
        }
    }
    return right[index] == '\0';
}

class XmlScanner
{
public:
    explicit XmlScanner(const std::string& text)
        : m_text(text)
    {
    }

    // Collects the paths named by <Reference> elements directly under the root
    // <ProcessList>. Returns Unsupported the moment anything is not certain.
    XmlOutcome CollectReferences(std::vector<std::string>& references)
    {
        std::vector<std::string> stack;
        bool sawRoot = false;

        while (m_position < m_text.size())
        {
            const size_t open = m_text.find('<', m_position);
            if (open == std::string::npos)
            {
                break;
            }

            // Character data between tags may carry entity references, and an
            // unsupported one there means the document uses a construct this reader
            // does not model.
            if (!IsSupportedText(m_text, m_position, open))
            {
                return XmlOutcome::Unsupported;
            }

            m_position = open + 1;
            if (m_position >= m_text.size())
            {
                return XmlOutcome::Unsupported;
            }

            const char lead = m_text[m_position];
            if (lead == '?')
            {
                if (!SkipTo("?>"))
                {
                    return XmlOutcome::Unsupported;
                }
                continue;
            }
            if (lead == '!')
            {
                if (m_text.compare(m_position, 3, "!--") == 0)
                {
                    if (!SkipTo("-->"))
                    {
                        return XmlOutcome::Unsupported;
                    }
                    continue;
                }
                if (m_text.compare(m_position, 8, "![CDATA[") == 0)
                {
                    if (!SkipTo("]]>"))
                    {
                        return XmlOutcome::Unsupported;
                    }
                    continue;
                }

                // A DOCTYPE may declare internal entities that change what every
                // attribute value means. Rather than model them, the file is reported
                // as unsupported.
                return XmlOutcome::Unsupported;
            }
            if (lead == '/')
            {
                ++m_position;
                std::string name;
                if (!ReadName(name) || HasPrefix(name))
                {
                    return XmlOutcome::Unsupported;
                }
                SkipSpace();
                if (m_position >= m_text.size() || m_text[m_position] != '>')
                {
                    return XmlOutcome::Unsupported;
                }
                ++m_position;
                if (stack.empty() || !EqualsIgnoreAsciiCase(stack.back(), name.c_str()))
                {
                    // Mismatched or unbalanced markup: the reader cannot know what the
                    // document means.
                    return XmlOutcome::Unsupported;
                }
                stack.pop_back();
                continue;
            }

            std::string name;
            std::vector<std::pair<std::string, std::string>> attributes;
            bool empty = false;
            if (!ReadElement(name, attributes, empty) || HasPrefix(name))
            {
                return XmlOutcome::Unsupported;
            }

            const size_t depth = stack.size();
            if (depth == 0)
            {
                if (sawRoot || !EqualsIgnoreAsciiCase(name, "ProcessList"))
                {
                    // Not a CTF/CLF process list, or a second root element.
                    return XmlOutcome::Unsupported;
                }
                sawRoot = true;
            }
            else if (depth == 1 && EqualsIgnoreAsciiCase(name, "Reference"))
            {
                bool found = false;
                for (const auto& attribute : attributes)
                {
                    if (!EqualsIgnoreAsciiCase(attribute.first, "path"))
                    {
                        continue;
                    }
                    if (attribute.second.empty())
                    {
                        return XmlOutcome::Unsupported;
                    }
                    references.push_back(attribute.second);
                    found = true;
                    break;
                }
                if (!found)
                {
                    // A reference op with no path is not something this reader will
                    // interpret on OpenColorIO's behalf.
                    return XmlOutcome::Unsupported;
                }
            }

            if (!empty)
            {
                stack.push_back(name);
            }
        }

        if (!sawRoot || !stack.empty())
        {
            return XmlOutcome::Unsupported;
        }
        return IsSupportedText(m_text, m_position, m_text.size())
            ? XmlOutcome::Complete
            : XmlOutcome::Unsupported;
    }

private:
    static bool IsNameStart(char character)
    {
        const auto value = static_cast<unsigned char>(character);
        return std::isalpha(value) != 0 || character == '_' || character == ':' ||
            value >= 0x80;
    }

    static bool IsNameChar(char character)
    {
        const auto value = static_cast<unsigned char>(character);
        return std::isalnum(value) != 0 || character == '_' || character == ':' ||
            character == '-' || character == '.' || value >= 0x80;
    }

    static bool IsSpace(char character)
    {
        const auto value = static_cast<unsigned char>(character);
        return std::isspace(value) != 0;
    }

    // A namespace-prefixed name is ambiguous without resolving the prefix, and
    // OpenColorIO's reader does not resolve prefixes, so any ':' makes the file
    // unsupported rather than guessing at the local name.
    static bool HasPrefix(const std::string& name)
    {
        return name.find(':') != std::string::npos;
    }

    void SkipSpace()
    {
        while (m_position < m_text.size() && IsSpace(m_text[m_position]))
        {
            ++m_position;
        }
    }

    bool SkipTo(const char* terminator)
    {
        const size_t at = m_text.find(terminator, m_position);
        if (at == std::string::npos)
        {
            m_position = m_text.size();
            return false;
        }
        m_position = at + std::strlen(terminator);
        return true;
    }

    bool ReadName(std::string& name)
    {
        if (m_position >= m_text.size() || !IsNameStart(m_text[m_position]))
        {
            return false;
        }
        const size_t start = m_position;
        while (m_position < m_text.size() && IsNameChar(m_text[m_position]))
        {
            ++m_position;
        }
        name.assign(m_text, start, m_position - start);
        return true;
    }

    bool ReadElement(
        std::string& name,
        std::vector<std::pair<std::string, std::string>>& attributes,
        bool& empty)
    {
        name.clear();
        attributes.clear();
        empty = false;
        if (!ReadName(name))
        {
            return false;
        }

        while (m_position < m_text.size())
        {
            const bool hadSpace = IsSpace(m_text[m_position]);
            SkipSpace();
            if (m_position >= m_text.size())
            {
                return false;
            }

            const char character = m_text[m_position];
            if (character == '/')
            {
                ++m_position;
                if (m_position < m_text.size() && m_text[m_position] == '>')
                {
                    ++m_position;
                    empty = true;
                    return true;
                }
                return false;
            }
            if (character == '>')
            {
                ++m_position;
                return true;
            }
            if (!hadSpace || !IsNameStart(character))
            {
                return false;
            }

            std::string attributeName;
            if (!ReadName(attributeName) || HasPrefix(attributeName))
            {
                return false;
            }

            SkipSpace();
            if (m_position >= m_text.size() || m_text[m_position] != '=')
            {
                return false;
            }
            ++m_position;
            SkipSpace();
            if (m_position >= m_text.size())
            {
                return false;
            }

            const char quote = m_text[m_position];
            if (quote != '"' && quote != '\'')
            {
                return false;
            }
            ++m_position;
            const size_t valueStart = m_position;
            const size_t valueEnd = m_text.find(quote, m_position);
            if (valueEnd == std::string::npos)
            {
                return false;
            }
            const std::string raw(m_text, valueStart, valueEnd - valueStart);
            m_position = valueEnd + 1;

            std::string decoded;
            if (!DecodeEntities(raw, decoded))
            {
                return false;
            }
            attributes.emplace_back(std::move(attributeName), std::move(decoded));
        }

        return false;
    }

    // Appends one Unicode scalar value as UTF-8.
    static bool AppendUtf8(std::string& destination, unsigned long code)
    {
        if (code == 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF))
        {
            return false;
        }
        if (code < 0x80)
        {
            destination.push_back(static_cast<char>(code));
        }
        else if (code < 0x800)
        {
            destination.push_back(static_cast<char>(0xC0 | (code >> 6)));
            destination.push_back(static_cast<char>(0x80 | (code & 0x3F)));
        }
        else if (code < 0x10000)
        {
            destination.push_back(static_cast<char>(0xE0 | (code >> 12)));
            destination.push_back(static_cast<char>(0x80 | ((code >> 6) & 0x3F)));
            destination.push_back(static_cast<char>(0x80 | (code & 0x3F)));
        }
        else
        {
            destination.push_back(static_cast<char>(0xF0 | (code >> 18)));
            destination.push_back(static_cast<char>(0x80 | ((code >> 12) & 0x3F)));
            destination.push_back(static_cast<char>(0x80 | ((code >> 6) & 0x3F)));
            destination.push_back(static_cast<char>(0x80 | (code & 0x3F)));
        }
        return true;
    }

    // Decodes the five predefined entities and numeric character references of any
    // length. Anything else -- a general entity, an unterminated reference -- fails,
    // because substituting nothing or the literal text would produce a file name the
    // document does not name.
    //
    // Literal whitespace is normalized first, as XML requires: the document's line ends
    // are normalized to LF, and every literal tab, line feed, and carriage return in an
    // attribute value becomes a single space. A character reference is *not* normalized,
    // which is exactly why the two steps cannot be merged. Skipping this made a path
    // written across a line break resolve to a name with a newline in it, while
    // OpenColorIO -- whose reader normalizes -- opened a name with a space.
    static bool DecodeEntities(const std::string& value, std::string& result)
    {
        result.clear();
        result.reserve(value.size());
        size_t index = 0;
        while (index < value.size())
        {
            const char character = value[index];
            if (character == '\r')
            {
                // CR and CRLF both normalize to one LF, and that LF to one space.
                result.push_back(' ');
                ++index;
                if (index < value.size() && value[index] == '\n')
                {
                    ++index;
                }
                continue;
            }
            if (character == '\n' || character == '\t')
            {
                result.push_back(' ');
                ++index;
                continue;
            }
            if (character != '&')
            {
                result.push_back(character);
                ++index;
                continue;
            }
            const size_t end = value.find(';', index + 1);
            if (end == std::string::npos)
            {
                return false;
            }
            const std::string entity = value.substr(index + 1, end - index - 1);
            index = end + 1;
            if (entity == "amp")
            {
                result.push_back('&');
                continue;
            }
            if (entity == "lt")
            {
                result.push_back('<');
                continue;
            }
            if (entity == "gt")
            {
                result.push_back('>');
                continue;
            }
            if (entity == "quot")
            {
                result.push_back('"');
                continue;
            }
            if (entity == "apos")
            {
                result.push_back('\'');
                continue;
            }
            if (entity.size() < 2 || entity[0] != '#')
            {
                return false;
            }

            const bool hexadecimal = entity[1] == 'x' || entity[1] == 'X';
            const std::string digits = entity.substr(hexadecimal ? 2 : 1);
            if (digits.empty() || digits.size() > 8)
            {
                return false;
            }
            for (const char digit : digits)
            {
                const auto value8 = static_cast<unsigned char>(digit);
                const bool valid = hexadecimal
                    ? std::isxdigit(value8) != 0
                    : std::isdigit(value8) != 0;
                if (!valid)
                {
                    return false;
                }
            }
            const unsigned long code = std::strtoul(
                digits.c_str(),
                nullptr,
                hexadecimal ? 16 : 10);
            if (!AppendUtf8(result, code))
            {
                return false;
            }
        }

        return true;
    }

    // Character data may contain entity references too. Only the predefined and numeric
    // ones are understood; anything else means the document uses a construct this reader
    // does not model, and the file must not be called exhaustive.
    static bool IsSupportedText(const std::string& text, size_t begin, size_t end)
    {
        for (size_t index = begin; index < end; ++index)
        {
            if (text[index] != '&')
            {
                continue;
            }
            const size_t stop = text.find(';', index + 1);
            if (stop == std::string::npos || stop >= end)
            {
                return false;
            }
            std::string ignored;
            if (!DecodeEntities(text.substr(index, stop - index + 1), ignored))
            {
                return false;
            }
            index = stop;
        }
        return true;
    }

    const std::string& m_text;
    size_t m_position = 0;
};

// Extracts the referenced paths from a complete CTF/CLF document.
//
// A <Reference> element names another transform file, which may itself reference more,
// so the dependency graph is genuinely transitive and hashing only the directly named
// LUT would miss an edit two levels down.
//
// The basePath attribute is deliberately ignored. OpenColorIO's CTF reader accepts and
// discards it, resolving the reference against its own search path instead, so honouring
// it here would make the identity depend on a file OpenColorIO would never read.
XmlOutcome ExtractReferences(
    const std::string& text,
    std::vector<std::string>& references)
{
    XmlScanner scanner(text);
    const size_t before = references.size();
    const XmlOutcome outcome = scanner.CollectReferences(references);
    if (outcome != XmlOutcome::Complete)
    {
        references.resize(before);
    }
    return outcome;
}

struct DependencyFile
{
    std::string key;
    std::filesystem::path directory;
};

// Returns true when the bytes begin an XML document: an optional UTF-8 byte-order mark,
// optional XML whitespace, then '<'.
//
// The format is decided by content, never by the file's name. OpenColorIO falls back to
// trying its readers when an extension does not identify a format, so a valid process
// list called "grade.lut" -- or reached through a link whose name ends in anything at
// all -- is loaded as CTF and can reference further files. An extension test missed
// exactly those and reported the identity as exhaustive while ignoring real
// dependencies.
bool LooksLikeXml(const char* data, size_t size)
{
    size_t index = 0;
    if (size >= 3 &&
        static_cast<unsigned char>(data[0]) == 0xEF &&
        static_cast<unsigned char>(data[1]) == 0xBB &&
        static_cast<unsigned char>(data[2]) == 0xBF)
    {
        index = 3;
    }
    for (; index < size; ++index)
    {
        const char character = data[index];
        if (character == ' ' || character == '\t' || character == '\r' ||
            character == '\n')
        {
            continue;
        }
        return character == '<';
    }
    return false;
}

// Hashes a file's contents and, when the content is an XML process list, reports the
// paths it names.
//
// Contents, not metadata: an edited LUT of the same length written inside the same clock
// tick has the same size and the same modification time, which is precisely the change
// OpenColorIO's own mtime-and-inode cache identity cannot see, and precisely the change
// that alters every rendered pixel.
//
// Returns false when the file cannot be accounted for exhaustively, which makes the whole
// identity partial and the transform refused.
bool HashFileContents(
    const std::string& path,
    uint64_t& hash,
    uint64_t& budget,
    std::vector<std::string>& references)
{
    std::ifstream stream = OpenBinary(path);
    if (!stream)
    {
        // A missing dependency is itself an identity: it must differ from the same
        // config with the file present.
        hash = HashString(hash, std::string("<missing>"));
        return true;
    }

    std::vector<char> buffer(64 * 1024);
    stream.read(buffer.data(), static_cast<std::streamsize>(buffer.size()));
    const auto first = static_cast<size_t>(stream.gcount());
    if (first == 0)
    {
        hash = HashString(hash, std::string("<empty>"));
        return true;
    }

    if (!LooksLikeXml(buffer.data(), first))
    {
        // A leaf LUT. Streamed rather than buffered, because a sampled 3D LUT can be
        // large and nothing in it needs to be parsed.
        size_t read = first;
        while (true)
        {
            if (budget < read)
            {
                hash = HashString(hash, std::string("<truncated>"));
                budget = 0;
                return false;
            }
            budget -= read;
            hash = HashBytes(hash, buffer.data(), read);
            if (stream.eof())
            {
                break;
            }
            stream.read(buffer.data(), static_cast<std::streamsize>(buffer.size()));
            read = static_cast<size_t>(stream.gcount());
            if (read == 0)
            {
                break;
            }
        }
        return true;
    }

    // An XML document is parsed as a whole rather than through a sliding window. A
    // window cannot see an element that straddles its boundary and re-reports every
    // element inside its overlap, and an XML document has no safe place to be cut.
    std::string document;
    uint64_t allowance = std::min<uint64_t>(budget, MaximumReferenceDocumentBytes);
    size_t read = first;
    while (true)
    {
        if (allowance < read)
        {
            hash = HashString(hash, std::string("<truncated>"));
            budget -= std::min<uint64_t>(budget, document.size());
            return false;
        }
        allowance -= read;
        document.append(buffer.data(), read);
        if (stream.eof())
        {
            break;
        }
        stream.read(buffer.data(), static_cast<std::streamsize>(buffer.size()));
        read = static_cast<size_t>(stream.gcount());
        if (read == 0)
        {
            break;
        }
    }

    budget -= std::min<uint64_t>(budget, document.size());
    hash = HashBytes(hash, document.data(), document.size());
    return ExtractReferences(document, references) == XmlOutcome::Complete;
}

void CollectFileTransforms(
    const OCIO::ConstTransformRcPtr& transform,
    std::vector<std::string>& sources)
{
    if (!transform)
    {
        return;
    }

    if (auto group = OCIO::DynamicPtrCast<const OCIO::GroupTransform>(transform))
    {
        const int count = group->getNumTransforms();
        for (int i = 0; i < count; ++i)
        {
            CollectFileTransforms(group->getTransform(i), sources);
        }
        return;
    }

    if (auto file = OCIO::DynamicPtrCast<const OCIO::FileTransform>(transform))
    {
        const char* src = file->getSrc();
        if (src != nullptr && src[0] != '\0')
        {
            sources.emplace_back(src);
        }
    }
}

void CollectConfigFileTransforms(
    const OCIO::ConstConfigRcPtr& config,
    std::vector<std::string>& sources)
{
    const int colorSpaces = config->getNumColorSpaces();
    for (int i = 0; i < colorSpaces; ++i)
    {
        OCIO::ConstColorSpaceRcPtr space =
            config->getColorSpace(config->getColorSpaceNameByIndex(i));
        if (!space)
        {
            continue;
        }
        CollectFileTransforms(
            space->getTransform(OCIO::COLORSPACE_DIR_TO_REFERENCE),
            sources);
        CollectFileTransforms(
            space->getTransform(OCIO::COLORSPACE_DIR_FROM_REFERENCE),
            sources);
    }

    const int looks = config->getNumLooks();
    for (int i = 0; i < looks; ++i)
    {
        OCIO::ConstLookRcPtr look = config->getLook(config->getLookNameByIndex(i));
        if (!look)
        {
            continue;
        }
        CollectFileTransforms(look->getTransform(), sources);
        CollectFileTransforms(look->getInverseTransform(), sources);
    }

    const int viewTransforms = config->getNumViewTransforms();
    for (int i = 0; i < viewTransforms; ++i)
    {
        OCIO::ConstViewTransformRcPtr view =
            config->getViewTransform(config->getViewTransformNameByIndex(i));
        if (!view)
        {
            continue;
        }
        CollectFileTransforms(
            view->getTransform(OCIO::VIEWTRANSFORM_DIR_TO_REFERENCE),
            sources);
        CollectFileTransforms(
            view->getTransform(OCIO::VIEWTRANSFORM_DIR_FROM_REFERENCE),
            sources);
    }

    const int namedTransforms = config->getNumNamedTransforms();
    for (int i = 0; i < namedTransforms; ++i)
    {
        OCIO::ConstNamedTransformRcPtr named = config->getNamedTransform(
            config->getNamedTransformNameByIndex(i));
        if (!named)
        {
            continue;
        }
        CollectFileTransforms(
            named->getTransform(OCIO::TRANSFORM_DIR_FORWARD),
            sources);
        CollectFileTransforms(
            named->getTransform(OCIO::TRANSFORM_DIR_INVERSE),
            sources);
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
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
    // ABI_OUTPUT_INITIALIZATION
    ResetAbiOutput(processor);
    if (processor == nullptr)
    {
        WriteError(error, "processor output pointer must not be null");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
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
            // An explicit look override replaces the view's own looks; it does not
            // compose with them. It also has to be evaluated in each look's declared
            // process space and converted back through its result space, which is
            // exactly what the viewing pipeline does and what applying a LookTransform
            // with skipColorSpaceConversion in the source space did not do. Composing a
            // source-space look in front of a view that still applied its own looks
            // produced a plausible-but-wrong image for every config whose look process
            // space differs from the source space, or whose view declares a look.
            auto pipeline = OCIO::LegacyViewingPipeline::Create();
            pipeline->setDisplayViewTransform(dvt);
            pipeline->setLooksOverrideEnabled(true);
            pipeline->setLooksOverride(looks);
            proc = pipeline->getProcessor(config, config->getCurrentContext());
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
    });
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
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
    // ABI_OUTPUT_INITIALIZATION
    if (destination != nullptr && destination_size > 0)
    {
        std::memset(destination, 0, destination_size);
    }
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
    });
}

OPENUSD_DOTNET_API openusd_status openusd_ocio_processor_apply_rgba32f_to_rgba8(
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
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
    // ABI_OUTPUT_INITIALIZATION
    if (destination != nullptr && destination_size > 0)
    {
        std::memset(destination, 0, destination_size);
    }
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
    constexpr size_t SourceBytesPerPixel = ChannelsPerPixel * sizeof(float);
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
        pixelCount > std::numeric_limits<size_t>::max() / DestinationBytesPerPixel)
    {
        WriteError(error, "image byte count overflows size_t");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (source_size != pixelCount * SourceBytesPerPixel)
    {
        WriteError(error, "source_size does not match width * height * 16");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (destination_size != pixelCount * DestinationBytesPerPixel)
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

        std::vector<float> scratch(pixelCount * ChannelsPerPixel);
        for (size_t i = 0; i < pixelCount; ++i)
        {
            const size_t si = i * ChannelsPerPixel;
            float channels[ChannelsPerPixel];
            std::memcpy(
                channels,
                source + (i * SourceBytesPerPixel),
                SourceBytesPerPixel);
            const float r = channels[0] * exposureScale;
            const float g = channels[1] * exposureScale;
            const float b = channels[2] * exposureScale;
            const float a = channels[3];
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
    });
}

OPENUSD_DOTNET_API openusd_status openusd_ocio_config_cache_id(
    const char* config_path,
    char* destination,
    size_t capacity,
    size_t* required,
    int* exhaustive,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
    // ABI_OUTPUT_INITIALIZATION
    ResetAbiOutput(required);
    ResetAbiOutput(exhaustive);
    if (destination != nullptr && capacity > 0)
    {
        destination[0] = '\0';
    }
    if (required == nullptr || exhaustive == nullptr)
    {
        WriteError(error, "required and exhaustive output pointers must not be null");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (config_path == nullptr || config_path[0] == '\0')
    {
        WriteError(error, "config_path must not be null or empty");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    try
    {
        // The parsed config is discarded immediately: what is wanted is a fresh
        // observation of the config and of the files it references, so an identity
        // that outlived an edit would defeat the purpose of asking.
        OCIO::ConstConfigRcPtr config = OCIO::Config::CreateFromFile(config_path);
        const char* cacheId = config->getCacheID(config->getCurrentContext());
        if (cacheId == nullptr)
        {
            WriteError(error, "OpenColorIO returned no config cache identity");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }

        // OpenColorIO's own identity folds in external references through the file
        // system's modification time and inode. That is not enough: a LUT rewritten to
        // the same length inside the same clock tick keeps both, and changes every
        // rendered pixel. The walk below hashes the bytes of the config and of every
        // file reachable from it -- including the files a CTF or CLF transform
        // references, transitively -- so the identity is content-driven.
        OCIO::ConstContextRcPtr context = config->getCurrentContext();
        const std::filesystem::path configPath = ToPath(std::string(config_path));
        ++g_dependencyWalks;

        std::set<std::string> visited;
        std::vector<DependencyFile> ordered;
        bool exhaustiveWalk = true;

        auto enqueue = [&](const std::filesystem::path& candidate) -> bool
        {
            std::string key = CanonicalKey(candidate);
            if (!visited.insert(key).second)
            {
                // Already seen under this or another spelling. This is what terminates
                // an alias cycle and what stops one file being weighed twice against
                // the bounds.
                return false;
            }
            if (ordered.size() >= MaximumDependencyFiles)
            {
                exhaustiveWalk = false;
                return false;
            }
            std::filesystem::path resolved = ToPath(key);
            ordered.push_back(DependencyFile{key, resolved.parent_path()});
            return true;
        };

        // Resolves one reference the way OpenColorIO does: through the config's own
        // unchanged context, exactly as written.
        //
        // An earlier attempt prepended the referencing document's directory to the search
        // path. That is wrong. OpenColorIO resolves a nested reference through the very
        // same context as every other file reference, so augmenting the search path makes
        // the identity depend on a file OpenColorIO would never open -- and when a
        // same-named file exists both beside the referencing document and on the search
        // path, it hashes the wrong one and the identity stops tracking the image.
        // Falling back to the name keeps an unresolvable reference in the identity, so a
        // later resolution is a change rather than a silent match.
        auto resolveThroughContext = [&](
            const std::string& source) -> std::filesystem::path
        {
            try
            {
                const char* location = context->resolveFileLocation(source.c_str());
                if (location != nullptr && location[0] != '\0')
                {
                    return ToPath(std::string(location));
                }
            }
            catch (const OCIO::Exception&)
            {
                // An unresolvable reference is part of the identity too: it must not
                // compare equal to the same config with the reference present.
            }

            std::filesystem::path fallback = ToPath(source);
            if (fallback.is_relative())
            {
                fallback = configPath.parent_path() / fallback;
            }
            return fallback;
        };

        enqueue(configPath);

        std::vector<std::string> sources;
        CollectConfigFileTransforms(config, sources);
        std::sort(sources.begin(), sources.end());
        sources.erase(std::unique(sources.begin(), sources.end()), sources.end());
        for (const std::string& source : sources)
        {
            enqueue(resolveThroughContext(source));
        }

        uint64_t digest = 1469598103934665603ull;
        digest = HashString(digest, std::string(cacheId));
        uint64_t budget = MaximumDependencyBytes;

        // Indexed rather than iterated: the walk appends transitively discovered files
        // while it runs, and every one of them has to be hashed too.
        for (size_t index = 0; index < ordered.size(); ++index)
        {
            const DependencyFile entry = ordered[index];
            digest = HashString(digest, entry.key);
            std::vector<std::string> references;
            if (!HashFileContents(entry.key, digest, budget, references))
            {
                exhaustiveWalk = false;
            }

            for (const std::string& reference : references)
            {
                enqueue(resolveThroughContext(reference));
            }
        }

        char identity[96];
        const int written = std::snprintf(
            identity,
            sizeof(identity),
            "ocio:%016llx",
            static_cast<unsigned long long>(digest));
        if (written <= 0 || static_cast<size_t>(written) >= sizeof(identity))
        {
            WriteError(error, "the config identity could not be formatted");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }

        *exhaustive = exhaustiveWalk ? 1 : 0;
        const size_t length = static_cast<size_t>(written);
        *required = length + 1;
        if (destination == nullptr || capacity < length + 1)
        {
            return OPENUSD_STATUS_BUFFER_TOO_SMALL;
        }
        std::memcpy(destination, identity, length);
        destination[length] = '\0';
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
    });
}

OPENUSD_DOTNET_API openusd_status openusd_ocio_config_dependency_walks(
    uint64_t* walks,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
    // ABI_OUTPUT_INITIALIZATION
    ResetAbiOutput(walks);
    if (walks == nullptr)
    {
        WriteError(error, "walks output pointer must not be null");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    *walks = g_dependencyWalks.load(std::memory_order_relaxed);
    return OPENUSD_STATUS_OK;
    });
}

OPENUSD_DOTNET_API openusd_status openusd_ocio_clear_caches(
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
    try
    {
        OCIO::ClearAllCaches();
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
    });
}

OPENUSD_DOTNET_API void openusd_ocio_processor_release(
    openusd_ocio_processor* processor)
{
    delete processor;
}

}  // extern "C"
