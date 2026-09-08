// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Text;
using System.Text.Json;
using OpenUsd.Geom;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal static class ViewerCameraBookmarkCodec
{
    internal const int MaximumRecords = 32;
    internal const int MaximumRecordBytes = 4096;
    internal const int MaximumCatalogBytes = 128 * 1024;
    internal static UTF8Encoding Utf8 { get; } = new(false, true);
    private static readonly string[] RecordFields =
        ["v", "id", "name", "source", "time", "width", "height", "kind", "view", "projection"];
    private static readonly string[] StageFields = ["path", "local", "world", "optics"];

    internal static string[] EncodeCatalog(IReadOnlyList<ViewerCameraBookmark> bookmarks)
    {
        ArgumentNullException.ThrowIfNull(bookmarks);
        if (bookmarks.Count > MaximumRecords)
        {
            throw new ArgumentException("A review supports at most 32 saved views.", nameof(bookmarks));
        }
        var records = new string[bookmarks.Count];
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < records.Length; index++)
        {
            ViewerCameraBookmark bookmark = bookmarks[index] ??
                throw new ArgumentException("A saved view cannot be null.", nameof(bookmarks));
            RequireUnique(bookmark, ids, names);
            records[index] = Encode(bookmark);
        }
        ValidateStorage(records);
        return records;
    }

    internal static IReadOnlyList<ViewerCameraBookmark> DecodeCatalog(
        IReadOnlyList<string> records, string sourceStamp)
    {
        ValidateStorage(records);
        var bookmarks = new ViewerCameraBookmark[records.Count];
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < bookmarks.Length; index++)
        {
            ViewerCameraBookmark bookmark = Decode(records[index]);
            if (!string.Equals(bookmark.SourceStamp, sourceStamp, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The saved view belongs to a different verified source context.");
            }
            RequireUnique(bookmark, ids, names);
            bookmarks[index] = bookmark;
        }
        return Array.AsReadOnly(bookmarks);
    }

    internal static void ValidateStorage(IReadOnlyList<string> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count > MaximumRecords)
        {
            throw new InvalidDataException("The saved-view catalog exceeds 32 records; nothing was changed.");
        }
        int total = 8;
        foreach (string record in records)
        {
            if (record is null || record.Length > MaximumRecordBytes || record.Contains('\0', StringComparison.Ordinal))
            {
                throw new InvalidDataException("A saved-view record exceeds its bounded string domain.");
            }
            int bytes = Utf8.GetByteCount(record);
            if (bytes > MaximumRecordBytes)
            {
                throw new InvalidDataException("A saved-view record exceeds 4096 UTF-8 bytes.");
            }
            total += 4 + bytes;
            if (total > MaximumCatalogBytes)
            {
                throw new InvalidDataException("The saved-view catalog exceeds 128 KiB including framing.");
            }
        }
    }

    private static void RequireUnique(ViewerCameraBookmark bookmark, HashSet<Guid> ids, HashSet<string> names)
    {
        if (!ids.Add(bookmark.Id) || !names.Add(bookmark.Name))
        {
            throw new InvalidDataException("Saved-view identities and names must be unique (names ignore case).");
        }
    }

    private static string Encode(ViewerCameraBookmark bookmark)
    {
        using var stream = new MemoryStream(MaximumRecordBytes);
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", 1);
            writer.WriteString("id", bookmark.Id.ToString("N"));
            writer.WriteString("name", bookmark.Name);
            writer.WriteString("source", bookmark.SourceStamp);
            writer.WriteNumber("time", bookmark.TimeCode);
            writer.WriteNumber("width", bookmark.Viewport.Width);
            writer.WriteNumber("height", bookmark.Viewport.Height);
            writer.WriteString("kind", bookmark.StageCamera is null ? "free" : "stage");
            WriteFloats(writer, "view", MatrixValues(bookmark.Camera.View));
            WriteFloats(writer, "projection", MatrixValues(bookmark.Camera.Projection));
            if (bookmark.StageCamera is { } stage)
            {
                writer.WriteStartObject("stage");
                writer.WriteString("path", stage.PrimPath);
                WriteDoubles(writer, "local", MatrixValues(stage.LocalToWorld));
                WriteDoubles(writer, "world", MatrixValues(stage.WorldToView));
                WriteDoubles(writer, "optics", OpticsValues(stage.Optics));
                writer.WriteEndObject();
            }
            else
            {
                WriteFloats(writer, "free", FreeValues(bookmark.FreeCamera));
            }
            writer.WriteEndObject();
        }
        if (stream.Length > MaximumRecordBytes)
        {
            throw new InvalidDataException("The encoded saved view exceeds 4096 UTF-8 bytes.");
        }
        return Utf8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    private static ViewerCameraBookmark Decode(string record)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(record, new JsonDocumentOptions { MaxDepth = 4 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("kind", out JsonElement kindValue))
            {
                throw new InvalidDataException("The saved-view kind is missing.");
            }
            string? kind = kindValue.GetString();
            if (kind is not ("free" or "stage"))
            {
                throw new InvalidDataException("The saved-view kind is unsupported.");
            }
            RequireFields(root, RecordFields, kind);
            if (root.GetProperty("v").GetInt32() != 1 ||
                !Guid.TryParseExact(root.GetProperty("id").GetString(), "N", out Guid id))
            {
                throw new InvalidDataException("The saved-view version, kind or identity is unsupported.");
            }
            string name = root.GetProperty("name").GetString() ??
                throw new InvalidDataException("The saved-view name is missing.");
            if (ViewerCameraBookmark.ValidateName(name) != name)
            {
                throw new InvalidDataException("A stored saved-view name must already be trimmed.");
            }
            string source = root.GetProperty("source").GetString() ??
                throw new InvalidDataException("The saved-view source stamp is missing.");
            var viewport = new ViewportDimensions(
                root.GetProperty("width").GetInt32(), root.GetProperty("height").GetInt32());
            double time = root.GetProperty("time").GetDouble();
            ViewerCameraBookmark bookmark;
            if (kind == "stage")
            {
                JsonElement stage = root.GetProperty("stage");
                RequireFields(stage, StageFields);
                double[] optics = ReadDoubles(stage.GetProperty("optics"), 14);
                if (optics[0] is not (0 or 1))
                {
                    throw new InvalidDataException("The authored saved-view projection is unsupported.");
                }
                var sample = new ViewerStageCameraSnapshot(stage.GetProperty("path").GetString()!, time,
                    ReadMatrix(stage.GetProperty("local")), ReadMatrix(stage.GetProperty("world")),
                    new UsdGeomCameraState((UsdGeomCameraProjection)optics[0],
                        optics[1], optics[2], optics[3], optics[4], optics[5], optics[6], optics[7],
                        optics[8], optics[9], optics[10], optics[11], optics[12], optics[13]));
                bookmark = ViewerCameraBookmark.CreateStage(id, name, source, viewport, sample);
            }
            else
            {
                float[] values = ReadFloats(root.GetProperty("free"), 12);
                if (values[6] is not (0 or 1))
                {
                    throw new InvalidDataException("The saved free-camera projection is unsupported.");
                }
                var free = new ViewerCameraNavigationState(false, new Vector3(values[0], values[1], values[2]),
                    values[3], values[4], values[5], (ViewerCameraProjectionMode)values[6],
                    values[7], values[8], values[9], values[10], values[11]);
                if (!FreeValues(free).SequenceEqual(values))
                {
                    throw new InvalidDataException("The saved camera would require normalization or clamping.");
                }
                bookmark = ViewerCameraBookmark.CreateFree(id, name, source, time, viewport, free);
            }
            if (!MatrixValues(bookmark.Camera.View).SequenceEqual(ReadFloats(root.GetProperty("view"), 16)) ||
                !MatrixValues(bookmark.Camera.Projection).SequenceEqual(ReadFloats(root.GetProperty("projection"), 16)))
            {
                throw new InvalidDataException("The saved matrices do not exactly match the logical camera.");
            }
            return bookmark;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or
            InvalidOperationException or FormatException or OverflowException)
        {
            throw new InvalidDataException("The saved-view record is malformed; nothing was changed.", exception);
        }
    }

    private static void RequireFields(JsonElement element, string[] expected, string? additional = null)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("A saved view must be an object.");
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if ((!expected.Contains(property.Name, StringComparer.Ordinal) && property.Name != additional) ||
                !seen.Add(property.Name))
            {
                throw new InvalidDataException("The saved view contains unknown or duplicate fields.");
            }
        }
        if (seen.Count != expected.Length + (additional is null ? 0 : 1))
        {
            throw new InvalidDataException("The saved view is missing required fields.");
        }
    }

    private static float[] ReadFloats(JsonElement element, int count)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != count)
        {
            throw new InvalidDataException("The saved view has an invalid numeric extent.");
        }
        var values = new float[count];
        for (int index = 0; index < count; index++)
        {
            if (!element[index].TryGetSingle(out values[index]) || !float.IsFinite(values[index]))
            {
                throw new InvalidDataException("Saved camera numbers must be finite float32 values.");
            }
        }
        return values;
    }

    private static void WriteFloats(Utf8JsonWriter writer, string name, ReadOnlySpan<float> values)
    {
        writer.WriteStartArray(name);
        foreach (float value in values)
        {
            writer.WriteNumberValue(value);
        }
        writer.WriteEndArray();
    }

    private static double[] ReadDoubles(JsonElement element, int count)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != count)
        {
            throw new InvalidDataException("The saved view has an invalid numeric extent.");
        }
        var values = new double[count];
        for (int index = 0; index < count; index++)
        {
            if (!element[index].TryGetDouble(out values[index]) || !double.IsFinite(values[index]))
            {
                throw new InvalidDataException("Authored saved-camera numbers must be finite.");
            }
        }
        return values;
    }

    private static void WriteDoubles(Utf8JsonWriter writer, string name, ReadOnlySpan<double> values)
    {
        writer.WriteStartArray(name);
        foreach (double value in values)
        {
            writer.WriteNumberValue(value);
        }
        writer.WriteEndArray();
    }

    private static UsdMatrix4d ReadMatrix(JsonElement element)
    {
        double[] v = ReadDoubles(element, 16);
        return new UsdMatrix4d(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7],
            v[8], v[9], v[10], v[11], v[12], v[13], v[14], v[15]);
    }

    private static double[] MatrixValues(UsdMatrix4d value) =>
    [
        value.M00, value.M01, value.M02, value.M03, value.M10, value.M11, value.M12, value.M13,
        value.M20, value.M21, value.M22, value.M23, value.M30, value.M31, value.M32, value.M33
    ];

    private static double[] OpticsValues(UsdGeomCameraState value) =>
    [
        (double)value.Projection, value.WindowLeft, value.WindowRight, value.WindowBottom, value.WindowTop,
        value.ClippingNear, value.ClippingFar, value.FocalLength, value.HorizontalAperture, value.VerticalAperture,
        value.HorizontalApertureOffset, value.VerticalApertureOffset, value.FocusDistance, value.FStop
    ];

    private static float[] FreeValues(ViewerCameraNavigationState state) =>
    [
        state.Target.X, state.Target.Y, state.Target.Z, state.Distance, state.Yaw, state.Pitch,
        (float)state.ProjectionMode, state.VerticalFieldOfView, state.OrthographicHeight,
        state.NearPlane, state.FarPlane, state.AspectRatio
    ];

    private static float[] MatrixValues(Matrix4x4 matrix) =>
    [
        matrix.M11, matrix.M12, matrix.M13, matrix.M14, matrix.M21, matrix.M22, matrix.M23, matrix.M24,
        matrix.M31, matrix.M32, matrix.M33, matrix.M34, matrix.M41, matrix.M42, matrix.M43, matrix.M44
    ];
}
