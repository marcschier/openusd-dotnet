// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_STORM_ENGINE_H
#define OPENUSD_STORM_ENGINE_H

#include "pxr/imaging/hd/tokens.h"
#include "pxr/imaging/hdx/taskController.h"
#include "pxr/imaging/hdx/taskControllerSceneIndex.h"
#include "pxr/usdImaging/usdImagingGL/engine.h"

class OpenUsdStormEngine final : public PXR_NS::UsdImagingGLEngine
{
public:
    explicit OpenUsdStormEngine(const Parameters& parameters)
        : UsdImagingGLEngine(parameters)
    {
    }

    bool SetCaptureOutputs(const PXR_NS::TfTokenVector& outputs, bool multisampled)
    {
        if (!SetRendererAovs(outputs))
        {
            return false;
        }
        // OpenUSD 26.05 disables viewport AOV selection for multiple outputs.
        // Its protected controller extension seam lets this project preserve
        // color presentation without dropping the simultaneously rendered AOVs.
        if (_taskControllerSceneIndex)
        {
            ConfigureSampling(*_taskControllerSceneIndex, outputs, multisampled);
            _taskControllerSceneIndex->SetViewportRenderOutput(PXR_NS::HdAovTokens->color);
            return true;
        }
        if (_taskController)
        {
            ConfigureSampling(*_taskController, outputs, multisampled);
            _taskController->SetViewportRenderOutput(PXR_NS::HdAovTokens->color);
            return true;
        }
        return false;
    }

private:
    template <typename TController>
    static void ConfigureSampling(
        TController& controller,
        const PXR_NS::TfTokenVector& outputs,
        bool multisampled)
    {
        // AOV defaults enable MSAA even for a single-sample presentation target.
        // Preserve that target's sampling mode, including implicit paired IDs.
        bool has_ids = false;
        for (const PXR_NS::TfToken& name : outputs)
        {
            if (name == PXR_NS::HdAovTokens->primId ||
                name == PXR_NS::HdAovTokens->instanceId)
            {
                has_ids = true;
                continue;
            }
            if (name == PXR_NS::HdAovTokens->elementId ||
                name == PXR_NS::HdAovTokens->normal)
            {
                continue;
            }
            PXR_NS::HdAovDescriptor descriptor = controller.GetRenderOutputSettings(name);
            descriptor.multiSampled = multisampled;
            controller.SetRenderOutputSettings(name, descriptor);
        }
        if (has_ids)
        {
            for (const PXR_NS::TfToken& name :
                 {PXR_NS::HdAovTokens->primId, PXR_NS::HdAovTokens->instanceId})
            {
                PXR_NS::HdAovDescriptor descriptor = controller.GetRenderOutputSettings(name);
                descriptor.multiSampled = multisampled;
                controller.SetRenderOutputSettings(name, descriptor);
            }
        }
        PXR_NS::HdAovDescriptor depth =
            controller.GetRenderOutputSettings(PXR_NS::HdAovTokens->depth);
        depth.multiSampled = multisampled;
        controller.SetRenderOutputSettings(PXR_NS::HdAovTokens->depth, depth);
    }
};

#endif
