// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_physx.h"

#include <cmath>
#include <cstring>
#include <iostream>
#include <vector>

namespace
{
bool Check(openusd_physx_status status, const char* action)
{
    if (status == OPENUSD_PHYSX_STATUS_OK || status == OPENUSD_PHYSX_STATUS_BUFFER_TOO_SMALL)
    {
        return true;
    }
    std::cerr << action << " failed with status " << status << '\n';
    return false;
}
}

int main()
{
    char version[64]{};
    size_t required = 0;
    char error_data[512]{};
    openusd_physx_error_buffer error{error_data, sizeof(error_data), 0};
    if (!Check(openusd_physx_get_version(version, sizeof(version), &required, &error), "version") ||
        std::strlen(version) == 0)
    {
        return 1;
    }
    std::cout << "PhysX version: " << version << '\n';

    openusd_physx_scene* scene = nullptr;
    if (!Check(openusd_physx_scene_create({0.0F, -9.81F, 0.0F}, &scene, &error), "create"))
    {
        return 1;
    }
    if (!Check(openusd_physx_scene_add_static_plane(scene, 0.0F, 0.8F, 0.6F, 0.1F, &error), "plane"))
    {
        openusd_physx_scene_release(scene);
        return 1;
    }
    for (int index = 0; index < 10; ++index)
    {
        const float y = 0.5F + static_cast<float>(index) * 1.05F;
        if (!Check(
                openusd_physx_scene_add_dynamic_box(
                    scene,
                    {0.0F, y + 3.0F, 0.0F},
                    {0.0F, 0.0F, 0.0F, 1.0F},
                    {0.5F, 0.5F, 0.5F},
                    {0.0F, 0.0F, 0.0F},
                    {0.0F, 0.0F, 0.0F},
                    1.0F,
                    0.8F,
                    0.6F,
                    0.1F,
                    &error),
                "box"))
        {
            openusd_physx_scene_release(scene);
            return 1;
        }
    }
    if (!Check(openusd_physx_scene_step(scene, 1.0F / 240.0F, 2400, &error), "settle"))
    {
        openusd_physx_scene_release(scene);
        return 1;
    }
    std::vector<openusd_physx_transform> transforms(10);
    size_t count = 0;
    if (!Check(
            openusd_physx_scene_get_dynamic_transforms(
                scene,
                transforms.data(),
                transforms.size(),
                &count,
                &error),
            "readback") || count != transforms.size())
    {
        openusd_physx_scene_release(scene);
        return 1;
    }
    if (transforms.front().position.y < 0.49F || transforms.back().position.y < 9.0F)
    {
        std::cerr << "stack did not settle at expected heights: first="
                  << transforms.front().position.y << " last="
                  << transforms.back().position.y << "\n";
        openusd_physx_scene_release(scene);
        return 1;
    }
    openusd_physx_scene_release(scene);

    scene = nullptr;
    if (!Check(openusd_physx_scene_create({0.0F, -9.81F, 0.0F}, &scene, &error), "friction create") ||
        !Check(openusd_physx_scene_add_static_plane(scene, 0.0F, 0.8F, 0.8F, 0.0F, &error), "friction plane") ||
        !Check(
            openusd_physx_scene_add_dynamic_box(
                scene,
                {0.0F, 0.5F, 0.0F},
                {0.0F, 0.0F, 0.0F, 1.0F},
                {0.5F, 0.5F, 0.5F},
                {5.0F, 0.0F, 0.0F},
                {0.0F, 0.0F, 0.0F},
                1.0F,
                0.8F,
                0.8F,
                0.0F,
                &error),
            "friction box") ||
        !Check(openusd_physx_scene_step(scene, 1.0F / 240.0F, 2400, &error), "friction step") ||
        !Check(
            openusd_physx_scene_get_dynamic_transforms(scene, transforms.data(), 1, &count, &error),
            "friction readback"))
    {
        openusd_physx_scene_release(scene);
        return 1;
    }
    if (transforms[0].position.x < 1.0F || transforms[0].position.x > 3.0F)
    {
        std::cerr << "friction did not stop near expected distance: x="
                  << transforms[0].position.x << "\n";
        openusd_physx_scene_release(scene);
        return 1;
    }
    openusd_physx_scene_release(scene);

    scene = nullptr;
    if (!Check(openusd_physx_scene_create({0.0F, 0.0F, 0.0F}, &scene, &error), "angular create") ||
        !Check(
            openusd_physx_scene_add_dynamic_box(
                scene,
                {0.0F, 0.0F, 0.0F},
                {0.0F, 0.0F, 0.0F, 1.0F},
                {0.25F, 0.5F, 1.0F},
                {0.0F, 0.0F, 0.0F},
                {0.0F, 8.0F, 0.0F},
                1.0F,
                0.0F,
                0.0F,
                0.0F,
                &error),
            "angular box") ||
        !Check(openusd_physx_scene_step(scene, 1.0F / 240.0F, 120, &error), "angular step") ||
        !Check(
            openusd_physx_scene_get_dynamic_transforms(scene, transforms.data(), 1, &count, &error),
            "angular readback"))
    {
        openusd_physx_scene_release(scene);
        return 1;
    }
    if (std::abs(transforms[0].rotation.w) > 0.999F)
    {
        std::cerr << "angular velocity did not rotate the body\n";
        openusd_physx_scene_release(scene);
        return 1;
    }
    openusd_physx_scene_release(scene);
    return 0;
}