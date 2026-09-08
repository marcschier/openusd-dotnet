// Copyright (c) marcschier. Licensed under the MIT License.
#pragma once

#include "layer_edit_probe_support.h"

void RunAuthoredKinds(EditProbe::Api& api, openusd_layer* review);
void RunSampleBatchBounds(EditProbe::Api& api, openusd_layer* review);
void RunUnsupportedAddresses(EditProbe::Api& api, openusd_layer* review);
void RunPersistence(EditProbe::Api& api, openusd_layer* review, const std::string& directory);
void RunBitExactCheckpoint(EditProbe::Api& api, openusd_stage* stage, openusd_layer* review);
void RunRollbackInventories(EditProbe::Api& api, openusd_layer* review);
void RunOverlayContinuity(EditProbe::Api& api, const std::string& directory);
void RunOverlayLifecycles(EditProbe::Api& api, const std::string& directory);
void RunOverlayRefusals(EditProbe::Api& api, const std::string& directory);
void RunOverlayRollback(EditProbe::Api& api, const std::string& directory);
void RunOverlayAdmission(EditProbe::Api& api, const std::string& directory);
void RunIdentityAndBounds(EditProbe::Api& api, openusd_stage* stage,
    openusd_layer* review, const std::string& rootFile, const std::string& directory);
