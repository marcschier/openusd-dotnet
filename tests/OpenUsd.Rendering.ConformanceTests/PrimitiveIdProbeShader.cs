// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

internal static class PrimitiveIdProbeShader
{
    private const string ProgramName = "conformance.primitiveid.fragment";
    private const string SourcePath = "eng/shaders/sources/conformance.primitiveid.fragment.slang";
    private const string EntryPoint = "primitiveIdProbeFragmentMain";

    internal static SilkShaderModuleDescriptor Load(
        SilkShaderBinaryFormat format,
        SilkShaderModuleDescriptor vertex)
    {
        string repository = FindRepository();
        string checkedDirectory = Path.Combine(repository, "eng", "shaders", "checked");
        byte[] manifestBytes = File.ReadAllBytes(Path.Combine(checkedDirectory, "manifest.json"));
        using Stream embedded = typeof(SilkCheckedShaderAssets).Assembly.GetManifestResourceStream(
            "OpenUsd.Rendering.Silk.Shaders.manifest.json") ??
            throw new InvalidDataException("The embedded checked shader manifest is missing.");
        using var embeddedBytes = new MemoryStream();
        embedded.CopyTo(embeddedBytes);
        if (!manifestBytes.AsSpan().SequenceEqual(embeddedBytes.ToArray()))
        {
            throw new InvalidDataException(
                "The deployed renderer and repository checked shader manifests differ; rebuild after regeneration.");
        }

        using JsonDocument document = JsonDocument.Parse(manifestBytes);
        JsonElement manifest = document.RootElement;
        ValidateInput(
            manifest,
            "eng/shaders/shader-manifest.json",
            Path.Combine(repository, "eng", "shaders", "shader-manifest.json"));
        ValidateInput(
            manifest,
            SourcePath,
            Path.Combine(repository, "eng", "shaders", "sources", $"{ProgramName}.slang"));

        JsonElement program = FindProgram(manifest, ProgramName);
        if (program.GetProperty("source").GetString() != SourcePath ||
            program.GetProperty("entryPoint").GetString() != EntryPoint ||
            program.GetProperty("stage").GetString() != "fragment" ||
            program.GetProperty("profile").GetString() != "ps_6_0" ||
            program.GetProperty("resourceContract").GetArrayLength() != 0)
        {
            throw new InvalidDataException(
                "The checked primitive-ID fragment does not match the resource-free probe contract.");
        }

        byte[] reflectionBytes = ReadArtifact(checkedDirectory, program, "reflection.json");
        using JsonDocument reflectionDocument = JsonDocument.Parse(reflectionBytes);
        JsonElement reflection = reflectionDocument.RootElement;
        JsonElement[] primitiveInputs = reflection.GetProperty("stageInputs").EnumerateArray()
            .Where(input => input.GetProperty("semantic").GetProperty("name").GetString() ==
                "SV_PRIMITIVEID")
            .ToArray();
        if (reflection.GetProperty("entryPoint").GetProperty("name").GetString() != EntryPoint ||
            reflection.GetProperty("resources").GetArrayLength() != 0 ||
            primitiveInputs.Length != 1 ||
            !primitiveInputs[0].GetProperty("semantic").GetProperty("systemValue").GetBoolean() ||
            primitiveInputs[0].GetProperty("type").GetProperty("kind").GetString() != "scalar" ||
            primitiveInputs[0].GetProperty("type").GetProperty("scalarType").GetString() != "uint32")
        {
            throw new InvalidDataException(
                "The checked primitive-ID reflection does not expose one uint SV_PrimitiveID input.");
        }

        if (format == SilkShaderBinaryFormat.MetalLibrary)
        {
            string metalSource = Encoding.UTF8.GetString(ReadArtifact(checkedDirectory, program, "metal"));
            if (!metalSource.Contains("[[primitive_id]]", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The checked Metal fragment does not declare the primitive_id system input.");
            }
            return new SilkShaderModuleDescriptor(
                SilkShaderStage.Fragment,
                format,
                EntryPoint,
                vertex.Code);
        }

        string suffix = format switch
        {
            SilkShaderBinaryFormat.Dxil => "dxil",
            SilkShaderBinaryFormat.SpirV => "spv",
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        JsonElement vertexProgram = FindProgram(manifest, "pick.vertex");
        VerifyArtifact(
            vertexProgram.GetProperty("artifacts").GetProperty(suffix),
            $"eng/shaders/checked/pick.vertex.{suffix}",
            vertex.Code.Span);
        byte[] code = ReadArtifact(checkedDirectory, program, suffix);
        if (format == SilkShaderBinaryFormat.SpirV)
        {
            ValidateSpirvPrimitiveInput(code);
        }
        return new SilkShaderModuleDescriptor(
            SilkShaderStage.Fragment,
            format,
            format == SilkShaderBinaryFormat.SpirV ? "main" : EntryPoint,
            code);
    }

    private static string FindRepository()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException(
            "The primitive-ID conformance probe requires the repository checked shader payload.");
    }

    private static JsonElement FindProgram(JsonElement manifest, string name)
    {
        JsonElement[] matches = manifest.GetProperty("programs").EnumerateArray()
            .Where(program => program.GetProperty("name").GetString() == name)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException(
                $"The checked shader manifest must contain one '{name}' program; regenerate it.");
        }
        return matches[0];
    }

