// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd;

/// <summary>
/// Reports an ownership operation that is invalid for a borrowed scheduler stage facade.
/// </summary>
public sealed class UsdStageOwnershipException : InvalidOperationException
{
    /// <summary>Identifies borrowed-stage ownership violations.</summary>
    public const string ErrorCode = "OPENUSD_BORROWED_STAGE_OWNERSHIP";

    /// <summary>Provides the stable borrowed-stage ownership message.</summary>
    public const string ErrorMessage =
        "A borrowed UsdStage cannot be disposed because its scheduler owns the native stage.";

    internal UsdStageOwnershipException()
        : base(ErrorMessage)
    {
    }

    /// <summary>Gets the stable ownership error code.</summary>
    public string Code { get; } = ErrorCode;
}

/// <summary>
/// Reports a scheduler operation invoked recursively from its stage-owner thread.
/// </summary>
public sealed class UsdStageSchedulerReentrancyException : InvalidOperationException
{
    /// <summary>Identifies scheduler owner-thread reentrancy.</summary>
    public const string ErrorCode = "OPENUSD_STAGE_SCHEDULER_REENTRANCY";

    /// <summary>Provides the stable scheduler reentrancy message.</summary>
    public const string ErrorMessage =
        "UsdStageScheduler operations cannot be invoked from a scheduler callback.";

    internal UsdStageSchedulerReentrancyException()
        : base(ErrorMessage)
    {
    }

    /// <summary>Gets the stable reentrancy error code.</summary>
    public string Code { get; } = ErrorCode;
}
