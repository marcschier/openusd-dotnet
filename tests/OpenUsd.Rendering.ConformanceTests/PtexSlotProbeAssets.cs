// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

internal static class PtexSlotProbeAssets
{
    internal static (SilkShaderModuleDescriptor Vertex, SilkShaderModuleDescriptor Fragment) Load(
        SilkShaderBinaryFormat format, bool sampling = false, bool mixed = false)
    {
        string output = Environment.GetEnvironmentVariable("OPENUSD_PTEX_SLOT_PROBE_ARTIFACTS") ??
            throw new InvalidDataException("The exact compiled Ptex probe directory is required.");
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OpenUsd.slnx")))
        {
            root = root.Parent;
        }
        string repository = root?.FullName ??
            throw new InvalidDataException("The probe requires its source repository.");
        string source = Path.Combine(repository, "tests", "OpenUsd.Rendering.ConformanceTests", "PtexSlotShaders");
        sampling |= mixed;
        string stem = mixed ? "ptex-slot-mixed" : sampling ? "ptex-slot-sampling" : "ptex-slot-probe";
        string receiptName = sampling ? $"{stem}-inputs.json" : "probe-inputs.json";
        using JsonDocument receipt = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, receiptName)));
        JsonElement manifest = receipt.RootElement;
        if (manifest.GetProperty("schemaVersion").GetInt32() != 1 ||
            manifest.GetProperty("inputs").EnumerateObject().Count() != (mixed ? 7 : sampling ? 5 : 4) ||
            manifest.GetProperty("artifacts").EnumerateObject().Count() != 12)
        {
            throw new InvalidDataException("The Ptex probe compilation receipt is invalid.");
        }
        if (sampling)
        {
            Verify(manifest.GetProperty("inputs"), "ptex-slot-sampling.slang",
                Path.Combine(source, "ptex-slot-sampling.slang"));
        }
        if (mixed)
        {
            Verify(manifest.GetProperty("inputs"), "ptex-slot-mixed.slang",
                Path.Combine(source, "ptex-slot-mixed.slang"));
            Verify(manifest.GetProperty("inputs"), "material.surface.slang",
                Path.Combine(repository, "eng", "shaders", "sources", "material.surface.slang"));
        }
        foreach (string name in new[] { "ptex-slot-lookup.slang", "ptex-slot-probe.slang", "compile_probe.py" })
        {
            Verify(manifest.GetProperty("inputs"), name, Path.Combine(source, name));
        }
        Verify(manifest.GetProperty("inputs"), "toolchain.lock.json",
            Path.Combine(repository, "eng", "shaders", "toolchain.lock.json"));
        foreach (string stage in new[] { "vertex", "fragment" })
        {
            foreach (string target in new[] { "dxil", "spirv", "metal" })
            {
                string prefix = $"{stem}.{stage}";
                string suffix = target == "spirv" ? "spv" : target;
                byte[] code = Verify(manifest.GetProperty("artifacts"), $"{prefix}.{suffix}",
                    Path.Combine(output, $"{prefix}.{suffix}"));
                string reflectionName = $"{prefix}.{target}.reflection.json";
                byte[] reflection = Verify(manifest.GetProperty("artifacts"), reflectionName,
                    Path.Combine(output, reflectionName));
                using JsonDocument reflected = JsonDocument.Parse(reflection);
                JsonElement[] resources = reflected.RootElement.GetProperty("parameters")
                    .EnumerateArray().Where(parameter =>
                        parameter.GetProperty("name").GetString() == "ptexSlotLookup").ToArray();
                uint expected = target == "dxil" ? 31u : target == "spirv" ? 49u : 2u;
                if (resources.Length != 1 ||
                    ResourceIndex(resources[0]) != expected)
                {
                    throw new InvalidDataException("The probe lookup reflection does not match the backend map.");
                }
                if (sampling && stage == "fragment")
                {
                    foreach (var (name, vkIndex, register) in new (string, uint, uint)[]
                    {
                        ("ptexBaseColorTexture", 2, 0), ("ptexBaseColorSampler", 1, 0),
                        ("ptexRoughnessTexture", 4, 2), ("ptexRoughnessSampler", 11, 2),
                    })
                    {
                        JsonElement[] matches = reflected.RootElement.GetProperty("parameters")
                            .EnumerateArray().Where(parameter => parameter.GetProperty("name").GetString() == name)
                            .ToArray();
                        if (matches.Length != 1 || ResourceIndex(matches[0]) !=
                            (target == "spirv" ? vkIndex : register))
                        {
                            throw new InvalidDataException($"The sampling probe resource does not match: {name}.");
                        }
                    }
                }
                if (target == "metal" &&
                    !Encoding.UTF8.GetString(code).Contains("[[buffer(2)]]", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The probe Metal code does not declare buffer(2).");
                }
            }
        }
        string extension = format switch
        {
            SilkShaderBinaryFormat.Dxil => "dxil",
            SilkShaderBinaryFormat.SpirV => "spv",
            SilkShaderBinaryFormat.MetalLibrary => "metal",
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
        SilkShaderModuleDescriptor module(string stage, SilkShaderStage shaderStage) => new(
            shaderStage, format,
            format == SilkShaderBinaryFormat.MetalLibrary ? "main" :
                mixed && stage == "fragment" ? "ptexSlotMixedFragmentMain" :
                sampling && stage == "fragment" ? "ptexSlotSamplingFragmentMain" :
                $"ptexSlotProbe{(stage == "vertex" ? "Vertex" : "Fragment")}Main",
            Verify(manifest.GetProperty("artifacts"), $"{stem}.{stage}.{extension}",
                Path.Combine(output, $"{stem}.{stage}.{extension}")));
        return (module("vertex", SilkShaderStage.Vertex), module("fragment", SilkShaderStage.Fragment));
    }

    private static uint ResourceIndex(JsonElement parameter)
    {
        bool hasBinding = parameter.TryGetProperty("binding", out JsonElement binding);
        bool hasBindings = parameter.TryGetProperty("bindings", out JsonElement bindings);
        if (hasBinding == hasBindings)
        {
            throw new InvalidDataException("A probe resource has a missing or ambiguous binding.");
        }
        JsonElement[] candidates = hasBinding ? [binding] : bindings.EnumerateArray().ToArray();
        JsonElement[] active = candidates.Where(candidate =>
        {
            int count = candidate.TryGetProperty("count", out JsonElement value) ? value.GetInt32() : 1;
            if (count is not (0 or 1))
            {
                throw new InvalidDataException("A probe resource has an unexpected binding count.");
            }
            return count == 1;
        }).ToArray();
        if (active.Length != 1)
        {
            throw new InvalidDataException("A probe resource must have exactly one active binding.");
        }
        return active[0].GetProperty("index").GetUInt32();
    }

    private static byte[] Verify(JsonElement entries, string name, string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        string expected = entries.GetProperty(name).GetString() ??
            throw new InvalidDataException("Missing probe input hash.");
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The exact Ptex probe input changed: {name}.");
        }
        return bytes;
    }
}
