// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

internal static class FlatOrdinalProbeShader
{
    private const string Program = "flat-ordinal.fragment";
    private const string EntryPoint = "flatOrdinalTransportFragmentMain";
    private const string VertexProgram = "flat-ordinal.vertex";
    private const string VertexEntryPoint = "flatOrdinalTransportVertexMain";
    // The generic Metal pipeline compiles source only for this host-visible entry name.
    private const string MetalHostEntryPoint = "main";
    private const string TestDirectory = "tests/OpenUsd.Rendering.ConformanceTests/FlatOrdinalShaders";

    internal static (SilkShaderModuleDescriptor Vertex, SilkShaderModuleDescriptor Fragment) Load(
        SilkShaderBinaryFormat format)
    {
        string repository = FindRepository();
        string shaderDirectory = Path.Combine(
            repository, "tests", "OpenUsd.Rendering.ConformanceTests", "FlatOrdinalShaders");
        string checkedDirectory = Path.Combine(shaderDirectory, "checked");
        string receiptPath = Path.Combine(checkedDirectory, "manifest.json");
        if (!File.Exists(receiptPath))
        {
            throw new InvalidDataException(
                "The test-only flat-ordinal shader payload is missing; generate it with " +
                "FlatOrdinalShaders/build_flat_ordinal_shader.py on the locked Windows toolchain.");
        }

        using JsonDocument receiptDocument = JsonDocument.Parse(File.ReadAllBytes(receiptPath));
        JsonElement receipt = receiptDocument.RootElement;
        if (receipt.GetProperty("schemaVersion").GetInt32() != 2 ||
            !receipt.TryGetProperty("programs", out JsonElement receiptPrograms) ||
            receiptPrograms.GetArrayLength() != 2 ||
            receiptPrograms[0].GetString() != Program ||
            receiptPrograms[1].GetString() != VertexProgram)
        {
            throw new InvalidDataException(
                "The test-only flat-ordinal shader receipt is stale or invalid; regenerate it.");
        }

        JsonElement inputs = receipt.GetProperty("inputs");
        if (inputs.GetArrayLength() != 7)
        {
            throw new InvalidDataException("The test-only shader receipt has unexpected inputs.");
        }
        byte[] source = ReadVerified(inputs, $"{TestDirectory}/{Program}.slang",
            Path.Combine(shaderDirectory, $"{Program}.slang"));
        byte[] vertexSource = ReadVerified(inputs, $"{TestDirectory}/{VertexProgram}.slang",
            Path.Combine(shaderDirectory, $"{VertexProgram}.slang"));
        byte[] contractBytes = ReadVerified(inputs, $"{TestDirectory}/flat-ordinal.manifest.json",
            Path.Combine(shaderDirectory, "flat-ordinal.manifest.json"));
        _ = ReadVerified(inputs, $"{TestDirectory}/build_flat_ordinal_shader.py",
            Path.Combine(shaderDirectory, "build_flat_ordinal_shader.py"));
        _ = ReadVerified(inputs, "eng/shaders/toolchain.lock.json",
            Path.Combine(repository, "eng", "shaders", "toolchain.lock.json"));
        _ = ReadVerified(inputs, "eng/shaders/scripts/normalize-reflection.py",
            Path.Combine(repository, "eng", "shaders", "scripts", "normalize-reflection.py"));
        _ = ReadVerified(inputs, "eng/shaders/scripts/shader_model.py",
            Path.Combine(repository, "eng", "shaders", "scripts", "shader_model.py"));

        using JsonDocument contractDocument = JsonDocument.Parse(contractBytes);
        ValidateContract(contractDocument.RootElement, receipt);
        if (!Encoding.UTF8.GetString(source).Contains(
                "nointerpolation float2 ordinal : TEXCOORD0", StringComparison.Ordinal) ||
            !Encoding.UTF8.GetString(vertexSource).Contains(
                "nointerpolation float2 ordinal : TEXCOORD0", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The test shader sources do not declare a flat ordinal in both stages.");
        }

        JsonElement artifacts = receipt.GetProperty("artifacts");
        if (artifacts.GetArrayLength() != 8)
        {
            throw new InvalidDataException("The test-only shader receipt has unexpected artifacts.");
        }
        byte[] dxil = ReadArtifact(artifacts, checkedDirectory, $"{Program}.dxil");
        byte[] spirv = ReadArtifact(artifacts, checkedDirectory, $"{Program}.spv");
        byte[] metal = ReadArtifact(artifacts, checkedDirectory, $"{Program}.metal");
        byte[] reflection = ReadArtifact(artifacts, checkedDirectory, $"{Program}.reflection.json");
        _ = ReadArtifact(artifacts, checkedDirectory, $"{Program}.dxil-reflection.json");
        _ = ReadArtifact(artifacts, checkedDirectory, $"{Program}.spirv-reflection.json");
        byte[] vertexDxil = ReadArtifact(artifacts, checkedDirectory, $"{VertexProgram}.dxil");
        byte[] vertexReflection = ReadArtifact(
            artifacts, checkedDirectory, $"{VertexProgram}.dxil-reflection.json");
        if (!dxil.AsSpan().StartsWith("DXBC"u8) || !vertexDxil.AsSpan().StartsWith("DXBC"u8))
        {
            throw new InvalidDataException("A flat-ordinal DXIL container is invalid.");
        }
        ValidateVertexReflection(vertexReflection);
        ValidateSpirv(spirv, requireFlatInput: true);
        ValidateMetal(metal);
        ValidateReflection(reflection);

        // The DXIL pair declares the same ordered, flat output/input interface.
        SilkShaderModuleDescriptor vertex = format == SilkShaderBinaryFormat.Dxil
            ? new SilkShaderModuleDescriptor(SilkShaderStage.Vertex, format, VertexEntryPoint, vertexDxil)
            : SilkCheckedShaderAssets.LoadMaskedShadowVertex(format);
        if (format == SilkShaderBinaryFormat.SpirV)
        {
            ValidateSpirv(vertex.Code.Span, requireFlatInput: false);
        }

        var fragment = new SilkShaderModuleDescriptor(
            SilkShaderStage.Fragment,
            format,
            format == SilkShaderBinaryFormat.MetalLibrary ? MetalHostEntryPoint : EntryPoint,
            format switch
            {
                SilkShaderBinaryFormat.Dxil => dxil,
                SilkShaderBinaryFormat.SpirV => spirv,
                SilkShaderBinaryFormat.MetalLibrary => metal,
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            });
        return (vertex, fragment);
    }

    private static void ValidateContract(JsonElement contract, JsonElement receipt)
    {
        JsonElement programs = contract.GetProperty("programs");
        if (contract.GetProperty("schemaVersion").GetInt32() != 1 ||
            contract.GetProperty("matrixLayout").GetString() != "row-major" ||
            programs.GetArrayLength() != 2)
        {
            throw new InvalidDataException("The flat-ordinal shader contract is invalid.");
        }
        JsonElement program = programs[0];
        if (program.GetProperty("name").GetString() != Program ||
            program.GetProperty("source").GetString() != $"{Program}.slang" ||
            program.GetProperty("entryPoint").GetString() != EntryPoint ||
            program.GetProperty("stage").GetString() != "fragment" ||
            program.GetProperty("profile").GetString() != "ps_6_0" ||
            program.GetProperty("resources").GetArrayLength() != 0)
        {
            throw new InvalidDataException("The flat-ordinal fragment contract has changed.");
        }
        JsonElement vertex = programs[1];
        JsonElement vertexResources = vertex.GetProperty("resources");
        if (vertex.GetProperty("name").GetString() != VertexProgram ||
            vertex.GetProperty("source").GetString() != $"{VertexProgram}.slang" ||
            vertex.GetProperty("entryPoint").GetString() != VertexEntryPoint ||
            vertex.GetProperty("stage").GetString() != "vertex" ||
            vertex.GetProperty("profile").GetString() != "vs_6_0" ||
            vertexResources.GetArrayLength() != 1)
        {
            throw new InvalidDataException("The flat-ordinal vertex contract has changed.");
        }
        JsonElement resource = vertexResources[0];
        JsonElement d3d = resource.GetProperty("d3d");
        JsonElement vulkan = resource.GetProperty("vulkan");
        if (resource.GetProperty("name").GetString() != "flatOrdinalInstances" ||
            resource.GetProperty("access").GetString() != "read" ||
            d3d.GetProperty("registerClass").GetString() != "t" ||
            d3d.GetProperty("register").GetInt32() != 6 ||
            d3d.GetProperty("space").GetInt32() != 0 ||
            vulkan.GetProperty("binding").GetInt32() != 6 ||
            vulkan.GetProperty("set").GetInt32() != 0)
        {
            throw new InvalidDataException("The flat-ordinal vertex binding contract has changed.");
        }
        JsonElement expectedToolchain = contract.GetProperty("toolchain");
        JsonElement actualToolchain = receipt.GetProperty("toolchain");
        string[] fields =
        [
            "slangVersion", "slangCommit", "spirvToolsCommit", "shaderModel",
            "vulkanVersion", "spirvVersion", "metalVersion"
        ];
        if (expectedToolchain.EnumerateObject().Count() != fields.Length ||
            actualToolchain.EnumerateObject().Count() != fields.Length ||
            fields.Any(field =>
                expectedToolchain.GetProperty(field).GetString() !=
                actualToolchain.GetProperty(field).GetString()))
        {
            throw new InvalidDataException("The flat-ordinal shader toolchain receipt is stale.");
        }
    }

    private static byte[] ReadArtifact(JsonElement records, string directory, string name) =>
        ReadVerified(records, $"{TestDirectory}/checked/{name}", Path.Combine(directory, name));

    private static byte[] ReadVerified(JsonElement records, string expectedPath, string localPath)
    {
        JsonElement[] matches = records.EnumerateArray()
            .Where(item => item.GetProperty("path").GetString() == expectedPath)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException($"The test shader receipt does not identify '{expectedPath}' exactly once.");
        }
        byte[] bytes = File.ReadAllBytes(localPath);
        if (bytes.Length == 0 || matches[0].GetProperty("size").GetInt64() != bytes.Length ||
            !string.Equals(
                matches[0].GetProperty("sha256").GetString(),
                Convert.ToHexString(SHA256.HashData(bytes)),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The test shader input or artifact '{expectedPath}' is stale.");
        }
        return bytes;
    }

    private static void ValidateVertexReflection(ReadOnlySpan<byte> bytes)
    {
        using JsonDocument document = JsonDocument.Parse(bytes.ToArray());
        JsonElement entries = document.RootElement.GetProperty("entryPoints");
        if (entries.GetArrayLength() != 1 ||
            entries[0].GetProperty("name").GetString() != VertexEntryPoint ||
            entries[0].GetProperty("stage").GetString() != "vertex")
        {
            throw new InvalidDataException("The test-only DXIL vertex reflection is invalid.");
        }
    }

    private static void ValidateReflection(ReadOnlySpan<byte> bytes)
    {
        using JsonDocument document = JsonDocument.Parse(bytes.ToArray());
        JsonElement reflection = document.RootElement;
        JsonElement entry = reflection.GetProperty("entryPoint");
        JsonElement inputs = reflection.GetProperty("stageInputs");
        if (reflection.GetProperty("source").GetString() != $"{Program}.slang" ||
            entry.GetProperty("name").GetString() != EntryPoint ||
            entry.GetProperty("stage").GetString() != "fragment" ||
            entry.GetProperty("profile").GetString() != "ps_6_0" ||
            reflection.GetProperty("resources").GetArrayLength() != 0 ||
            inputs.GetArrayLength() != 2)
        {
            throw new InvalidDataException("The flat-ordinal shader reflection is invalid.");
        }
        JsonElement position = inputs[0];
        JsonElement positionSemantic = position.GetProperty("semantic");
        JsonElement positionType = position.GetProperty("type");
        if (positionSemantic.GetProperty("name").GetString() != "SV_POSITION" ||
            positionSemantic.GetProperty("index").GetInt32() != 0 ||
            !positionSemantic.GetProperty("systemValue").GetBoolean() ||
            position.GetProperty("location").ValueKind != JsonValueKind.Null ||
            positionType.GetProperty("kind").GetString() != "vector" ||
            positionType.GetProperty("elementCount").GetInt32() != 4 ||
            positionType.GetProperty("elementType").GetProperty("scalarType").GetString() != "float32")
        {
            throw new InvalidDataException("The fragment must declare SV_Position before TEXCOORD0.");
        }
        JsonElement input = inputs[1];
        JsonElement semantic = input.GetProperty("semantic");
        JsonElement type = input.GetProperty("type");
        if (semantic.GetProperty("name").GetString() != "TEXCOORD" ||
            semantic.GetProperty("index").GetInt32() != 0 ||
            semantic.GetProperty("systemValue").GetBoolean() ||
            input.GetProperty("location").GetInt32() != 0 ||
            type.GetProperty("kind").GetString() != "vector" ||
            type.GetProperty("elementCount").GetInt32() != 2 ||
            type.GetProperty("elementType").GetProperty("scalarType").GetString() != "float32")
        {
            throw new InvalidDataException("The flat-ordinal input must be a float32 TEXCOORD0 pair.");
        }
    }

    private static void ValidateMetal(ReadOnlySpan<byte> bytes)
    {
        string source = Encoding.UTF8.GetString(bytes);
        bool flatInput = source.Split('\n').Any(line =>
        {
            string compact = line.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("\t", string.Empty, StringComparison.Ordinal);
            return compact.Contains("user(TEXCOORD)", StringComparison.Ordinal) &&
                (compact.Contains("[[flat,", StringComparison.Ordinal) ||
                    compact.Contains(",flat,", StringComparison.Ordinal) ||
                    compact.Contains(",flat]]", StringComparison.Ordinal) ||
                    compact.Contains("[[flat]]", StringComparison.Ordinal));
        });
        bool entryPoint = source.Split('\n').Count(line =>
            line.Contains($"[[host_name(\"{MetalHostEntryPoint}\")]] [[fragment]]", StringComparison.Ordinal) &&
            line.Contains($" {EntryPoint}(", StringComparison.Ordinal)) == 1;
        if (!source.Contains("#include <metal_stdlib>", StringComparison.Ordinal) ||
            !entryPoint ||
            source.Contains("primitive_id", StringComparison.Ordinal) ||
            !flatInput)
        {
            throw new InvalidDataException(
                "The test MSL must expose a flat TEXCOORD and the named fragment with the Metal host alias.");
        }
    }

    private static void ValidateSpirv(ReadOnlySpan<byte> code, bool requireFlatInput)
    {
        if (code.Length < 20 || code.Length % sizeof(uint) != 0 ||
            ReadWord(code, 0) != 0x07230203 || ReadWord(code, 1) != 0x00010500)
        {
            throw new InvalidDataException("The flat-ordinal SPIR-V module is not SPIR-V 1.5.");
        }
        var flat = new HashSet<uint>();
        var locationZero = new HashSet<uint>();
        var inputs = new HashSet<uint>();
        var loads = new HashSet<uint>();
        int fragmentEntries = 0;
        int words = code.Length / sizeof(uint);
        for (int offset = 5; offset < words;)
        {
            uint instruction = ReadWord(code, offset);
            int count = (int)(instruction >> 16);
            uint opcode = instruction & 0xffff;
            if (count == 0 || count > words - offset)
            {
                throw new InvalidDataException("The flat-ordinal SPIR-V module is truncated.");
            }
            if (opcode == 17 && count >= 2 && ReadWord(code, offset + 1) == 2)
            {
                throw new InvalidDataException("The flat-ordinal pipeline must not require Geometry capability.");
            }
            if (requireFlatInput && opcode == 15 && count >= 4 && ReadWord(code, offset + 1) == 4)
            {
                ReadOnlySpan<byte> name = code.Slice((offset + 3) * sizeof(uint), (count - 3) * sizeof(uint));
                int terminator = name.IndexOf((byte)0);
                if (terminator < 0 || Encoding.UTF8.GetString(name[..terminator]) != EntryPoint)
                {
                    throw new InvalidDataException("The flat-ordinal SPIR-V fragment entry point is incorrect.");
                }
                fragmentEntries++;
            }
            if (opcode == 71 && count >= 3)
            {
                uint target = ReadWord(code, offset + 1);
                uint decoration = ReadWord(code, offset + 2);
                if (decoration == 14)
                {
                    flat.Add(target);
                }
                else if (count >= 4 && decoration == 30 && ReadWord(code, offset + 3) == 0)
                {
                    locationZero.Add(target);
                }
                else if (count >= 4 && decoration == 11 && ReadWord(code, offset + 3) == 7)
                {
                    throw new InvalidDataException("The flat-ordinal pipeline must not use PrimitiveId.");
                }
            }
            else if (opcode == 59 && count >= 4 && ReadWord(code, offset + 3) == 1)
            {
                inputs.Add(ReadWord(code, offset + 2));
            }
            else if (opcode == 61 && count >= 4)
            {
                loads.Add(ReadWord(code, offset + 3));
            }
            offset += count;
        }
        if (requireFlatInput &&
            (fragmentEntries != 1 ||
                flat.Count(id => locationZero.Contains(id) && inputs.Contains(id) && loads.Contains(id)) != 1))
        {
            throw new InvalidDataException(
                "The fragment must have one named SPIR-V entry and load one flat input at location 0.");
        }
    }

    private static uint ReadWord(ReadOnlySpan<byte> code, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(code.Slice(offset * sizeof(uint), sizeof(uint)));

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
        throw new InvalidOperationException("The flat-ordinal probe requires the repository shader payload.");
    }
}
