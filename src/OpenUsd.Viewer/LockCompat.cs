// Copyright (c) marcschier. Licensed under the MIT License.

#if !NET9_0_OR_GREATER

namespace System.Threading;

/// <summary>
/// Stands in for the runtime's own lock type, which .NET 9 introduced, so the viewport can
/// also target .NET 8 without changing how it declares its gates.
/// </summary>
/// <remarks>
/// The compiler recognises a <c>lock</c> target by this exact type name and then requires
/// the scope pattern below, so a bare placeholder is not enough. Entering still goes
/// through <see cref="Monitor"/>, which is what a <c>lock</c> over a plain object already
/// compiles to elsewhere in this repository on .NET 8. This file compiles to nothing on
/// .NET 9 and later, so those targets keep the runtime type and its faster path and are
/// unaffected by multi-targeting.
/// </remarks>
internal sealed class Lock
{
    private readonly object _gate = new();

    /// <summary>Enters the lock and returns the scope that exits it on disposal.</summary>
    public Scope EnterScope()
    {
        Monitor.Enter(_gate);
        return new Scope(_gate);
    }

    /// <summary>Holds an entered <see cref="Lock"/> for the duration of a statement.</summary>
    public ref struct Scope
    {
        private object? _gate;

        internal Scope(object gate) => _gate = gate;

        /// <summary>Exits the lock once, tolerating repeated disposal.</summary>
        public void Dispose()
        {
            object? gate = _gate;
            if (gate is not null)
            {
                _gate = null;
                Monitor.Exit(gate);
            }
        }
    }
}

#endif
