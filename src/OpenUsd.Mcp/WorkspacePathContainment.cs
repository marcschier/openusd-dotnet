// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp;

/// <summary>Reports that a workspace path is unavailable or violates containment.</summary>
public sealed class WorkspacePathContainmentException : IOException
{
    /// <summary>Initializes a path-containment error.</summary>
    public WorkspacePathContainmentException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a path-containment error with its filesystem cause.</summary>
    public WorkspacePathContainmentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Canonicalizes workspace paths and rejects traversal and reparse-point escapes.</summary>
public sealed class WorkspacePathContainment
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Initializes containment for configured source and output roots.</summary>
    public WorkspacePathContainment(string sourceRoot, string outputRoot)
    {
        SourceRoot = CanonicalizeDirectory(sourceRoot, nameof(sourceRoot));
        EnsureExistingDirectory(SourceRoot);
        OutputRoot = CanonicalizeDirectory(outputRoot, nameof(outputRoot));
        CreateDirectorySafely(OutputRoot);
    }

    /// <summary>Gets the canonical read-only source root.</summary>
    public string SourceRoot { get; }

    /// <summary>Gets the canonical writable output root.</summary>
    public string OutputRoot { get; }

    /// <summary>Resolves an existing source file without allowing indirection outside the root.</summary>
    public string ResolveSourceFile(string relativePath)
    {
        string candidate = ResolveContainedPath(SourceRoot, relativePath, nameof(relativePath));
        try
        {
            if (!File.Exists(candidate))
            {
                throw new WorkspacePathContainmentException(
                    $"The source stage does not exist: '{candidate}'.");
            }

            RejectReparsePoints(SourceRoot, candidate);
            return candidate;
        }
        catch (WorkspacePathContainmentException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw Denied(candidate, exception);
        }
    }

    /// <summary>Creates and resolves a contained output directory.</summary>
    public string CreateOutputDirectory(string relativePath)
    {
        string candidate = ResolveContainedPath(OutputRoot, relativePath, nameof(relativePath));
        return CreateContainedDirectory(OutputRoot, candidate);
    }

    /// <summary>Resolves a relative path beneath an existing canonical root.</summary>
    public static string ResolveContainedPath(string root, string relativePath) =>
        ResolveContainedPath(root, relativePath, nameof(relativePath));

    /// <summary>
    /// Creates a directory after validating every existing ancestor and revalidates each segment
    /// before creating the next one.
    /// </summary>
    public static string CreateDirectorySafely(string directoryPath)
    {
        string candidate = CanonicalizeDirectory(directoryPath, nameof(directoryPath));
        try
        {
            DirectoryInfo? existing = FindExistingAncestor(candidate);
            if (existing is null)
            {
                throw new WorkspacePathContainmentException(
                    $"No existing ancestor was found for '{candidate}'.");
            }

            RejectExistingPathReparsePoints(existing.FullName);
            CreateMissingSegments(existing.FullName, candidate);
            RejectExistingPathReparsePoints(candidate);
            return candidate;
        }
        catch (WorkspacePathContainmentException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw Denied(candidate, exception);
        }
    }

    /// <summary>Safely creates a directory lexically and physically contained by a root.</summary>
    public static string CreateContainedDirectory(string root, string directoryPath)
    {
        string canonicalRoot = CanonicalizeDirectory(root, nameof(root));
        string candidate = CanonicalizeDirectory(directoryPath, nameof(directoryPath));
        EnsureContained(canonicalRoot, candidate, nameof(directoryPath), allowRoot: true);
        EnsureExistingDirectory(canonicalRoot);
        try
        {
            RejectReparsePoints(canonicalRoot, candidate);
            CreateMissingSegments(canonicalRoot, candidate);
            RejectReparsePoints(canonicalRoot, candidate);
            return candidate;
        }
        catch (WorkspacePathContainmentException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw Denied(candidate, exception);
        }
    }

