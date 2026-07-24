// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;

namespace OpenUsd;

/// <summary>
/// Classifies the renderer work required after a stage edit.
/// </summary>
public enum UsdStageInvalidationKind
{
    /// <summary>Only authored property values require refresh.</summary>
    Property,

    /// <summary>Scene topology or prim membership requires refresh.</summary>
    Topology,

    /// <summary>Composition results require refresh.</summary>
    Composition,

    /// <summary>All derived scene and renderer state requires refresh.</summary>
    Full
}

/// <summary>
/// Describes one or more ordered stage edits using native change serials.
/// </summary>
public readonly record struct UsdStageChange : IUsdDetachedResult
{
    /// <summary>Initializes a stage change notification.</summary>
    /// <param name="beforeChangeSerial">The native change serial before the first edit.</param>
    /// <param name="afterChangeSerial">The native change serial after the last edit.</param>
    /// <param name="invalidation">The strongest required invalidation.</param>
    /// <param name="editCount">The number of coalesced edits.</param>
    public UsdStageChange(
        ulong beforeChangeSerial,
        ulong afterChangeSerial,
        UsdStageInvalidationKind invalidation,
        int editCount = 1)
    {
        if (afterChangeSerial <= beforeChangeSerial)
        {
            throw new ArgumentOutOfRangeException(
                nameof(afterChangeSerial),
                "The serial after an edit must be greater than the serial before it.");
        }
        if ((uint)invalidation > (uint)UsdStageInvalidationKind.Full)
        {
            throw new ArgumentOutOfRangeException(nameof(invalidation));
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(editCount);

        BeforeChangeSerial = beforeChangeSerial;
        AfterChangeSerial = afterChangeSerial;
        Invalidation = invalidation;
        EditCount = editCount;
    }

    /// <summary>Gets the native change serial before the first edit.</summary>
    public ulong BeforeChangeSerial { get; }

    /// <summary>Gets the native change serial after the last edit.</summary>
    public ulong AfterChangeSerial { get; }

    /// <summary>Gets the strongest required invalidation.</summary>
    public UsdStageInvalidationKind Invalidation { get; }

    /// <summary>Gets the number of coalesced edits.</summary>
    public int EditCount { get; }

    /// <summary>Coalesces a subsequent ordered change into this notification.</summary>
    /// <param name="subsequent">The subsequent change.</param>
    /// <returns>A notification spanning both changes.</returns>
    public UsdStageChange Coalesce(UsdStageChange subsequent)
    {
        if (subsequent.BeforeChangeSerial < AfterChangeSerial)
        {
            throw new ArgumentException(
                "The subsequent change overlaps or precedes this change.",
                nameof(subsequent));
        }

        UsdStageInvalidationKind invalidation =
            subsequent.BeforeChangeSerial == AfterChangeSerial
                ? Strongest(Invalidation, subsequent.Invalidation)
                : UsdStageInvalidationKind.Full;
        return new UsdStageChange(
            BeforeChangeSerial,
            subsequent.AfterChangeSerial,
            invalidation,
            checked(EditCount + subsequent.EditCount));
    }

    private static UsdStageInvalidationKind Strongest(
        UsdStageInvalidationKind left,
        UsdStageInvalidationKind right) =>
        left >= right ? left : right;
}

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
internal sealed class UsdStageChangeFeed
{
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly LinkedList<UsdStageChange> _pending = [];
    private readonly Channel<byte> _signals;
    private Exception? _failure;
    private bool _completed;
    private int _readerState;

    internal UsdStageChangeFeed(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _signals = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
            AllowSynchronousContinuations = false
        });
    }

    internal void Publish(UsdStageChange change)
    {
        bool release = false;
        lock (_gate)
        {
            if (_completed)
            {
                return;
            }

            if (_pending.Count < _capacity)
            {
                _pending.AddLast(change);
                release = true;
            }
            else
            {
                LinkedListNode<UsdStageChange> tail = _pending.Last!;
                tail.Value = tail.Value.Coalesce(change);
            }
        }

        if (release)
        {
            _signals.Writer.TryWrite(0);
        }
    }

    internal void Complete(Exception? failure = null)
    {
        lock (_gate)
        {
            if (_completed)
            {
                return;
            }

            _failure = failure;
            _completed = true;
        }
        _signals.Writer.TryWrite(0);
    }

    internal async IAsyncEnumerable<UsdStageChange> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _readerState, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "Only one active stage change reader is supported.");
        }

        try
        {
            while (true)
            {
                UsdStageChange? change = null;
                Exception? failure = null;
                bool completed = false;
                lock (_gate)
                {
                    if (_pending.First is { } first)
                    {
                        change = first.Value;
                        _pending.RemoveFirst();
                    }
                    else if (_completed)
                    {
                        failure = _failure;
                        completed = true;
                    }
                }

                if (change is { } value)
                {
                    yield return value;
                }
                else if (completed)
                {
                    if (failure is not null)
                    {
                        ExceptionDispatchInfo.Capture(failure).Throw();
                    }
                    yield break;
                }
                else
                {
                    _ = await _signals.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            Volatile.Write(ref _readerState, 0);
        }
    }
}
