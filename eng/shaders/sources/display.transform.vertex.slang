// Copyright (c) marcschier. Licensed under the MIT License.

struct DisplayTransformVertexOutput
{
    float4 position : SV_Position;
    float2 textureCoordinate : TEXCOORD0;
};

[shader("vertex")]
DisplayTransformVertexOutput displayTransformVertexMain(uint vertexId : SV_VertexID)
{
    float2 textureCoordinate = float2(
        float((vertexId << 1u) & 2u),
        float(vertexId & 2u));

    DisplayTransformVertexOutput output;
    output.position = float4(
        textureCoordinate * float2(2.0, -2.0) + float2(-1.0, 1.0),
        0.0,
        1.0);
    output.textureCoordinate = textureCoordinate;
    return output;
}
