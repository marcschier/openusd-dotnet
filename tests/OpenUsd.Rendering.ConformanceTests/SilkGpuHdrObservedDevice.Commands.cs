// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

internal sealed partial class SilkGpuHdrObservedDevice
{
    private sealed class Commands(SilkGpuHdrObservedDevice owner, ISilkGraphicsCommandList inner) :
        ISilkGraphicsCommandList,
        ISilkDisplayTransformGraphicsCommandList,
        ISilkSelectionOutlineGraphicsCommandList
    {
        internal ISilkGraphicsCommandList Inner { get; } = inner;

        private ISilkDisplayTransformGraphicsCommandList Display => (ISilkDisplayTransformGraphicsCommandList)Inner;

        private ISilkSelectionOutlineGraphicsCommandList Selection => (ISilkSelectionOutlineGraphicsCommandList)Inner;

        public void BeginRendering(SilkRenderingDescriptor descriptor)
        {
            owner.ScenePasses++;
            Inner.BeginRendering(descriptor);
        }

        public void UploadTexture(ISilkGraphicsTexture texture, ReadOnlySpan<byte> source) =>
            Inner.UploadTexture(texture, source);

        public void ClearColor(ISilkGraphicsTexture texture, SilkColor color) => Inner.ClearColor(texture, color);

        public void ClearDepth(ISilkGraphicsTexture texture, float depth) => Inner.ClearDepth(texture, depth);

        public void SetGraphicsPipeline(ISilkGraphicsPipeline pipeline) => Inner.SetGraphicsPipeline(pipeline);

        public void SetViewport(SilkViewport viewport) => Inner.SetViewport(viewport);

        public void SetScissor(SilkScissor scissor) => Inner.SetScissor(scissor);

        public void SetVertexBuffer(ISilkGraphicsBuffer buffer) => Inner.SetVertexBuffer(buffer);

        public void SetIndexBuffer(ISilkGraphicsBuffer buffer) => Inner.SetIndexBuffer(buffer);

        public void SetUniformBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer) =>
            Inner.SetUniformBuffer(setIndex, binding, buffer);

        public void SetTexture(uint setIndex, uint binding, ISilkGraphicsTexture texture) =>
            Inner.SetTexture(setIndex, binding, texture);

        public void SetSampler(uint setIndex, uint binding, ISilkGraphicsSampler sampler) =>
            Inner.SetSampler(setIndex, binding, sampler);

        public void DrawIndexed(uint indexCount) => Inner.DrawIndexed(indexCount);

        public void DrawIndexedInstanced(uint indexCount, uint instanceCount) =>
            Inner.DrawIndexedInstanced(indexCount, instanceCount);

        public void EndRendering() => Inner.EndRendering();

        public void SetComputePipeline(ISilkComputePipeline pipeline) => Inner.SetComputePipeline(pipeline);

        public void SetStorageBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer) =>
            Inner.SetStorageBuffer(setIndex, binding, buffer);

        public void SetComputeUniformBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer) =>
            Inner.SetComputeUniformBuffer(setIndex, binding, buffer);

        public void Dispatch(uint elementCount) => Inner.Dispatch(elementCount);

        public void BufferBarrier(ISilkGraphicsBuffer buffer) => Inner.BufferBarrier(buffer);

        public void BeginDisplayTransformRendering(SilkDisplayTransformRenderingDescriptor descriptor) =>
            Display.BeginDisplayTransformRendering(descriptor);

        public void SetDisplayTransformGraphicsPipeline(ISilkDisplayTransformGraphicsPipeline pipeline) =>
            Display.SetDisplayTransformGraphicsPipeline(pipeline);

        public void SetDisplayTransformBinding(ISilkDisplayTransformBinding binding) =>
            Display.SetDisplayTransformBinding(binding);

        public void DrawDisplayTransformFullscreenTriangle() => Display.DrawDisplayTransformFullscreenTriangle();

        public void BeginSelectionMaskRendering(SilkSelectionMaskRenderingDescriptor descriptor) =>
            Selection.BeginSelectionMaskRendering(descriptor);

        public void SetSelectionMaskGraphicsPipeline(ISilkSelectionMaskGraphicsPipeline pipeline) =>
            Selection.SetSelectionMaskGraphicsPipeline(pipeline);

        public void BeginSelectionOutlineRendering(SilkSelectionOutlineRenderingDescriptor descriptor) =>
            Selection.BeginSelectionOutlineRendering(descriptor);

        public void SetSelectionOutlineGraphicsPipeline(ISilkSelectionOutlineGraphicsPipeline pipeline) =>
            Selection.SetSelectionOutlineGraphicsPipeline(pipeline);

        public void SetSelectionOutlineBinding(ISilkSelectionOutlineBinding binding) =>
            Selection.SetSelectionOutlineBinding(binding);

        public void DrawSelectionOutlineFullscreenTriangle() => Selection.DrawSelectionOutlineFullscreenTriangle();

        public void Dispose() => Inner.Dispose();
    }
}
