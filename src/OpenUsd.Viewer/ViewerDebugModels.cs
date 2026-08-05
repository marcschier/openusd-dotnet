// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Viewer;

internal enum ViewerPickMode
{
    Prims,
    Models,
    Instances,
    Prototypes
}

internal static class ViewerPickModeResolver
{
    internal static string ResolvePath(
        string primPath,
        ViewerPickMode mode,
        Func<string, bool> exists,
        Func<string, bool> isModel,
        Func<string, bool> isInstance,
        Func<string, bool> isPrototype,
        Func<string, string> getPrototypePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentNullException.ThrowIfNull(exists);
        ArgumentNullException.ThrowIfNull(isModel);
        ArgumentNullException.ThrowIfNull(isInstance);
        ArgumentNullException.ThrowIfNull(isPrototype);
        ArgumentNullException.ThrowIfNull(getPrototypePath);

        if (!exists(primPath))
        {
            return primPath;
        }

        return mode switch
        {
            ViewerPickMode.Prims => primPath,
            ViewerPickMode.Models => FindAncestor(primPath, path => exists(path) && isModel(path)) ?? primPath,
            ViewerPickMode.Instances => FindAncestor(primPath, path => exists(path) && isInstance(path)) ?? primPath,
            ViewerPickMode.Prototypes => ResolvePrototypePath(
                primPath,
                exists,
                isInstance,
                isPrototype,
                getPrototypePath),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    private static string ResolvePrototypePath(
        string primPath,
        Func<string, bool> exists,
        Func<string, bool> isInstance,
        Func<string, bool> isPrototype,
        Func<string, string> getPrototypePath)
    {
        string? prototype = FindAncestor(primPath, path => exists(path) && isPrototype(path));
        if (!string.IsNullOrEmpty(prototype))
        {
            return prototype;
        }

        string? instance = FindAncestor(primPath, path => exists(path) && isInstance(path));
        if (string.IsNullOrEmpty(instance))
        {
            return primPath;
        }

        string prototypePath = getPrototypePath(instance);
        return string.IsNullOrWhiteSpace(prototypePath) ? primPath : prototypePath;
    }

    private static string? FindAncestor(string path, Func<string, bool> predicate)
    {
        string? current = path;
        while (current is not null)
        {
            if (predicate(current))
            {
                return current;
            }
            current = GetParentPath(current);
        }
        return null;
    }

    private static string? GetParentPath(string path)
    {
        int separator = path.LastIndexOf('/');
        return separator > 0 ? path[..separator] : null;
    }
}

internal sealed record ViewerTfDebugFlag(string Name, string Description, bool Enabled);

internal static class ViewerTfDebugFormatter
{
    internal static string FormatStatus(IReadOnlyList<ViewerTfDebugFlag> flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        int enabled = flags.Count(flag => flag.Enabled);
        return flags.Count == 0
            ? "TfDebug: no symbols are registered."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"TfDebug: {enabled} of {flags.Count} symbols enabled.");
    }
}

internal sealed class ViewerTfDebugPanelModel(
    Func<IReadOnlyList<string>> getSymbolNames,
    Func<string, string> getDescription,
    Func<string, bool> getEnabled,
    Func<string, bool, bool> setEnabled)
{
    internal ViewerTfDebugPanelModel()
        : this(
            TfDebug.GetSymbolNames,
            TfDebug.GetSymbolDescription,
            TfDebug.GetSymbolEnabled,
            TfDebug.SetSymbolEnabled)
    {
    }

    internal ViewerTfDebugFlag[] Load()
    {
        return getSymbolNames()
            .Order(StringComparer.Ordinal)
            .Select(name => new ViewerTfDebugFlag(
                name,
                getDescription(name),
                getEnabled(name)))
            .ToArray();
    }

    internal ViewerTfDebugFlag SetEnabled(string name, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        setEnabled(name, enabled);
        return new ViewerTfDebugFlag(name, getDescription(name), getEnabled(name));
    }
}

internal sealed record ViewerHydraSceneEntry(
    string Kind,
    string Path,
    int PrimId,
    int InstanceId,
    int InstanceIndex,
    string Topology,
    int Points,
    int Indices,
    int Primitives,
    string MaterialPath);

internal sealed record ViewerHydraSceneSnapshot(
    DateTimeOffset Timestamp,
    ulong PageRevision,
    uint CommandCount,
    ViewerHydraSceneEntry[] Entries)
{
    internal static ViewerHydraSceneSnapshot Empty { get; } =
        new(DateTimeOffset.MinValue, 0, 0, []);

    internal static ViewerHydraSceneSnapshot FromPage(OpenUsdSilkPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return FromCommandPage(
            page.GetEnumerator(),
            page.Revision,
            page.CommandCount);
    }

    internal static ViewerHydraSceneSnapshot FromCommands(
        ReadOnlySpan<byte> data,
        uint commandCount,
        ulong pageRevision)
    {
        SilkCommandEnumerator enumerator = SilkCommandParser.Enumerate(
            data,
            commandCount,
            SilkCommandParser.PageAbiVersion);
        return FromCommandPage(enumerator, pageRevision, commandCount);
    }

    private static ViewerHydraSceneSnapshot FromCommandPage(
        SilkCommandEnumerator enumerator,
        ulong pageRevision,
        uint commandCount)
    {
        var entries = new List<ViewerHydraSceneEntry>();
        try
        {
            while (enumerator.MoveNext())
            {
                SilkCommand command = enumerator.Current;
                switch (command.Type)
                {
                    case SilkCommandType.MeshUpsert:
                        SilkMeshUpsertCommand mesh = command.AsMeshUpsert();
                        entries.Add(new ViewerHydraSceneEntry(
                            mesh.IsInstanceReference ? "Mesh instance reference" : "Mesh",
                            mesh.Path,
                            mesh.PrimId,
                            mesh.InstanceId,
                            mesh.InstanceIndex,
                            mesh.TopologyKind.ToString(),
                            mesh.PointCount,
                            mesh.IndexCount,
                            mesh.TriangleCount,
                            mesh.MaterialPath));
                        break;
                    case SilkCommandType.MeshRemove:
                        SilkMeshRemoveCommand remove = command.AsMeshRemove();
                        entries.Add(new ViewerHydraSceneEntry(
                            "Mesh removal",
                            remove.Path,
                            PrimId: 0,
                            InstanceId: 0,
                            remove.InstanceIndex,
                            Topology: "—",
                            Points: 0,
                            Indices: 0,
                            Primitives: 0,
                            MaterialPath: string.Empty));
                        break;
                }
            }
        }
        finally
        {
            enumerator.Dispose();
        }
        return new ViewerHydraSceneSnapshot(
            DateTimeOffset.UtcNow,
            pageRevision,
            commandCount,
            entries.ToArray());
    }

    internal string Format()
    {
        if (Timestamp == DateTimeOffset.MinValue)
        {
            return "No Hydra scene snapshot has been observed. Render a frame with an hdSilk backend.";
        }

        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"Hydra scene revision {PageRevision}; ");
        builder.Append(CultureInfo.InvariantCulture, $"commands {CommandCount}; ");
        builder.Append(CultureInfo.InvariantCulture, $"mesh records {Entries.Length}");
        foreach (ViewerHydraSceneEntry entry in Entries.OrderBy(entry => entry.Path, StringComparer.Ordinal))
        {
            builder.AppendLine();
            builder.Append(CultureInfo.InvariantCulture, $"{entry.Kind}: {entry.Path}");
            builder.Append(CultureInfo.InvariantCulture, $" primId={entry.PrimId}");
            if (entry.InstanceId != 0)
            {
                builder.Append(CultureInfo.InvariantCulture, $" instance={entry.InstanceId}[{entry.InstanceIndex}]");
            }
            builder.Append(CultureInfo.InvariantCulture, $" topology={entry.Topology}");
            builder.Append(CultureInfo.InvariantCulture, $" points={entry.Points}");
            builder.Append(CultureInfo.InvariantCulture, $" indices={entry.Indices}");
            builder.Append(CultureInfo.InvariantCulture, $" primitives={entry.Primitives}");
            if (!string.IsNullOrEmpty(entry.MaterialPath))
            {
                builder.Append(CultureInfo.InvariantCulture, $" material={entry.MaterialPath}");
            }
        }
        return builder.ToString();
    }
}

