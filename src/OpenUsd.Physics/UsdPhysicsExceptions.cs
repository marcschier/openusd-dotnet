// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics;

/// <summary>
/// Reports a <see cref="UsdPhysicsSession.Step"/> call made without the exclusive
/// <see cref="UsdPhysicsStepOwnership"/> for that session, or made concurrently with another step.
/// </summary>
public sealed class UsdPhysicsStepOwnershipException : InvalidOperationException
{
    /// <summary>Identifies step-ownership contract violations.</summary>
    public const string ErrorCode = "OPENUSD_PHYSICS_STEP_OWNERSHIP_VIOLATION";

    /// <summary>Provides the stable step-ownership violation message.</summary>
    public const string ErrorMessage =
        "UsdPhysicsSession.Step requires the exclusive UsdPhysicsStepOwnership acquired for this " +
        "session and cannot be called concurrently.";

    internal UsdPhysicsStepOwnershipException()
        : base(ErrorMessage)
    {
    }

    /// <summary>
    /// Initializes the exception with a message describing a specific ownership-contract violation,
    /// such as a lifecycle operation rejected because a <see cref="UsdPhysicsStepOwnership"/> is
    /// still active, or a <see cref="UsdPhysicsSession.Step"/> call made from the wrong thread.
    /// </summary>
    internal UsdPhysicsStepOwnershipException(string message)
        : base(message)
    {
    }

    /// <summary>Gets the stable step-ownership error code.</summary>
    public string Code { get; } = ErrorCode;
}

/// <summary>
/// Reports a <see cref="UsdPhysicsSession"/> operation that is invalid for the session's current
/// <see cref="UsdPhysicsSessionState"/>.
/// </summary>
public sealed class UsdPhysicsSessionStateException : InvalidOperationException
{
    /// <summary>Identifies session lifecycle-state contract violations.</summary>
    public const string ErrorCode = "OPENUSD_PHYSICS_SESSION_INVALID_STATE";

    internal UsdPhysicsSessionStateException(UsdPhysicsSessionState state)
        : base($"The UsdPhysicsSession operation is not valid while the session state is '{state}'.")
    {
        State = state;
    }

    /// <summary>Gets the stable session lifecycle-state error code.</summary>
    public string Code { get; } = ErrorCode;

    /// <summary>Gets the session state observed when the operation was rejected.</summary>
    public UsdPhysicsSessionState State { get; }
}
