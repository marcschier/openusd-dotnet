// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef HDSILK_PTEX_SYNC_TEST_HOOK_H
#define HDSILK_PTEX_SYNC_TEST_HOOK_H

#include "pxr/pxr.h"

PXR_NAMESPACE_OPEN_SCOPE

class HdSceneDelegate;
class SdfPath;

void HdSilkTestObservePtexSync(
    HdSceneDelegate* sceneDelegate,
    const SdfPath& id,
    bool usdDefaultOneProven);

PXR_NAMESPACE_CLOSE_SCOPE

#endif
