// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

std::atomic<size_t> DiagnosticLiveStageCoreCount{0};
std::atomic<size_t> DiagnosticPeakStageCoreCount{0};
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
std::atomic<size_t> TestDestroyedStageCoreCount{0};
#endif
