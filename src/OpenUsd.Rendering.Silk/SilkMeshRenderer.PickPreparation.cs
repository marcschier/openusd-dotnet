// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

public sealed partial class SilkMeshRenderer
{
    private PreparedPicking? _pickPreparation;

    private sealed class PreparedPicking : IDisposable
    {
        private readonly SilkMeshRenderer _owner;
        private readonly ISilkGraphicsTexture? _color;
        private readonly ISilkGraphicsTexture? _depth;
        private readonly uint _width;
        private readonly uint _height;
        private readonly ulong _generation;
        private readonly Dictionary<(SilkPickPrimitiveTopology, uint, SilkPickDepthBias, bool),
            ISilkPickGraphicsPipeline> _pipelines;
        private SilkPickReadbackReservation? _submitted;
        private bool _committed;
        private bool _disposed;

        internal PreparedPicking(SilkMeshRenderer owner, SilkPickFrameBinding binding)
        {
            if (owner._pickPreparation is not null)
            {
                throw new InvalidOperationException("Picking is already being prepared for a frame.");
            }
            owner.EnsurePickDeviceGeneration(binding);
            _owner = owner;
            _color = owner._pickColorTarget;
            _depth = owner._pickDepthTarget;
            _width = owner._pickTargetWidth;
            _height = owner._pickTargetHeight;
            _generation = owner.ReadDeviceGeneration();
            _pipelines = new(owner._pickPipelines);
            owner._pickPreparation = this;
        }

        internal void Track(SilkPickReadbackReservation reservation) => _submitted = reservation;
        internal void Commit() => _committed = true;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _owner._pickPreparation = null;
            List<Exception>? errors = null;
            if (!_committed && _submitted is { } reservation)
            {
                PendingPick? pending = _owner._inFlightPicks?[reservation.SlotIndex];
                try
                {
                    _owner._pickReadbacks!.DiscardSubmitted(reservation);
                    _owner._inFlightPicks![reservation.SlotIndex] = null;
                    _owner.ReleaseInFlightPickBuffers(reservation.SlotIndex);
                }
                catch (Exception error)
                {
                    (errors ??= []).Add(error);
                }
                pending?.Fail(new InvalidOperationException("The pick's frame was not published."));
            }
            if (_committed || _owner.ReadDeviceGeneration() != _generation)
            {
                DisposeDifferentSelection(_color, _owner._pickColorTarget, ref errors);
                DisposeDifferentSelection(_depth, _owner._pickDepthTarget, ref errors);
            }
            else
            {
                DisposeDifferentSelection(_owner._pickColorTarget, _color, ref errors);
                DisposeDifferentSelection(_owner._pickDepthTarget, _depth, ref errors);
                foreach (var pair in _owner._pickPipelines)
                {
                    _pipelines.TryGetValue(pair.Key, out ISilkPickGraphicsPipeline? previous);
                    DisposeDifferentSelection(pair.Value, previous, ref errors);
                }
                _owner._pickPipelines.Clear();
                foreach (var pair in _pipelines)
                {
                    _owner._pickPipelines.Add(pair.Key, pair.Value);
                }
                _owner._pickColorTarget = _color;
                _owner._pickDepthTarget = _depth;
                _owner._pickTargetWidth = _width;
                _owner._pickTargetHeight = _height;
            }
            if (errors is not null)
            {
                throw new AggregateException("Unpublished pick resources could not all be released.", errors);
            }
        }
    }
}
