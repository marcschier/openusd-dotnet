// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Extraction;

/// <summary>
/// Reports that a physics extraction page could not be produced or could not be trusted.
/// </summary>
public sealed class UsdPhysicsExtractionException : InvalidOperationException
{
    /// <summary>Initializes a new instance with a default message.</summary>
    public UsdPhysicsExtractionException()
        : base("The physics extraction failed.")
    {
    }

    /// <summary>Initializes a new instance with the supplied message.</summary>
    /// <param name="message">The message that describes the failure.</param>
    public UsdPhysicsExtractionException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the supplied message and cause.</summary>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="innerException">The cause of the failure.</param>
    public UsdPhysicsExtractionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
