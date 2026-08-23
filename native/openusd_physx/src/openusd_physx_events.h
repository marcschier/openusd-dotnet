// Copyright (c) marcschier. Licensed under the MIT License.

// Event ordering, bounded retention, command validation, and query validation
// for the retained world.
//
// Nothing in this translation unit depends on PhysX or on OpenUSD, so the
// deterministic ordering rules, the overflow policy, and the command and query
// argument rules are compiled and executed by a probe that does not need the
// simulation SDK. The world implementation is the only caller; it owns the
// PhysX side and delegates every policy decision here.
//
// Determinism contract
// --------------------
// * Events of one step are reported in one total order that depends only on the
//   events themselves: step index, then type, then id0, id1, detail0, detail1.
//   The order never depends on the worker thread count or on the order PhysX
//   happened to report a pair in.
// * Overflow keeps the deterministic prefix. A sink that is full compares a new
//   event against the largest event it currently holds and keeps the smaller
//   one, so the retained set is always the first N events of the total order no
//   matter which order they arrived in. The remainder is counted, never
//   allocated.
// * Query hits of one request are ordered by distance, then actor identity,
//   then shape identity, then face index, and overflow keeps the nearest hits.

#ifndef OPENUSD_PHYSX_EVENTS_H
#define OPENUSD_PHYSX_EVENTS_H

#include "openusd_physx_world.h"

#include <cstddef>
#include <string>
#include <vector>

namespace openusd_physx_events
{
// Returns a negative, zero, or positive value when left orders before, equal
// to, or after right in the deterministic event order.
int CompareEvents(const openusd_physx_event& left, const openusd_physx_event& right) noexcept;

// Strict weak ordering built on CompareEvents.
bool EventLess(const openusd_physx_event& left, const openusd_physx_event& right) noexcept;

// Strict weak ordering over the hits of one request: nearest first, then by
// stable identity so that two hits at the same distance never swap places.
bool HitLess(const openusd_physx_query_hit& left, const openusd_physx_query_hit& right) noexcept;

// Bounded, deterministic event buffer.
//
// Reserve allocates once, at build time, from the capacity the build page
// declares. Retain never allocates and never reallocates, so a step that
// produces far more events than the declared capacity costs no memory and no
// warm path allocation, only a bounded comparison per event.
class EventSink
{
public:
    // Sizes the sink. Called once per build; never called from a step.
    void Reserve(uint32_t capacity);

    // Drops every retained event and every dropped count, keeping the memory.
    void Reset() noexcept;

    // Retains the event when it belongs to the deterministic prefix, otherwise
    // counts it as dropped. Never allocates.
    void Retain(const openusd_physx_event& event) noexcept;

    // Orders the retained events. Called once, after a step has collected
    // everything, before the caller owned result page is filled.
    void Sort() noexcept;

    const openusd_physx_event* Data() const noexcept
    {
        return entries_.empty() ? nullptr : entries_.data();
    }

    size_t Size() const noexcept
    {
        return entries_.size();
    }

    size_t Capacity() const noexcept
    {
        return capacity_;
    }

    uint32_t Dropped() const noexcept
    {
        return dropped_;
    }

    bool Overflowed() const noexcept
    {
        return dropped_ != 0;
    }

private:
    std::vector<openusd_physx_event> entries_;
    size_t capacity_ = 0;
    uint32_t dropped_ = 0;
};

// Bounded, deterministic hit buffer over one contiguous region of the caller
// owned hit array. The sink never allocates: it treats the region as a heap
// keyed by HitLess, so a request whose touch count exceeds its budget keeps the
// nearest hits and counts the rest.
class HitSink
{
public:
    HitSink(openusd_physx_query_hit* hits, size_t capacity) noexcept;

    // Retains the hit when it is nearer than the farthest retained hit,
    // otherwise counts it as dropped.
    void Retain(const openusd_physx_query_hit& hit) noexcept;

    // Orders the retained hits nearest first.
    void Sort() noexcept;

    size_t Size() const noexcept
    {
        return size_;
    }

    size_t Capacity() const noexcept
    {
        return capacity_;
    }

    size_t Dropped() const noexcept
    {
        return dropped_;
    }

private:
    openusd_physx_query_hit* hits_;
    size_t capacity_;
    size_t size_ = 0;
    size_t dropped_ = 0;
};

// The command flags one command type accepts. A command that declares any other
// flag is rejected.
uint32_t AllowedCommandFlags(uint32_t command_type) noexcept;

// Validates one command of a batch. A false result fills reason with a message
// that names the offending command index.
bool ValidateCommand(const openusd_physx_command& command, size_t index, std::string& reason);

// Resolves the effective vector of a command, applying the magnitude modifier
// when it is set. Only call this after ValidateCommand accepted the command.
openusd_physx_vec3f ResolveCommandVector(const openusd_physx_command& command) noexcept;

// Validates one query request against the number of scenes the world holds. A
// false result fills reason with the message reported as a query diagnostic.
bool ValidateQueryRequest(
    const openusd_physx_query_request& request,
    size_t scene_count,
    std::string& reason);
}

#endif
