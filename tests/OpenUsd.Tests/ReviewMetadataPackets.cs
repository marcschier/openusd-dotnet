// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using OpenUsd.Editing;

namespace OpenUsd.Tests;

internal static class ReviewMetadataPackets
{
    internal const string DocumentId = "158868de-43ce-44dc-9373-bbf7e1124b4c";
    internal static readonly string Root = Path.GetFullPath(
        Path.Combine("artifacts", "portable-review-managed", "codec", "source.usda"));
    internal static readonly string Target = Path.ChangeExtension(Root, ".review");
    internal static readonly string Asset = Path.ChangeExtension(Root, ".png");
    internal static readonly string Fingerprint = new('a', 64);
    internal static readonly byte[] DocumentBytes = [0x10, 0x32, 0x54, 0x76, 0x98, 0xba, 0xdc, 0xfe];

    internal static byte[] Binding(string corruption = "", int dependencyCount = 2)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(corruption == "magic" ? 0u : 0x31425352u);
        writer.Write(corruption == "version" ? 2u : 1u);
        writer.Write(corruption == "stage-id" ? 0ul : 42ul);
        Metadata(writer, corruption, document: false, dependencyCount: dependencyCount);
        return Finish(stream, corruption);
    }

    internal static byte[] Envelope(
        string corruption = "", bool captured = true, byte[]? portableBytes = null,
        string? originalIdentifier = null, int dependencyCount = 2)
    {
        portableBytes ??= DocumentBytes;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(corruption == "magic" ? 0u : 0x31454452u);
        writer.Write(corruption == "version" ? 2u : 1u);
        writer.Write(corruption switch
        {
            "empty-document" => 0u,
            "document-limit" => (uint)UsdReviewDocumentCodec.MaximumDocumentBytes + 1,
            "document-overflow" => uint.MaxValue,
            _ => (uint)portableBytes.Length
        });
        if (corruption != "empty-document")
        {
            writer.Write(portableBytes);
        }
        byte[] receipt = captured ? [11, 22, 33, 44] : [];
        writer.Write(corruption == "receipt-overflow" ? uint.MaxValue : (uint)receipt.Length);
        writer.Write(receipt);
        Text(writer, corruption switch
        {
            "guid" => "not-a-guid",
            "guid-upper" => DocumentId.ToUpperInvariant(),
            "guid-braces" => "{" + DocumentId + "}",
            _ => DocumentId
        });
        Metadata(writer, corruption, document: true, originalIdentifier, dependencyCount);
        return Finish(stream, corruption);
    }

    internal static byte[] State(ulong stageId = 42, uint role = 2, uint flags = 42)
    {
        var writer = new UsdEditWriter();
        writer.Header(5);
        writer.U64(stageId);
        writer.U64(101);
        writer.U64(3);
        writer.U64(9);
        writer.U32(role);
        writer.U32(flags);
        writer.Text("owned:review.usda");
        writer.Text("");
        writer.Text("");
        writer.Text(Root);
        return writer.Written.ToArray();
    }

    internal static byte[] Inspection(string corruption = "")
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(corruption == "magic" ? 0u : 0x31494452u);
        writer.Write(corruption == "version" ? 2u : 1u);
        writer.Write(corruption switch
        {
            "empty-document" => 0u,
            "document-limit" => (uint)UsdReviewDocumentCodec.MaximumDocumentBytes + 1,
            "document-overflow" => uint.MaxValue,
            "document-mismatch" => (uint)DocumentBytes.Length + 1,
            _ => (uint)DocumentBytes.Length
        });
        Text(writer, corruption switch
        {
            "guid" => "not-a-guid",
            "guid-upper" => DocumentId.ToUpperInvariant(),
            "guid-braces" => "{" + DocumentId + "}",
            _ => DocumentId
        });
        Metadata(writer, corruption, document: true);
        return Finish(stream, corruption);
    }

    private static void Metadata(
        BinaryWriter writer, string corruption, bool document,
        string? originalIdentifier = null, int dependencyCount = 2)
    {
        switch (corruption)
        {
            case "utf8":
                writer.Write(2u);
                writer.Write(new byte[] { 0xc0, 0xaf });
                break;
            case "text-limit":
                writer.Write(4097u);
                writer.Write(new byte[4097]);
                break;
            case "text-overflow":
                writer.Write(uint.MaxValue);
                break;
            default:
                Text(writer, corruption switch
                {
                    "empty-root" => "",
                    "relative-root" => "source.usda",
                    "root-mismatch" => Target,
                    "nul" => Root + "\0",
                    _ => Root
                });
                break;
        }
        Text(writer, corruption switch
        {
            "fingerprint-size" => new string('a', 63),
            "fingerprint-hex" => new string('g', 64),
            "fingerprint-mismatch" => new string('b', 64),
            _ => Fingerprint
        });
        if (document)
        {
            Text(writer, corruption == "empty-identifier" ? "" : originalIdentifier ?? "anon:original-review.usda");
            Text(writer, corruption switch
            {
                "relative-target" => "review.urd",
                "target-mismatch" => Asset,
                _ => Target
            });
        }
        Text(writer, corruption switch
        {
            "relative-anchor" => "anchor",
            "anchor-mismatch" => Asset,
            _ => Root
        });
        writer.Write(corruption switch
        {
            "dependency-limit" => 1025u,
            "dependency-overflow" => uint.MaxValue,
            "dependency-extent" => 100u,
            _ => (uint)dependencyCount
        });
        Text(writer, Root);
        Text(writer, Fingerprint);
        writer.Write(321ul);
        writer.Write(0u);
        for (int index = 1; index < dependencyCount; index++)
        {
            Text(writer, corruption switch
            {
                "relative-dependency" => "texture.png",
                "duplicate-dependency" => Root,
                _ => index == 1 ? Asset : Path.ChangeExtension(Root, $".{index}.bin")
            });
            Text(writer, corruption switch
            {
                "dependency-sha-size" => "b",
                "dependency-sha-hex" => new string('z', 64),
                _ => new string('b', 64)
            });
            writer.Write(765ul);
            writer.Write(corruption == "dependency-kind" ? 2u : 1u);
        }
    }

    private static void Text(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write((uint)bytes.Length);
        writer.Write(bytes);
    }

    private static byte[] Finish(MemoryStream stream, string corruption)
    {
        byte[] bytes = stream.ToArray();
        return corruption switch
        {
            "truncated" => bytes[..^1],
            "trailing" => [.. bytes, 0],
            _ => bytes
        };
    }
}
