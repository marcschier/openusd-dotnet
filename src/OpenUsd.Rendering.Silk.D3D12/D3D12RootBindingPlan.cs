// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk.D3D12;

/// <summary>Maps renderer-neutral slots onto a bounded D3D12 root signature.</summary>
internal sealed class D3D12RootBindingPlan
{
    private const uint SceneParametersRootParameter = 0;
    private readonly IReadOnlyList<SilkBindingSlot> _slots;
    private readonly uint _sampledTextureRootParameter;
    private readonly uint _samplerRootParameter;

    internal D3D12RootBindingPlan(SilkBindingLayoutDescriptor layout)
    {
        _slots = layout.MaterialSlots ?? [];
        uint rootParameter = 1;
        foreach (SilkBindingSlot slot in _slots)
        {
            switch (slot.Kind)
            {
                case SilkBindingKind.UniformBuffer:
                case SilkBindingKind.StorageBuffer:
                    rootParameter++;
                    break;
                case SilkBindingKind.SampledTexture:
                    SampledTextureCount++;
                    break;
                case SilkBindingKind.Sampler:
                    SamplerCount++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(layout),
                        slot.Kind,
                        "The D3D12 binding layout contains an unsupported slot kind.");
            }
        }

        _sampledTextureRootParameter =
            SampledTextureCount == 0 ? uint.MaxValue : rootParameter++;
        _samplerRootParameter =
            SamplerCount == 0 ? uint.MaxValue : rootParameter++;
        RootParameterCount = rootParameter;
        RootSignatureDwordCost =
            2 + (2 * BufferCount) +
            (SampledTextureCount == 0 ? 0u : 1u) +
            (SamplerCount == 0 ? 0u : 1u);
    }

    internal uint RootParameterCount { get; }

    internal uint RootSignatureDwordCost { get; }

    internal uint SampledTextureCount { get; }

    internal uint SamplerCount { get; }

    internal uint BufferCount =>
        RootParameterCount - 1 -
        (SampledTextureCount == 0 ? 0u : 1u) -
        (SamplerCount == 0 ? 0u : 1u);

    internal uint SampledTextureRootParameter => RequireTable(
        _sampledTextureRootParameter,
        SilkBindingKind.SampledTexture);

    internal uint SamplerRootParameter => RequireTable(
        _samplerRootParameter,
        SilkBindingKind.Sampler);

    internal IEnumerable<SilkBindingSlot> BufferSlots =>
        _slots.Where(static slot =>
            slot.Kind is SilkBindingKind.UniformBuffer or SilkBindingKind.StorageBuffer);

    internal IEnumerable<SilkBindingSlot> SampledTextureSlots =>
        _slots.Where(static slot => slot.Kind == SilkBindingKind.SampledTexture);

    internal IEnumerable<SilkBindingSlot> SamplerSlots =>
        _slots.Where(static slot => slot.Kind == SilkBindingKind.Sampler);

    internal uint GetRootParameter(uint set, uint binding, SilkBindingKind kind)
    {
        if (kind == SilkBindingKind.SampledTexture)
        {
            _ = RequireSlot(set, binding, kind);
            return SampledTextureRootParameter;
        }
        if (kind == SilkBindingKind.Sampler)
        {
            _ = RequireSlot(set, binding, kind);
            return SamplerRootParameter;
        }

        uint rootParameter = SceneParametersRootParameter + 1;
        foreach (SilkBindingSlot slot in _slots)
        {
            if (slot.Kind is not (
                SilkBindingKind.UniformBuffer or SilkBindingKind.StorageBuffer))
            {
                continue;
            }
            if (slot.Set == set && slot.Binding == binding && slot.Kind == kind)
            {
                return rootParameter;
            }
            rootParameter++;
        }
        throw MissingSlot(set, binding, kind);
    }

    internal uint GetDescriptorOffset(uint set, uint binding, SilkBindingKind kind)
    {
        if (kind is not (SilkBindingKind.SampledTexture or SilkBindingKind.Sampler))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Only descriptor-table slots have a descriptor offset.");
        }

        uint offset = 0;
        foreach (SilkBindingSlot slot in _slots)
        {
            if (slot.Kind != kind)
            {
                continue;
            }
            if (slot.Set == set && slot.Binding == binding)
            {
                return offset;
            }
            offset++;
        }
        throw MissingSlot(set, binding, kind);
    }

    private SilkBindingSlot RequireSlot(uint set, uint binding, SilkBindingKind kind)
    {
        foreach (SilkBindingSlot slot in _slots)
        {
            if (slot.Set == set && slot.Binding == binding && slot.Kind == kind)
            {
                return slot;
            }
        }
        throw MissingSlot(set, binding, kind);
    }

    private static uint RequireTable(uint rootParameter, SilkBindingKind kind) =>
        rootParameter != uint.MaxValue
            ? rootParameter
            : throw new InvalidOperationException(
                $"The D3D12 root signature has no {kind} descriptor table.");

    private static InvalidOperationException MissingSlot(
        uint set,
        uint binding,
        SilkBindingKind kind) =>
        new($"The D3D12 root signature does not declare set {set}, binding {binding}, kind {kind}.");
}
