// Copyright (c) marcschier. Licensed under the MIT License.

[[vk::binding(0, 0)]]
RWStructuredBuffer<float4> outputValues : register(u0);

[[vk::binding(1, 0)]]
cbuffer ComputeParameters : register(b1)
{
    uint elementCount;
    float scale;
};

[shader("compute")]
[numthreads(64, 1, 1)]
void fillMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint index = dispatchThreadId.x;
    if (index < elementCount)
    {
        outputValues[index] = float4(float(index) * scale, 0.0, 0.0, 1.0);
    }
}

[shader("compute")]
[numthreads(64, 1, 1)]
void scaleMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint index = dispatchThreadId.x;
    if (index < elementCount)
    {
        outputValues[index] *= float4(scale, scale, scale, 1.0);
    }
}
