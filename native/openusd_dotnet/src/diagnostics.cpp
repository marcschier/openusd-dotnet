// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

extern "C" OPENUSD_DOTNET_API size_t openusd_diagnostic_get_live_stage_core_count(void)
{
    return DiagnosticLiveStageCoreCount.load(std::memory_order_relaxed);
}

extern "C" OPENUSD_DOTNET_API size_t openusd_diagnostic_get_peak_stage_core_count(void)
{
    return DiagnosticPeakStageCoreCount.load(std::memory_order_relaxed);
}

extern "C" OPENUSD_DOTNET_API void openusd_diagnostic_reset_peak_stage_core_count(void)
{
    DiagnosticPeakStageCoreCount.store(
        DiagnosticLiveStageCoreCount.load(std::memory_order_relaxed),
        std::memory_order_relaxed);
}

extern "C" OPENUSD_DOTNET_API openusd_status openusd_diagnostic_set_display_color(
    openusd_stage* stage,
    const char* prim_path,
    float red,
    float green,
    float blue,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return GuardStage(stage, error, [&]() -> openusd_status
    {
        if (stage == nullptr || !stage->value || prim_path == nullptr)
        {
            WriteError(error, "A stage and prim path are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (!std::isfinite(red) || !std::isfinite(green) || !std::isfinite(blue))
        {
            WriteError(error, "Display color components must be finite.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        const SdfPath path(prim_path);
        if (!IsValidPrimPath(prim_path))
        {
            WriteError(error, "The prim path must be an absolute prim path.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        const UsdGeomGprim gprim(stage->value->GetPrimAtPath(path));
        if (!gprim)
        {
            WriteError(error, "The prim does not exist or is not a geometric prim.");
            return OPENUSD_STATUS_NOT_FOUND;
        }

        UsdGeomPrimvar display_color = gprim.GetDisplayColorPrimvar();
        if (!display_color)
        {
            display_color = gprim.CreateDisplayColorPrimvar();
        }
        VtArray<GfVec3f> colors(1);
        colors[0] = GfVec3f(red, green, blue);
        return display_color.Set(colors) ? OPENUSD_STATUS_OK : OPENUSD_STATUS_NATIVE_ERROR;
    });
}

#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
extern "C" OPENUSD_DOTNET_API size_t openusd_test_get_live_stage_core_count(void)
{
    return DiagnosticLiveStageCoreCount.load(std::memory_order_relaxed);
}

extern "C" OPENUSD_DOTNET_API size_t openusd_test_get_destroyed_stage_core_count(void)
{
    return TestDestroyedStageCoreCount.load(std::memory_order_relaxed);
}
#endif
