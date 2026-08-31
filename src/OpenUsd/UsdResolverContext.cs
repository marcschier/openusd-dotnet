// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>
/// Pairs a URI/IRI scheme with the resolver-defined context string that configures it.
/// </summary>
/// <param name="UriScheme">
/// The lower-case URI/IRI scheme of the resolver to configure, or an empty string for the
/// primary resolver.
/// </param>
/// <param name="ContextString">The resolver-defined context string.</param>
public readonly record struct UsdResolverContextString(string UriScheme, string ContextString);

/// <summary>
/// Owns an OpenUSD <c>ArResolverContext</c> that configures asset resolution.
/// </summary>
/// <remarks>
/// A context is an immutable value that may be shared between threads. Binding one is not: a
/// binding is thread local and must be disposed on the thread that created it, in reverse
/// creation order, and must never be held across an <see langword="await"/>. Prefer
/// <see cref="UsdStage.Open(string, UsdResolverContext)"/> or
/// <see cref="UsdResolver.Resolve"/> over an explicit binding whenever the context only has to
/// apply to one stage or one batch, because both bind and unbind internally around the whole
/// operation.
/// </remarks>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public sealed class UsdResolverContext : IDisposable
{
    private OpenUsdNativeResolverContext? _native;

    internal UsdResolverContext(OpenUsdNativeResolverContext native)
    {
        _native = native;
    }

    /// <summary>Gets the resolver-defined debug description of the context.</summary>
    public string DebugString => OpenUsdNativeRuntime.GetResolverContextDebugString(Native);

    /// <summary>Gets a value indicating whether the context carries no resolver state.</summary>
    public bool IsEmpty => OpenUsdNativeRuntime.IsResolverContextEmpty(Native);

    internal OpenUsdNativeResolverContext Native =>
        _native ?? throw new ObjectDisposedException(nameof(UsdResolverContext));

    /// <summary>Creates the default context of the primary resolver.</summary>
    public static UsdResolverContext CreateDefault() =>
        new(OpenUsdNativeRuntime.CreateResolverContext([]));

    /// <summary>Creates a context from resolver-defined context strings.</summary>
    /// <param name="contextStrings">
    /// The scheme and context-string pairs to combine into one context. An entry whose scheme has
    /// no registered resolver is ignored by OpenUSD.
    /// </param>
    public static UsdResolverContext Create(IReadOnlyList<UsdResolverContextString> contextStrings)
    {
        ArgumentNullException.ThrowIfNull(contextStrings);
        var packed = new string[contextStrings.Count * 2];
        for (int i = 0; i < contextStrings.Count; i++)
        {
            UsdResolverContextString entry = contextStrings[i];
            ArgumentNullException.ThrowIfNull(entry.UriScheme, nameof(contextStrings));
            ArgumentNullException.ThrowIfNull(entry.ContextString, nameof(contextStrings));
            packed[i * 2] = entry.UriScheme;
            packed[(i * 2) + 1] = entry.ContextString;
        }
        return new UsdResolverContext(OpenUsdNativeRuntime.CreateResolverContext(packed));
    }

    /// <summary>Creates the default context the primary resolver associates with an asset.</summary>
    public static UsdResolverContext CreateForAsset(string assetPath) =>
        new(OpenUsdNativeRuntime.CreateResolverContextForAsset(assetPath));

    /// <summary>Binds the context on the calling thread until the binding is disposed.</summary>
    /// <remarks>
    /// The binding is thread local: dispose it on the calling thread, before any binding created
    /// after it, and never across an <see langword="await"/>.
    /// </remarks>
    public UsdResolverContextBinding Bind() =>
        new(OpenUsdNativeRuntime.BindResolverContext(Native));

    /// <summary>Refreshes any resolver caches associated with the context.</summary>
    /// <remarks>
    /// This is a process-wide operation, not a per-context one. It invalidates resolved paths for
    /// every thread and every open stage and sends OpenUSD's <c>ArNotice::ResolverChanged</c>,
    /// whose listeners mutate their own state while handling it. Upstream documents concurrent
    /// refreshes of the same context as unsafe, so call it from one thread at a quiescent point
    /// rather than from a worker while other threads resolve or compose.
    /// </remarks>
    /// <exception cref="OpenUsdNativeException">
    /// This same context has a live binding on some thread. The rejection is scoped to this
    /// context by value identity, so an unrelated bound context never causes it, and a context
    /// bound on another thread always does. Release the binding and retry.
    /// </exception>
    public void Refresh() => OpenUsdNativeRuntime.RefreshResolverContext(Native);

    /// <inheritdoc />
    public void Dispose()
    {
        _native?.Dispose();
        _native = null;
    }
}

/// <summary>
/// Keeps a resolver context bound on the thread that created the binding.
/// </summary>
/// <remarks>
/// Do not hold a binding across an <see langword="await"/>. The native binding is thread local,
/// so a continuation that resumes on a different thread would resolve without it and would fail
/// to release it. Bind, do the resolution, dispose, and only then await.
/// </remarks>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public sealed class UsdResolverContextBinding : IDisposable
{
    private readonly OpenUsdNativeResolverBinding _native;
    private bool _disposed;

    internal UsdResolverContextBinding(OpenUsdNativeResolverBinding native)
    {
        _native = native;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The binding is marked disposed only after the native release succeeds. Disposing from the
    /// wrong thread, or before a binding created after it, throws and leaves the binding usable,
    /// so the owner thread can still release it in the right order.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The calling thread did not create the binding, or a binding created after this one is
    /// still held. The binding remains bound and can be released in order.
    /// </exception>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _native.Dispose();
        _disposed = true;
    }
}
