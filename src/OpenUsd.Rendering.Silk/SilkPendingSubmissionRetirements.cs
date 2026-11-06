// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.ExceptionServices;

namespace OpenUsd.Rendering.Silk;

internal abstract class SilkPendingSubmissionRetirement
{
    internal SilkPendingSubmissionRetirement? Next;
    internal bool IsPending;

    internal abstract void RetireAfterDeviceIdle();
}

internal sealed class SilkPendingSubmissionRetirements
{
    private readonly object _gate = new();
    private SilkPendingSubmissionRetirement? _head;

    internal void Retain(SilkPendingSubmissionRetirement submission)
    {
        lock (_gate)
        {
            if (submission.IsPending)
            {
                return;
            }
            // A failed native wait may have reported out-of-memory.
            submission.Next = _head;
            submission.IsPending = true;
            _head = submission;
        }
    }

    internal void Remove(SilkPendingSubmissionRetirement submission)
    {
        lock (_gate)
        {
            SilkPendingSubmissionRetirement? previous = null;
            for (SilkPendingSubmissionRetirement? current = _head;
                current is not null;
                current = current.Next)
            {
                if (!ReferenceEquals(current, submission))
                {
                    previous = current;
                    continue;
                }
                if (previous is null)
                {
                    _head = current.Next;
                }
                else
                {
                    previous.Next = current.Next;
                }
                current.Next = null;
                current.IsPending = false;
                return;
            }
        }
    }

    internal SilkPendingSubmissionRetirement[] Snapshot()
    {
        lock (_gate)
        {
            if (_head is null)
            {
                return [];
            }
            var pending = new List<SilkPendingSubmissionRetirement>();
            for (SilkPendingSubmissionRetirement? current = _head;
                current is not null;
                current = current.Next)
            {
                pending.Add(current);
            }
            return [.. pending];
        }
    }

    internal static void Retire(SilkPendingSubmissionRetirement[] pending)
    {
        List<Exception>? failures = null;
        foreach (SilkPendingSubmissionRetirement submission in pending)
        {
            try
            {
                submission.RetireAfterDeviceIdle();
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
        }
        if (failures is [Exception single])
        {
            ExceptionDispatchInfo.Capture(single).Throw();
        }
        if (failures is not null)
        {
            throw new AggregateException(
                "One or more submitted GPU workloads failed to retire.", failures);
        }
    }
}
