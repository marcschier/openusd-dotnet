// Copyright (c) marcschier. Licensed under the MIT License.
#ifndef OPENUSD_HYDRA_CONTEXT_SUPPORT_H
#define OPENUSD_HYDRA_CONTEXT_SUPPORT_H

enum class OpenUsdStormLinuxContextKind
{
    Missing,
    Glx,
    NonGlx
};

constexpr OpenUsdStormLinuxContextKind DiagnoseOpenUsdStormLinuxContext(
    bool hasOpenGlContext,
    bool hasGlxContext)
{
    if (!hasOpenGlContext)
    {
        return OpenUsdStormLinuxContextKind::Missing;
    }
    return hasGlxContext
        ? OpenUsdStormLinuxContextKind::Glx
        : OpenUsdStormLinuxContextKind::NonGlx;
}

#endif
