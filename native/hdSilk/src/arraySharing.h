// Copyright (c) marcschier. Licensed under the MIT License.
#ifndef HDSILK_ARRAY_SHARING_H
#define HDSILK_ARRAY_SHARING_H

#include <cstddef>
#include <cstdint>
#include <unordered_map>
#include <vector>

// Arrays must be copy-on-write. The owning sync scope serializes access and clears the index.
template <typename Array, size_t MaximumEntries = 65536, size_t MaximumCandidates = 8>
class HdSilkArraySharing final
{
public:
    template <typename Equal>
    bool Intern(Array& value, uint64_t hash, Equal equal)
    {
        if (value.empty()) { return false; }
        auto found = _buckets.find(hash);
        if (found != _buckets.end())
        {
            for (const Array& candidate : found->second)
            {
                if (equal(value, candidate))
                {
                    value = candidate;
                    return true;
                }
            }
        }
        if (_entries >= MaximumEntries || MaximumCandidates == 0 ||
            (found != _buckets.end() && found->second.size() >= MaximumCandidates))
        {
            return false;
        }
        if (found == _buckets.end())
        {
            _buckets.emplace(hash, std::vector<Array>{value});
        }
        else
        {
            found->second.push_back(value);
        }
        ++_entries;
        return false;
    }

    void Clear()
    {
        _buckets.clear();
        _entries = 0;
    }

    size_t EntryCount() const { return _entries; }

private:
    std::unordered_map<uint64_t, std::vector<Array>> _buckets;
    size_t _entries = 0;
};

#endif