    internal static void RejectReparsePoints(string root, string candidate)
    {
        string canonicalRoot = CanonicalizeDirectory(root, nameof(root));
        string canonicalCandidate = Path.GetFullPath(candidate);
        EnsureContained(canonicalRoot, canonicalCandidate, nameof(candidate), allowRoot: true);
        RejectReparsePoint(canonicalRoot);
        string relative = Path.GetRelativePath(canonicalRoot, canonicalCandidate);
        if (relative == ".")
        {
            return;
        }

        string current = canonicalRoot;
        foreach (string segment in Split(relative))
        {
            current = Path.Combine(current, segment);
            if (File.Exists(current) || Directory.Exists(current))
            {
                RejectReparsePoint(current);
            }
        }
    }

    private static string ResolveContainedPath(
        string root,
        string relativePath,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath, parameterName);
        if (Path.IsPathRooted(relativePath))
        {
            throw new WorkspacePathContainmentException(
                "Workspace paths must be relative.");
        }

        try
        {
            string canonicalRoot = CanonicalizeDirectory(root, nameof(root));
            string candidate = Path.GetFullPath(Path.Combine(canonicalRoot, relativePath));
            EnsureContained(canonicalRoot, candidate, parameterName, allowRoot: false);
            return candidate;
        }
        catch (WorkspacePathContainmentException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            IOException or
            NotSupportedException or
            UnauthorizedAccessException)
        {
            throw Denied(relativePath, exception);
        }
    }

    private static void EnsureContained(
        string root,
        string candidate,
        string parameterName,
        bool allowRoot)
    {
        if (allowRoot && string.Equals(root, candidate, PathComparison))
        {
            return;
        }

        string prefix = Path.EndsInDirectorySeparator(root)
            ? root
            : string.Concat(root, Path.DirectorySeparatorChar);
        if (!candidate.StartsWith(prefix, PathComparison))
        {
            throw new WorkspacePathContainmentException(
                $"The path supplied for '{parameterName}' escapes its configured workspace root.");
        }
    }

    private static void EnsureExistingDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                throw new WorkspacePathContainmentException(
                    $"The configured directory does not exist: '{path}'.");
            }

            RejectExistingPathReparsePoints(path);
        }
        catch (WorkspacePathContainmentException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw Denied(path, exception);
        }
    }

    private static void CreateMissingSegments(string existingRoot, string candidate)
    {
        string relative = Path.GetRelativePath(existingRoot, candidate);
        if (relative == ".")
        {
            return;
        }

        string current = existingRoot;
        foreach (string segment in Split(relative))
        {
            RejectReparsePoint(current);
            string next = Path.Combine(current, segment);
            if (File.Exists(next) && !Directory.Exists(next))
            {
                throw new WorkspacePathContainmentException(
                    $"A file blocks workspace directory creation: '{next}'.");
            }

            if (!Directory.Exists(next))
            {
                Directory.CreateDirectory(next);
            }

            RejectReparsePoint(next);
            current = next;
        }
    }

    private static DirectoryInfo? FindExistingAncestor(string candidate)
    {
        DirectoryInfo? current = new(candidate);
        while (current is not null && !current.Exists)
        {
            current = current.Parent;
        }

        return current;
    }

    private static void RejectExistingPathReparsePoints(string path)
    {
        string? pathRoot = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(pathRoot))
        {
            throw new WorkspacePathContainmentException(
                $"The workspace path has no filesystem root: '{path}'.");
        }

        string canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(pathRoot));
        if (canonicalRoot.Length == 0)
        {
            canonicalRoot = pathRoot;
        }

        RejectReparsePoint(canonicalRoot);
        string relative = Path.GetRelativePath(canonicalRoot, path);
        string current = canonicalRoot;
        foreach (string segment in Split(relative))
        {
            current = Path.Combine(current, segment);
            RejectReparsePoint(current);
        }
    }

    private static string CanonicalizeDirectory(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            IOException or
            NotSupportedException or
            UnauthorizedAccessException)
        {
            throw Denied(path, exception);
        }
    }

    private static string[] Split(string relativePath) =>
        relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

    private static void RejectReparsePoint(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new WorkspacePathContainmentException(
                    $"Reparse points are not allowed in workspace paths: '{path}'.");
            }
        }
        catch (WorkspacePathContainmentException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw Denied(path, exception);
        }
    }

    private static WorkspacePathContainmentException Denied(
        string path,
        Exception exception) =>
        new($"The workspace path is unavailable or denied: '{path}'.", exception);
}
