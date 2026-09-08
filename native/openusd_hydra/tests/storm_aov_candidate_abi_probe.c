/* Copyright (c) marcschier. Licensed under the MIT License. */

#include "openusd_storm_aov_candidate.h"
#include "openusd_storm_aov_test_hooks.h"

#include <stddef.h>
#include <stdio.h>
#include <string.h>

_Static_assert(sizeof(openusd_storm_aov_request) == 640, "request ABI");
_Static_assert(offsetof(openusd_storm_aov_request, camera) == 112, "request camera ABI");
_Static_assert(sizeof(openusd_storm_aov_output) == 64, "output ABI");
_Static_assert(sizeof(openusd_storm_aov_identity) == 56, "identity ABI");
_Static_assert(sizeof(openusd_storm_aov_instance_context) == 24, "context ABI");
_Static_assert(sizeof(openusd_storm_aov_view) == 688, "view ABI");
_Static_assert(offsetof(openusd_storm_aov_view, pixel_bytes) == 112, "view extent ABI");
_Static_assert(offsetof(openusd_storm_aov_view, applied_camera) == 160, "view camera ABI");
_Static_assert(sizeof(openusd_storm_aov_test_statistics) == 48, "statistics ABI");

int main(void)
{
    openusd_storm_aov_owner* owner = (openusd_storm_aov_owner*)(uintptr_t)1;
    openusd_storm_aov_view view;
    char message[256] = {0};
    openusd_error_buffer error = {message, sizeof(message), 0};
    memset(&view, 0, sizeof(view));
    view.struct_size = sizeof(view);
    view.version = OPENUSD_STORM_AOV_CANDIDATE_VERSION;
    if (openusd_storm_get_abi_version() != 8 ||
        openusd_storm_aov_candidate_capture(NULL, NULL, &owner, &error) !=
            OPENUSD_STATUS_INVALID_ARGUMENT ||
        owner != NULL ||
        openusd_storm_aov_candidate_get_view(NULL, &view, &error) !=
            OPENUSD_STATUS_INVALID_ARGUMENT ||
        view.struct_size != 0 || view.version != 0 || view.pixel_data != NULL ||
        view.outputs != NULL || view.pixel_bytes != 0 || view.identity_count != 0)
    {
        fprintf(stderr, "Private C ABI initialization failed: %s\n", message);
        return 1;
    }
    openusd_storm_aov_candidate_release(NULL);
    puts("PRIVATE_C11_ABI=640/64/56/24/688; PUBLIC_STORM_ABI=8; ERROR_OUTPUTS=zero");
    return 0;
}
