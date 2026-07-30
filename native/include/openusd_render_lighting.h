// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_RENDER_LIGHTING_H
#define OPENUSD_RENDER_LIGHTING_H

#include <stddef.h>
#include <stdint.h>

#define OPENUSD_RENDER_HEADLIGHT_VERSION 1u

/// Shared deterministic headlight ABI for Storm and hdSilk parity rendering.
/// Callers must set struct_size to sizeof(openusd_render_headlight) when a
/// structure is passed into native code. Natural platform layout is part of the
/// ABI and must not be packed.
typedef struct openusd_render_headlight
{
    uint32_t struct_size;
    uint32_t version;
    float direction[3];
    float intensity;
    float color[3];
    float ambient;
} openusd_render_headlight;

#if defined(__cplusplus)
static_assert(alignof(openusd_render_headlight) == alignof(float));
static_assert(offsetof(openusd_render_headlight, struct_size) == 0);
static_assert(offsetof(openusd_render_headlight, version) == 4);
static_assert(offsetof(openusd_render_headlight, direction) == 8);
static_assert(offsetof(openusd_render_headlight, intensity) == 20);
static_assert(offsetof(openusd_render_headlight, color) == 24);
static_assert(offsetof(openusd_render_headlight, ambient) == 36);
static_assert(sizeof(openusd_render_headlight) == 40);
#elif defined(__STDC_VERSION__) && __STDC_VERSION__ >= 201112L
_Static_assert(offsetof(openusd_render_headlight, struct_size) == 0, "invalid struct_size offset");
_Static_assert(offsetof(openusd_render_headlight, version) == 4, "invalid version offset");
_Static_assert(offsetof(openusd_render_headlight, direction) == 8, "invalid direction offset");
_Static_assert(offsetof(openusd_render_headlight, intensity) == 20, "invalid intensity offset");
_Static_assert(offsetof(openusd_render_headlight, color) == 24, "invalid color offset");
_Static_assert(offsetof(openusd_render_headlight, ambient) == 36, "invalid ambient offset");
_Static_assert(sizeof(openusd_render_headlight) == 40, "invalid headlight size");
#endif

#endif
