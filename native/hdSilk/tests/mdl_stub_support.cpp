// Copyright (c) marcschier. Licensed under the MIT License.
//
// A dependency of the MDL loader's ABI-mismatch stub. It exists only so the
// stub has something to resolve from its own directory: a loader that searched
// the process directory or the current directory instead of the directory the
// adapter was loaded from would fail to load the stub at all, and the probe
// would see LoadFailed instead of AbiMismatch.

#include "mdl_stub_support.h"

uint32_t
HdSilkMdlStubSupportValue()
{
    return 0xABCDEF01u;
}