internal static class ViewerFrameBitmapWriter
{
    internal static void WriteBmp(string path, int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int rowStride = checked(((width * 3) + 3) & ~3);
        int imageBytes = checked(rowStride * height);
        int rgbaBytes = checked(width * height * 4);
        if (rgba.Length != rgbaBytes)
        {
            throw new ArgumentException("The RGBA buffer size does not match the image dimensions.", nameof(rgba));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        Span<byte> header = stackalloc byte[54];
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        WriteInt32(header[2..], checked(54 + imageBytes));
        WriteInt32(header[10..], 54);
        WriteInt32(header[14..], 40);
        WriteInt32(header[18..], width);
        WriteInt32(header[22..], height);
        WriteInt16(header[26..], 1);
        WriteInt16(header[28..], 24);
        WriteInt32(header[34..], imageBytes);
        stream.Write(header);

        byte[] bgr = new byte[imageBytes];
        for (int y = 0; y < height; y++)
        {
            int sourceY = height - 1 - y;
            int sourceRow = checked(sourceY * width * 4);
            int destinationRow = checked(y * rowStride);
            for (int x = 0; x < width; x++)
            {
                int source = sourceRow + (x * 4);
                int destination = destinationRow + (x * 3);
                bgr[destination] = rgba[source + 2];
                bgr[destination + 1] = rgba[source + 1];
                bgr[destination + 2] = rgba[source];
            }
        }
        stream.Write(bgr);
    }

    private static void WriteInt16(Span<byte> destination, short value)
    {
        destination[0] = (byte)value;
        destination[1] = (byte)(value >> 8);
    }

    private static void WriteInt32(Span<byte> destination, int value)
    {
        destination[0] = (byte)value;
        destination[1] = (byte)(value >> 8);
        destination[2] = (byte)(value >> 16);
        destination[3] = (byte)(value >> 24);
    }
}
