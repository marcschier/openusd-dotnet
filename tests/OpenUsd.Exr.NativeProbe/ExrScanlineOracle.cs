// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

// Independent test oracle for the writer's single-part, origin-zero, RGBA HALF
// scanline/ZIPS contract. This is not a general EXR reader or production codec.
internal static class ExrScanlineOracle
{
    internal static byte[] Read(byte[] file, int width, int height)
    {
        Require(file.Length is >= 8 and <= 67_108_864, "Bounded EXR extent.");
        Require(U32(file, 0) == 20000630 && U32(file, 4) == 2, "EXR magic and scanline version.");
        Require(width is > 0 and <= 8192 && height is > 0 and <= 8192 &&
            (long)width * height <= 8_388_608, "Bounded oracle fixture dimensions.");
        int position = 8;
        var attributes = new Dictionary<string, AttributeValue>(StringComparer.Ordinal);
        while (true)
        {
            string name = CString(file, ref position);
            if (name.Length == 0)
            {
                break;
            }
            string type = CString(file, ref position);
            int length = I32(file, position);
            position += 4;
            Require(length >= 0 && length <= file.Length - position, "Attribute extent.");
            Require(attributes.TryAdd(name, new AttributeValue(type, position, length)), "Duplicate attribute.");
            position += length;
        }
        ReadOnlySpan<byte> compression = Attribute(file, attributes, "compression", "compression", 1);
        Require(compression[0] == 2, "Lossless single-scanline ZIPS compression.");
        ReadOnlySpan<byte> data = Attribute(file, attributes, "dataWindow", "box2i", 16);
        ReadOnlySpan<byte> display = Attribute(file, attributes, "displayWindow", "box2i", 16);
        Require(data.SequenceEqual(display) && I32(data, 0) == 0 && I32(data, 4) == 0 &&
            I32(data, 8) == width - 1 && I32(data, 12) == height - 1, "Origin-zero equal full windows.");
        Require(Attribute(file, attributes, "lineOrder", "lineOrder", 1)[0] == 0, "Increasing scanline order.");
        Require(U32(Attribute(file, attributes, "pixelAspectRatio", "float", 4), 0) == 0x3f800000,
            "Square pixels.");
        Require(StringAttribute(file, attributes, "openusd:colorPolicy") == "raw-working-unspecified",
            "Explicit raw renderer-working metadata.");
        Require(StringAttribute(file, attributes, "openusd:alphaPolicy") == "stored-unspecified",
            "No inferred alpha association.");
        Require(!attributes.ContainsKey("chromaticities") && !attributes.ContainsKey("acesImageContainerFlag"),
            "No invented named primaries.");
        int[] channels = Channels(file, attributes);
        Require(height * 8 <= file.Length - position, "Complete scanline offset table.");
        int chunkPosition = position + height * 8;
        byte[] rgba = new byte[checked(width * height * 8)];
        int rowBytes = width * 8;
        for (int y = 0; y < height; y++)
        {
            ulong offset = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(position + y * 8, 8));
            Require(offset == (ulong)chunkPosition, "Contiguous, rewritten scanline offsets.");
            Require(chunkPosition <= file.Length - 8 && I32(file, chunkPosition) == y, "Scanline coordinate.");
            int payloadBytes = I32(file, chunkPosition + 4);
            Require(payloadBytes > 0 && payloadBytes <= rowBytes &&
                payloadBytes <= file.Length - chunkPosition - 8, "Bounded scanline payload.");
            byte[] unpacked;
            if (payloadBytes == rowBytes)
            {
                unpacked = file.AsSpan(chunkPosition + 8, rowBytes).ToArray();
            }
            else
            {
                byte[] predicted = new byte[rowBytes];
                using var payload = new MemoryStream(file, chunkPosition + 8, payloadBytes, writable: false);
                using (var zlib = new ZLibStream(payload, CompressionMode.Decompress))
                {
                    zlib.ReadExactly(predicted);
                    Require(zlib.ReadByte() == -1, "Exact inflated scanline extent.");
                }
                // EXR ZIP reverses a modulo-256 predictor, then interleaves the
                // shuffled even and odd byte planes. No encoder code is reused.
                for (int i = 1; i < predicted.Length; i++)
                {
                    predicted[i] = unchecked((byte)(predicted[i - 1] + predicted[i] - 128));
                }
                unpacked = new byte[rowBytes];
                int first = 0;
                int second = (rowBytes + 1) / 2;
                for (int i = 0; i < rowBytes; i += 2)
                {
                    unpacked[i] = predicted[first++];
                    if (i + 1 < rowBytes)
                    {
                        unpacked[i + 1] = predicted[second++];
                    }
                }
            }
            for (int channel = 0; channel < channels.Length; channel++)
            {
                for (int x = 0; x < width; x++)
                {
                    unpacked.AsSpan((channel * width + x) * 2, 2).CopyTo(
                        rgba.AsSpan((y * width + x) * 8 + channels[channel] * 2, 2));
                }
            }
            chunkPosition += 8 + payloadBytes;
        }
        Require(chunkPosition == file.Length, "No trailing or unaccounted output.");
        return rgba;
    }

    private static int[] Channels(byte[] file, Dictionary<string, AttributeValue> attributes)
    {
        Require(attributes.TryGetValue("channels", out AttributeValue value) && value.Type == "chlist",
            "Channel list.");
        ReadOnlySpan<byte> bytes = file.AsSpan(value.Offset, value.Length);
        int position = 0;
        var result = new List<int>();
        string? previous = null;
        while (true)
        {
            string name = CString(bytes, ref position);
            if (name.Length == 0)
            {
                break;
            }
            Require(previous is null || StringComparer.Ordinal.Compare(previous, name) < 0,
                "Unique lexically ordered channels.");
            previous = name;
            int rgbaChannel = name switch
            {
                "R" => 0,
                "G" => 1,
                "B" => 2,
                "A" => 3,
                _ => throw new InvalidDataException("Unexpected channel.")
            };
            Require(position <= bytes.Length - 16 && I32(bytes, position) == 1 &&
                bytes[position + 4] is 0 or 1 &&
                bytes[position + 5] == 0 && bytes[position + 6] == 0 && bytes[position + 7] == 0 &&
                I32(bytes, position + 8) == 1 && I32(bytes, position + 12) == 1,
                "Full-resolution binary16 channel descriptor.");
            position += 16;
            result.Add(rgbaChannel);
        }
        Require(position == bytes.Length && result.Count == 4, "Exactly four RGBA channels.");
        return result.ToArray();
    }

    private static ReadOnlySpan<byte> Attribute(
        byte[] file, Dictionary<string, AttributeValue> attributes, string name, string type, int size)
    {
        Require(attributes.TryGetValue(name, out AttributeValue value) &&
            value.Type == type && value.Length == size, $"Attribute contract: {name}.");
        return file.AsSpan(value.Offset, value.Length);
    }

    private static string StringAttribute(
        byte[] file, Dictionary<string, AttributeValue> attributes, string name)
    {
        Require(attributes.TryGetValue(name, out AttributeValue value) && value.Type == "string" &&
            value.Length <= 128, $"String metadata: {name}.");
        return Encoding.UTF8.GetString(file, value.Offset, value.Length);
    }

    private static string CString(ReadOnlySpan<byte> bytes, ref int position)
    {
        Require(position >= 0 && position < bytes.Length, "CString location.");
        int count = bytes[position..].IndexOf((byte)0);
        Require(count is >= 0 and <= 255, "Bounded header name.");
        string text = Encoding.ASCII.GetString(bytes.Slice(position, count));
        position += count + 1;
        return text;
    }

    private static uint U32(ReadOnlySpan<byte> bytes, int offset)
    {
        Require(offset >= 0 && offset <= bytes.Length - 4, "UInt32 extent.");
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
    }

    private static int I32(ReadOnlySpan<byte> bytes, int offset) => unchecked((int)U32(bytes, offset));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    private readonly record struct AttributeValue(string Type, int Offset, int Length);
}
