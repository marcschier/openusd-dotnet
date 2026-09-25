// Copyright (c) marcschier. Licensed under the MIT License.
#ifndef HDSILK_MESH_PREPARATION_H
#define HDSILK_MESH_PREPARATION_H

#include <atomic>
#include <cstdint>
#include <cstdio>
#include <limits>
#include <memory>
#include <new>
#include <stdexcept>
#include <utility>

class HdSilkMeshPreparationExceeded final : public std::bad_alloc
{
public:
    explicit HdSilkMeshPreparationExceeded(const char* message) noexcept
    {
        std::snprintf(_message, sizeof(_message), "%s", message);
    }

    HdSilkMeshPreparationExceeded(uint64_t requested, uint64_t reserved, uint64_t limit) noexcept
    {
        std::snprintf(_message, sizeof(_message),
            "hdSilk mesh preparation refused %llu reservation bytes with %llu already reserved "
            "(limit %llu). No command page was published.",
            static_cast<unsigned long long>(requested), static_cast<unsigned long long>(reserved),
            static_cast<unsigned long long>(limit));
    }

    const char* what() const noexcept override { return _message; }

private:
    char _message[256]{};
};

struct HdSilkMeshPreparationPlan
{
    uint64_t bytes = 0;
    uint64_t vertices = 0;

    static uint64_t Add(uint64_t left, uint64_t right)
    {
        if (right > std::numeric_limits<uint64_t>::max() - left)
        {
            throw std::overflow_error("The hdSilk mesh preparation reservation overflows uint64.");
        }
        return left + right;
    }

    static uint64_t Multiply(uint64_t count, uint64_t width)
    {
        if (width != 0 && count > std::numeric_limits<uint64_t>::max() / width)
        {
            throw std::overflow_error("The hdSilk mesh preparation reservation overflows uint64.");
        }
        return count * width;
    }

    HdSilkMeshPreparationPlan(uint64_t points, uint64_t triangles)
    {
        const uint64_t corners = Multiply(triangles, 3);
        vertices = points > corners ? points : corners;
        // Logical payload reservations for conversion/emission, triangulation,
        // coarse topology, remapping/identity tables and referenced-point flags.
        // Reserve the worst corner layout even when sharing reduces the result.
        bytes = Add(Multiply(Add(points, vertices), 3 * sizeof(float)),
            Multiply(Add(Multiply(corners, 9), Multiply(triangles, 2)), sizeof(uint32_t)));
        bytes = Add(bytes, Add(Multiply(points, sizeof(uint32_t)), points));
    }

    void Attribute(uint64_t elements, uint32_t components, bool constant)
    {
        const uint64_t source = Multiply(elements, components);
        const uint64_t emitted = constant ? source : Multiply(vertices, components);
        // Pending flattened values and up to three triangulated/expanded/compact
        // buffers may overlap. Source Vt storage and allocator overhead are not this metric.
        bytes = Add(bytes, Multiply(Add(source, Multiply(emitted, 3)), sizeof(float)));
    }
};

class HdSilkMeshPreparationBudget;

class HdSilkMeshPreparationLease final
{
public:
    HdSilkMeshPreparationLease(std::shared_ptr<HdSilkMeshPreparationBudget> budget, uint64_t bytes);
    ~HdSilkMeshPreparationLease();
    HdSilkMeshPreparationLease(const HdSilkMeshPreparationLease&) = delete;
    HdSilkMeshPreparationLease& operator=(const HdSilkMeshPreparationLease&) = delete;
    void RetainPrevious(const std::shared_ptr<HdSilkMeshPreparationLease>& previous) { _previous = previous; }
    void Complete() noexcept { _previous.reset(); }

private:
    std::shared_ptr<HdSilkMeshPreparationBudget> _budget;
    uint64_t _bytes;
    std::shared_ptr<HdSilkMeshPreparationLease> _previous;
};

class HdSilkMeshPreparationBudget final : public std::enable_shared_from_this<HdSilkMeshPreparationBudget>
{
public:
    // Configuration changes only while the session owns its sync lock and no
    // Hydra workers are active. Live leases include both Rprim and retained-record owners.
    void Configure(uint64_t limit)
    {
        const uint64_t reserved = _reserved.load();
        if (limit != 0 && reserved > limit)
        {
            throw HdSilkMeshPreparationExceeded(0, reserved, limit);
        }
        _limit.store(limit);
        _peak.store(reserved);
        _refused.store(false);
        _failure[0] = '\0';
    }

    bool Enabled() const { return _limit.load() != 0; }
    uint64_t Limit() const { return _limit.load(); }
    uint64_t Reserved() const { return _reserved.load(); }
    uint64_t Peak() const { return _peak.load(); }
    bool Refused() const { return _refused.load(); }

    void Refuse(const char* message) noexcept
    {
        bool expected = false;
        if (_refused.compare_exchange_strong(expected, true))
        {
            std::snprintf(_failure, sizeof(_failure), "%s", message);
        }
    }

    // Called only after the Hydra worker barrier. Exceptions must not escape
    // Rprim Sync through USD's worker pool; refusal aborts publication on the caller.
    void ThrowIfRefused() const
    {
        if (_refused.load()) { throw HdSilkMeshPreparationExceeded(_failure); }
    }

    std::shared_ptr<HdSilkMeshPreparationLease> Acquire(uint64_t bytes)
    {
        return Enabled() ? std::make_shared<HdSilkMeshPreparationLease>(shared_from_this(), bytes) : nullptr;
    }

private:
    friend class HdSilkMeshPreparationLease;

    void Reserve(uint64_t bytes)
    {
        const uint64_t limit = _limit.load();
        uint64_t previous = _reserved.load();
        for (;;)
        {
            if (bytes > limit || previous > limit - bytes)
            {
                throw HdSilkMeshPreparationExceeded(bytes, previous, limit);
            }
            if (_reserved.compare_exchange_weak(previous, previous + bytes))
            {
                break;
            }
        }
        const uint64_t current = previous + bytes;
        uint64_t peak = _peak.load();
        while (peak < current && !_peak.compare_exchange_weak(peak, current)) {}
    }

    std::atomic<uint64_t> _limit{0};
    std::atomic<uint64_t> _reserved{0};
    std::atomic<uint64_t> _peak{0};
    std::atomic<bool> _refused{false};
    char _failure[256]{};
};

inline HdSilkMeshPreparationLease::HdSilkMeshPreparationLease(
    std::shared_ptr<HdSilkMeshPreparationBudget> budget, uint64_t bytes)
    : _budget(std::move(budget)), _bytes(bytes)
{
    _budget->Reserve(bytes);
}

inline HdSilkMeshPreparationLease::~HdSilkMeshPreparationLease()
{
    _budget->_reserved.fetch_sub(_bytes);
}

#endif
