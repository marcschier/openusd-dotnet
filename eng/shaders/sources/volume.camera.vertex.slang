// Copyright (c) marcschier. Licensed under the MIT License.

struct VolumeCameraVertexInput
{
    float3 position : POSITION;
    float3 normal : NORMAL;
};

struct VolumeCameraVertexOutput
{
    float4 position : SV_Position;
};

[shader("vertex")]
VolumeCameraVertexOutput volumeCameraVertexMain(VolumeCameraVertexInput input)
{
    VolumeCameraVertexOutput output;
    output.position = float4(input.position, 1.0);
    return output;
}
