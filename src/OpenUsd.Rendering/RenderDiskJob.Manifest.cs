// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Text.Json;

namespace OpenUsd.Rendering;

public static partial class RenderDiskJob
{
    private static void WriteRequestedState(Utf8JsonWriter json, StageRenderState state)
    {
        json.WriteStartObject("camera");
        json.WriteString("mode", state.Camera.Mode.ToString());
        WriteMatrix(json, "view", state.Camera.View);
        WriteMatrix(json, "projection", state.Camera.Projection);
        json.WriteStartArray("clipPlanes");
        foreach (Vector4 plane in state.Camera.ClipPlanes)
        {
            WriteVector(json, plane);
        }
        json.WriteEndArray();
        json.WriteEndObject();

        json.WriteStartObject("display");
        json.WriteNumber("purposes", (uint)state.Display.Purposes);
        json.WriteString("visibility", state.Display.Visibility.ToString());
        json.WriteString("drawMode", state.Display.DrawMode.ToString());
        json.WriteEndObject();

        RenderSettings settings = state.RenderSettings;
        json.WriteStartObject("settings");
        json.WriteNumber("samplesPerPixel", settings.SamplesPerPixel);
        json.WriteBoolean("lighting", settings.EnableLighting);
        json.WriteBoolean("shadows", settings.EnableShadows);
        json.WriteBoolean("backfaceCulling", settings.BackfaceCulling);
        json.WriteBoolean("sceneMaterials", settings.UseSceneMaterials);
        json.WriteString("complexity", settings.Complexity.ToString());
        json.WriteString("outputTransform", settings.OutputTransform.ToString());
        json.WriteNumber("exposure", settings.Exposure);
        json.WritePropertyName("clearColor");
        WriteVector(json, settings.ClearColor);
        if (settings.DisplayTransform is { } transform)
        {
            json.WriteStartObject("displayTransform");
            json.WriteString("configPath", transform.ConfigPath);
            json.WriteString("sourceColorSpace", transform.SourceColorSpace);
            json.WriteString("display", transform.Display);
            json.WriteString("view", transform.View);
            json.WriteString("look", transform.Look);
            json.WriteNumber("latticeSize", transform.LatticeSize);
            json.WriteNumber("shaperMinimumLog2", transform.ShaperMinimumLog2);
            json.WriteNumber("shaperMaximumLog2", transform.ShaperMaximumLog2);
            json.WriteEndObject();
        }
        else
        {
            json.WriteNull("displayTransform");
        }
        json.WriteEndObject();
        json.WriteStartArray("selection");
        foreach (SelectionItem item in state.Selection.Items)
        {
            json.WriteStartObject();
            json.WriteString("primPath", item.PrimPath);
            json.WriteString("elementKind", item.ElementKind.ToString());
            if (item.ElementIndex is { } element)
            {
                json.WriteNumber("elementIndex", element);
            }
            else
            {
                json.WriteNull("elementIndex");
            }
            json.WriteStartArray("instancerContext");
            foreach (SelectionInstancerEntry entry in item.InstancerContext)
            {
                json.WriteStartObject();
                json.WriteString("path", entry.InstancerPath);
                json.WriteNumber("instanceIndex", entry.InstanceIndex);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        json.WriteEndArray();
    }

    private static void WriteVector(Utf8JsonWriter json, Vector4 vector)
    {
        json.WriteStartArray();
        json.WriteNumberValue(vector.X);
        json.WriteNumberValue(vector.Y);
        json.WriteNumberValue(vector.Z);
        json.WriteNumberValue(vector.W);
        json.WriteEndArray();
    }

    private static void WriteMatrix(Utf8JsonWriter json, string name, Matrix4x4 matrix)
    {
        json.WriteStartArray(name);
        ReadOnlySpan<float> values =
        [
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44
        ];
        foreach (float value in values)
        {
            json.WriteNumberValue(value);
        }
        json.WriteEndArray();
    }
}
