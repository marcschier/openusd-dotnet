// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_PHYSX_H
#define OPENUSD_PHYSX_H

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#if defined(OPENUSD_PHYSX_BUILD)
#define OPENUSD_PHYSX_API __declspec(dllexport)
#else
#define OPENUSD_PHYSX_API __declspec(dllimport)
#endif
#else
#define OPENUSD_PHYSX_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef enum openusd_physx_status
{
    OPENUSD_PHYSX_STATUS_OK = 0,
    OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT = 1,
    OPENUSD_PHYSX_STATUS_BUFFER_TOO_SMALL = 2,
    OPENUSD_PHYSX_STATUS_NATIVE_ERROR = 3
} openusd_physx_status;

typedef struct openusd_physx_error_buffer
{
    char* data;
    size_t capacity;
    size_t required;
} openusd_physx_error_buffer;

typedef struct openusd_physx_scene openusd_physx_scene;

typedef struct openusd_physx_vec3f
{
    float x;
    float y;
    float z;
} openusd_physx_vec3f;

typedef struct openusd_physx_quatf
{
    float x;
    float y;
    float z;
    float w;
} openusd_physx_quatf;

typedef struct openusd_physx_transform
{
    openusd_physx_vec3f position;
    openusd_physx_quatf rotation;
} openusd_physx_transform;

OPENUSD_PHYSX_API openusd_physx_status openusd_physx_get_version(
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_physx_error_buffer* error);

OPENUSD_PHYSX_API openusd_physx_status openusd_physx_scene_create(
    openusd_physx_vec3f gravity,
    openusd_physx_scene** scene,
    openusd_physx_error_buffer* error);

OPENUSD_PHYSX_API void openusd_physx_scene_release(openusd_physx_scene* scene);

OPENUSD_PHYSX_API openusd_physx_status openusd_physx_scene_add_static_plane(
    openusd_physx_scene* scene,
    float y,
    float static_friction,
    float dynamic_friction,
    float restitution,
    openusd_physx_error_buffer* error);

OPENUSD_PHYSX_API openusd_physx_status openusd_physx_scene_add_dynamic_box(
    openusd_physx_scene* scene,
    openusd_physx_vec3f position,
    openusd_physx_quatf rotation,
    openusd_physx_vec3f half_extents,
    openusd_physx_vec3f linear_velocity,
    openusd_physx_vec3f angular_velocity,
    float density,
    float static_friction,
    float dynamic_friction,
    float restitution,
    openusd_physx_error_buffer* error);

OPENUSD_PHYSX_API openusd_physx_status openusd_physx_scene_step(
    openusd_physx_scene* scene,
    float time_step,
    uint32_t step_count,
    openusd_physx_error_buffer* error);

OPENUSD_PHYSX_API openusd_physx_status openusd_physx_scene_get_dynamic_transforms(
    const openusd_physx_scene* scene,
    openusd_physx_transform* transforms,
    size_t capacity,
    size_t* count,
    openusd_physx_error_buffer* error);

#ifdef __cplusplus
}
#endif

#endif