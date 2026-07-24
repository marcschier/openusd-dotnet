// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Interop;

internal readonly record struct OpenUsdNativePayloadArc(
    string AssetPath,
    string TargetPrimPath,
    string SourceLayerIdentifier);

internal static class NativePayloadArcListDecoder
{
    internal const uint Version = 1;
    private const int FieldsPerArc = 3;

    internal static OpenUsdNativePayloadArc[] Decode(
        uint version,
        nuint arcCount,
        ReadOnlySpan<byte> data,
        ReadOnlySpan<nuint> offsets)
    {
        if (version != Version)
        {
            throw InvalidBuffer($"unsupported view version {version}");
        }
        if (arcCount > (nuint)(int.MaxValue / FieldsPerArc) ||
            arcCount > nuint.MaxValue / FieldsPerArc)
        {
            throw InvalidBuffer("the arc count is too large");
        }

        nuint entryCount = arcCount * FieldsPerArc;
        if ((nuint)offsets.Length != entryCount)
        {
            throw InvalidBuffer(
                $"count {arcCount} requires {entryCount} offsets, not {offsets.Length}");
        }

        string[] fields = NativePackedStringListDecoder.Decode(
            data,
            offsets,
            "payload-arc list buffer");
        var arcs = new OpenUsdNativePayloadArc[(int)arcCount];
        for (int index = 0; index < arcs.Length; index++)
        {
            int field = index * FieldsPerArc;
            string sourceLayerIdentifier = fields[field + 2];
            if (sourceLayerIdentifier.Length == 0)
            {
                throw InvalidBuffer("an arc has no introducing layer identifier");
            }
            arcs[index] = new OpenUsdNativePayloadArc(
                fields[field],
                fields[field + 1],
                sourceLayerIdentifier);
        }
        return arcs;
    }

    private static OpenUsdNativeException InvalidBuffer(string detail) =>
        new(
            OpenUsdNativeStatus.NativeError,
            $"The native runtime returned an invalid payload-arc list buffer: {detail}.");
}
