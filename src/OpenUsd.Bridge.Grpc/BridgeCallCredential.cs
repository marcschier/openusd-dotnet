// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Bridge.Grpc;

/// <summary>
/// One short-lived credential presented on a bridge call.
/// </summary>
/// <remarks>
/// The token is held only for the duration of the call that presents it. Nothing in this package
/// writes it to a log, a file, a diagnostic event, or an exception message: <see cref="ToString"/>
/// is deliberately redacted, and the value is never included in any status or event this package
/// produces.
/// </remarks>
public readonly struct BridgeCallCredential : IEquatable<BridgeCallCredential>
{
    /// <summary>The maximum length of a credential token.</summary>
    public const int MaxTokenLength = 4096;

    /// <summary>The maximum length of an authorization scheme.</summary>
    public const int MaxSchemeLength = 32;

    private readonly string? _token;

    /// <summary>Initializes an ephemeral credential.</summary>
    /// <param name="token">The opaque credential value.</param>
    /// <param name="expiresAtUtc">
    /// When the credential stops being valid. A credential that is already expired is refused
    /// before a call is made, so an expired token is never put on the wire.
    /// </param>
    /// <param name="scheme">The authorization scheme; the default is <c>Bearer</c>.</param>
    /// <remarks>
    /// The scheme and the token are validated here, before anything can put them in request
    /// metadata. A carriage return, a line feed, or any other control character in a header value
    /// is a header-injection primitive on some HTTP/1.1 intermediaries, and an unbounded token is a
    /// cheap way to push a request past a peer's header limits. Both are rejected at construction
    /// rather than sanitized silently, because a caller whose token was quietly rewritten cannot
    /// tell that its credential is not the one being presented.
    /// </remarks>
    public BridgeCallCredential(string token, DateTimeOffset expiresAtUtc, string scheme = "Bearer")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ValidateScheme(scheme);
        ValidateToken(token);
        _token = token;
        ExpiresAtUtc = expiresAtUtc;
        Scheme = scheme;
    }

    /// <summary>Gets the authorization scheme.</summary>
    public string Scheme { get; }

    /// <summary>Gets when the credential stops being valid.</summary>
    public DateTimeOffset ExpiresAtUtc { get; }

    /// <summary>Gets whether this value carries a credential.</summary>
    public bool HasToken => !string.IsNullOrEmpty(_token);

    /// <summary>Returns whether the credential is still valid at <paramref name="nowUtc"/>.</summary>
    public bool IsValidAt(DateTimeOffset nowUtc) => HasToken && ExpiresAtUtc > nowUtc;

    /// <summary>Returns the header value to present, or an empty string when no token is held.</summary>
    /// <remarks>
    /// This is the only member that materializes the credential. Callers must pass the result
    /// straight into request metadata and must not retain, log, or persist it.
    /// </remarks>
    public string ToHeaderValue() => HasToken ? $"{Scheme} {_token}" : string.Empty;

    /// <inheritdoc/>
    public bool Equals(BridgeCallCredential other) =>
        string.Equals(_token, other._token, StringComparison.Ordinal) &&
        string.Equals(Scheme, other.Scheme, StringComparison.Ordinal) &&
        ExpiresAtUtc == other.ExpiresAtUtc;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is BridgeCallCredential other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_token, Scheme, ExpiresAtUtc);

    /// <summary>Returns a redacted description that never contains the credential.</summary>
    public override string ToString() =>
        HasToken ? $"{Scheme} <redacted> (expires {ExpiresAtUtc:O})" : "<no credential>";

    /// <summary>
    /// Returns whether <paramref name="value"/> is safe to place in a header value: no control
    /// character, no carriage return, and no line feed.
    /// </summary>
    internal static bool IsHeaderSafe(string value)
    {
        foreach (char character in value)
        {
            if (char.IsControl(character) || character == '\u007F')
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateScheme(string scheme)
    {
        if (scheme.Length > MaxSchemeLength)
        {
            throw new ArgumentException(
                $"An authorization scheme cannot exceed {MaxSchemeLength} characters.",
                nameof(scheme));
        }

        // RFC 9110 restricts a scheme to a token. Restricting it here also removes the space that
        // would otherwise let a scheme smuggle a second header value past a naive parser.
        foreach (char character in scheme)
        {
            bool allowed = char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '.' or '_' or '~' or '+';
            if (!allowed)
            {
                throw new ArgumentException(
                    "An authorization scheme may contain only letters, digits, and the characters " +
                    "'-', '.', '_', '~', and '+'.",
                    nameof(scheme));
            }
        }
    }

    private static void ValidateToken(string token)
    {
        if (token.Length > MaxTokenLength)
        {
            throw new ArgumentException(
                $"A credential token cannot exceed {MaxTokenLength} characters.",
                nameof(token));
        }
        if (!IsHeaderSafe(token))
        {
            // The message deliberately does not echo the offending value: a rejected credential is
            // still a credential, and a diagnostic is a place it must never appear.
            throw new ArgumentException(
                "A credential token cannot contain a control character, a carriage return, or a " +
                "line feed.",
                nameof(token));
        }
        if (token.Contains(' ', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A credential token cannot contain a space; the scheme and the token are joined " +
                "by exactly one.",
                nameof(token));
        }
    }

    /// <summary>Returns whether two credentials are equal.</summary>
    public static bool operator ==(BridgeCallCredential left, BridgeCallCredential right) =>
        left.Equals(right);

    /// <summary>Returns whether two credentials are not equal.</summary>
    public static bool operator !=(BridgeCallCredential left, BridgeCallCredential right) =>
        !left.Equals(right);
}

/// <summary>
/// Supplies a short-lived credential for each bridge call.
/// </summary>
/// <remarks>
/// The abstraction exists so a host can source a credential from wherever it already keeps one -- an
/// operating-system secret store, an environment-supplied handshake token, or a local broker -- and
/// so this package never has to own credential storage. A credential is requested per attempt, never
/// cached across a reconnect, and never written anywhere by this package.
/// </remarks>
public interface IBridgeCallCredentialProvider
{
    /// <summary>Returns the credential to present on the next call.</summary>
    ValueTask<BridgeCallCredential> GetCredentialAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A credential provider that returns one ephemeral token held in memory for the life of the
/// process, for a host that receives a session token out of band.
/// </summary>
/// <remarks>
/// This type deliberately offers no way to load a token from a file or an environment variable.
/// Reading and rotating a secret is the host's decision, and hiding it behind a convenience helper
/// would make the credential's lifetime invisible at the call site.
/// </remarks>
public sealed class EphemeralBearerTokenProvider : IBridgeCallCredentialProvider
{
    private readonly BridgeCallCredential _credential;

    /// <summary>Initializes a provider over one ephemeral token.</summary>
    public EphemeralBearerTokenProvider(string token, DateTimeOffset expiresAtUtc) =>
        _credential = new BridgeCallCredential(token, expiresAtUtc);

    /// <inheritdoc/>
    public ValueTask<BridgeCallCredential> GetCredentialAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_credential);
    }
}