    private static void ValidateInput(JsonElement manifest, string path, string localPath)
    {
        JsonElement[] matches = manifest.GetProperty("inputs").EnumerateArray()
            .Where(input => input.GetProperty("path").GetString() == path)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException($"The checked shader input '{path}' is missing or duplicated.");
        }
        VerifyHash(matches[0], File.ReadAllBytes(localPath), path);
    }

    private static byte[] ReadArtifact(string directory, JsonElement program, string suffix)
    {
        string name = program.GetProperty("name").GetString() ??
            throw new InvalidDataException("A checked shader program has no name.");
        string fileName = $"{name}.{suffix}";
        byte[] bytes = File.ReadAllBytes(Path.Combine(directory, fileName));
        VerifyArtifact(
            program.GetProperty("artifacts").GetProperty(suffix),
            $"eng/shaders/checked/{fileName}",
            bytes);
        return bytes;
    }

    private static void VerifyArtifact(
        JsonElement record,
        string expectedPath,
        ReadOnlySpan<byte> bytes)
    {
        if (record.GetProperty("path").GetString() != expectedPath ||
            record.GetProperty("size").GetInt64() != bytes.Length ||
            bytes.IsEmpty)
        {
            throw new InvalidDataException($"The checked shader artifact '{expectedPath}' has stale metadata.");
        }
        VerifyHash(record, bytes, expectedPath);
    }

    private static void VerifyHash(JsonElement record, ReadOnlySpan<byte> bytes, string path)
    {
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(record.GetProperty("sha256").GetString(), hash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The checked shader input or artifact '{path}' has a stale hash.");
        }
    }

    private static void ValidateSpirvPrimitiveInput(ReadOnlySpan<byte> code)
    {
        const uint opDecorate = 71;
        const uint opVariable = 59;
        const uint opLoad = 61;
        const uint decorationBuiltIn = 11;
        const uint builtInPrimitiveId = 7;
        const uint storageClassInput = 1;
        if (code.Length < 20 || code.Length % sizeof(uint) != 0 ||
            ReadWord(code, 0) != 0x07230203)
        {
            throw new InvalidDataException("The primitive-ID SPIR-V module has an invalid header.");
        }
        var decorated = new HashSet<uint>();
        var inputs = new HashSet<uint>();
        var loaded = new HashSet<uint>();
        int words = code.Length / sizeof(uint);
        for (int offset = 5; offset < words;)
        {
            uint instruction = ReadWord(code, offset);
            int count = (int)(instruction >> 16);
            uint opcode = instruction & 0xffff;
            if (count == 0 || count > words - offset)
            {
                throw new InvalidDataException("The primitive-ID SPIR-V module has a truncated instruction.");
            }
            if (opcode == opDecorate && count >= 4 &&
                ReadWord(code, offset + 2) == decorationBuiltIn &&
                ReadWord(code, offset + 3) == builtInPrimitiveId)
            {
                decorated.Add(ReadWord(code, offset + 1));
            }
            else if (opcode == opVariable && count >= 4 &&
                ReadWord(code, offset + 3) == storageClassInput)
            {
                inputs.Add(ReadWord(code, offset + 2));
            }
            else if (opcode == opLoad && count >= 4)
            {
                loaded.Add(ReadWord(code, offset + 3));
            }
            offset += count;
        }
        if (!decorated.Any(id => inputs.Contains(id) && loaded.Contains(id)))
        {
            throw new InvalidDataException(
                "The checked SPIR-V fragment does not load an Input decorated BuiltIn PrimitiveId.");
        }
    }

    private static uint ReadWord(ReadOnlySpan<byte> code, int wordOffset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(code.Slice(wordOffset * sizeof(uint), sizeof(uint)));
}
