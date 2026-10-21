// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

public sealed partial class SilkMeshRenderer
{
    private FrameTargets? _frameTargets;

    internal void ReleaseCaptureFrameScratch()
    {
        if (!Monitor.IsEntered(_gate))
        {
            throw new InvalidOperationException("Capture scratch release requires the renderer capture lease.");
        }
        FrameTargets? previous = _frameTargets;
        _frameTargets = null;
        previous?.Dispose();
    }

    private sealed class FrameTargets : IDisposable
    {
        internal FrameTargets(ISilkGraphicsDevice device, SilkTextureDescriptor color, SilkTextureDescriptor depth)
        {
            ColorDescriptor = color;
            DepthDescriptor = depth;
            Generation = SilkDeviceGeneration.Read(device);
            Color = device.CreateTexture2D(color);
            try
            {
                Depth = device.CreateTexture2D(depth);
            }
            catch (Exception failure)
            {
                try
                {
                    Color.Dispose();
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException("Frame target allocation and cleanup failed.", failure, cleanup);
                }
                throw;
            }
        }

        internal FrameTargets(ISilkGraphicsDevice device, ISilkGraphicsTexture color, ISilkGraphicsTexture depth)
        {
            ColorDescriptor = DescribeDisplayTarget(color);
            DepthDescriptor = DescribeDisplayTarget(depth);
            Generation = SilkDeviceGeneration.Read(device);
            Color = color;
            Depth = depth;
        }

        internal SilkTextureDescriptor ColorDescriptor { get; }
        internal SilkTextureDescriptor DepthDescriptor { get; }
        internal ulong Generation { get; }
        internal ISilkGraphicsTexture Color { get; }
        internal ISilkGraphicsTexture Depth { get; }

        public void Dispose()
        {
            List<Exception>? errors = null;
            DisposeDifferentSelection(Color, null, ref errors);
            DisposeDifferentSelection(Depth, null, ref errors);
            if (errors is not null)
            {
                throw new AggregateException("Frame output targets could not all be released.", errors);
            }
        }
    }

    private sealed class PreparedFrameOutput : IDisposable
    {
        private readonly SilkMeshRenderer _owner;
        private readonly FrameTargets? _previous;
        private readonly FrameTargets _targets;
        private readonly ISilkGraphicsCommandList? _publish;
        private readonly bool _privateCapture;
        private bool _committed;
        private bool _disposed;

        internal PreparedFrameOutput(
            SilkMeshRenderer owner, ISilkGraphicsTexture color, ISilkGraphicsTexture depth,
            bool privateCapture = false)
        {
            _owner = owner;
            _privateCapture = privateCapture;
            if (privateCapture)
            {
                // The capture facade owns unpublished targets and publishes only detached CPU results.
                _targets = new FrameTargets(owner._device, color, depth);
                return;
            }
            _publish = owner._device.CreateCommandList();
            FrameTargets? candidate = null;
            try
            {
                if (_publish is not ISilkTextureCopyGraphicsCommandList copy)
                {
                    throw new NotSupportedException("Admitted rendering requires exact GPU render-target copies.");
                }
                _previous = owner._frameTargets;
                SilkTextureDescriptor colorDescriptor = DescribeDisplayTarget(color);
                SilkTextureDescriptor depthDescriptor = DescribeDisplayTarget(depth);
                _targets = _previous is not null && _previous.ColorDescriptor == colorDescriptor &&
                    _previous.DepthDescriptor == depthDescriptor &&
                    _previous.Generation == owner.ReadDeviceGeneration()
                    ? _previous
                    : candidate = new FrameTargets(owner._device, colorDescriptor, depthDescriptor);
                copy.CopyRenderTarget(_targets.Color, color);
                copy.CopyRenderTarget(_targets.Depth, depth);
            }
            catch (Exception failure)
            {
                List<Exception>? errors = null;
                DisposeDifferentSelection(_publish, null, ref errors);
                DisposeDifferentSelection(candidate, null, ref errors);
                if (errors is not null)
                {
                    errors.Insert(0, failure);
                    throw new AggregateException("Frame output preparation and cleanup failed.", errors);
                }
                throw;
            }
        }

        internal ISilkGraphicsTexture Color => _targets.Color;
        internal ISilkGraphicsTexture Depth => _targets.Depth;
        internal bool IsPublished => _committed;

        internal ISilkGraphicsSubmission? Publish()
        {
            if (_privateCapture)
            {
                _committed = true;
                return null;
            }
            ISilkGraphicsCommandList commands = _publish ??
                throw new InvalidOperationException("Frame publication has no command list.");
            ISilkGraphicsSubmission submission = _owner._device.Submit(commands);
            try
            {
                submission.Wait();
            }
            catch (Exception failure)
            {
                try
                {
                    submission.Dispose();
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException("Frame publication and cleanup failed.", failure, cleanup);
                }
                throw;
            }
            _owner._frameTargets = _targets;
            _committed = true;
            return submission;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (_privateCapture)
            {
                return;
            }
            List<Exception>? errors = null;
            DisposeDifferentSelection(_publish, null, ref errors);
            if (!ReferenceEquals(_previous, _targets))
            {
                DisposeDifferentSelection(_committed ? _previous : _targets, null, ref errors);
            }
            if (errors is not null)
            {
                throw new AggregateException("Frame output ownership could not all be released.", errors);
            }
        }
    }
}
