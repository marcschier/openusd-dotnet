// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

internal sealed class SilkShadowAtlasResource : IDisposable
{
    internal const int MaximumTransmissionLayers = 4;
    private readonly List<ISilkGraphicsTexture> _owned = new(7);

    internal SilkShadowAtlasResource(ISilkGraphicsDevice device, uint edge, bool transmission)
    {
        try
        {
            Depth = Own(device.CreateTexture2D(SilkTextureDescriptor.SampledDepthTarget(edge, edge)));
            if (transmission)
            {
                Layers = new ISilkGraphicsTexture[MaximumTransmissionLayers];
                for (int layer = 0; layer < Layers.Length; layer++)
                {
                    Layers[layer] = Own(device.CreateTexture2D(new SilkTextureDescriptor(
                        edge, edge, SilkTextureFormat.Rgba32Float,
                        SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.Sampled)));
                }
                PeelDepth = Own(device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(edge, edge)));
                Probe = Own(device.CreateTexture2D(new SilkTextureDescriptor(
                    edge, edge, SilkTextureFormat.Rgba8Unorm,
                    SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource)));
            }
        }
        catch (Exception failure)
        {
            try
            {
                Dispose();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("Shadow atlas allocation and cleanup failed.", failure, cleanup);
            }
            throw;
        }
    }

    internal ISilkGraphicsTexture Depth { get; }
    internal ISilkGraphicsTexture[] Layers { get; } = [];
    internal ISilkGraphicsTexture? PeelDepth { get; }
    internal ISilkGraphicsTexture? Probe { get; }
    internal bool HasTransmission => Layers.Length != 0;
    internal uint Width => Depth.Width;
    internal uint Height => Depth.Height;
    internal ulong PayloadBytes => SilkShadowTransmissionWorkspace.AtlasBytes(Width, HasTransmission);

    private ISilkGraphicsTexture Own(ISilkGraphicsTexture texture)
    {
        _owned.Add(texture);
        return texture;
    }

    public void Dispose()
    {
        List<Exception>? failures = null;
        foreach (ISilkGraphicsTexture texture in _owned)
        {
            try
            {
                texture.Dispose();
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }
        _owned.Clear();
        if (failures is not null)
        {
            throw new AggregateException("Shadow atlas resources could not all be released.", failures);
        }
    }
}
