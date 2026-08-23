// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Tests.Baking;

/// <summary>
/// Blocks the runtime from saving a destination layer, using whichever mechanism the current
/// operating system actually honours.
/// </summary>
/// <remarks>
/// <para>
/// OpenUSD does not overwrite a layer in place. It writes a sibling temporary file next to the
/// destination and renames it over the target, so a block that only marks the destination file
/// read-only is defeated on Unix: the rename succeeds as long as the containing directory is
/// writable. The two supported platforms therefore need different mechanisms:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// Windows: an exclusive handle on the destination opened with <see cref="FileShare.None"/>.
/// Because the handle grants no delete sharing, both opening the file for writing and renaming
/// another file over it fail while it is held.
/// </description>
/// </item>
/// <item>
/// <description>
/// Unix: the write permission is removed from the containing directory as well as from the
/// destination file, which blocks creating the sibling temporary file and renaming it into place.
/// </description>
/// </item>
/// </list>
/// <para>
/// Neither mechanism is unconditional, so the blocker probes itself and reports
/// <see cref="IsEffective"/>. A process with enough privilege to ignore directory permissions —
/// most commonly a container running as root — cannot be blocked this way, and a test should skip
/// with <see cref="Explanation"/> rather than assert a failure that the platform will not produce.
/// </para>
/// <para>
/// The block must be applied at the save boundary, not before the bake starts, because preflight
/// legitimately refuses a destination it cannot write.
/// </para>
/// </remarks>
internal sealed class BakeSaveBlocker : IDisposable
{
    private const string ProbeName = ".openusd-save-probe";

    private readonly string _directory;
    private readonly string _path;
    private readonly FileStream? _exclusive;
    private readonly UnixFileMode _directoryMode;
    private readonly UnixFileMode _fileMode;
    private bool _restored;

    private BakeSaveBlocker(
        string directory,
        string path,
        FileStream? exclusive,
        UnixFileMode directoryMode,
        UnixFileMode fileMode,
        bool isEffective,
        string explanation)
    {
        _directory = directory;
        _path = path;
        _exclusive = exclusive;
        _directoryMode = directoryMode;
        _fileMode = fileMode;
        IsEffective = isEffective;
        Explanation = explanation;
    }

    /// <summary>Gets a value indicating whether the destination is provably unwritable.</summary>
    public bool IsEffective { get; }

    /// <summary>Gets the reason the block is ineffective, or an empty string when it holds.</summary>
    public string Explanation { get; }

    /// <summary>Applies the platform-appropriate block and probes that it holds.</summary>
    public static BakeSaveBlocker Create(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(destinationPath);
        string directory = Path.GetDirectoryName(destinationPath)
            ?? throw new ArgumentException(
                "The destination must live in a directory.", nameof(destinationPath));

        if (OperatingSystem.IsWindows())
        {
            var exclusive = new FileStream(
                destinationPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            (bool effective, string explanation) = ProbeWindows(destinationPath);
            return new BakeSaveBlocker(
                directory, destinationPath, exclusive, default, default, effective, explanation);
        }

        var directoryInfo = new DirectoryInfo(directory);
        var fileInfo = new FileInfo(destinationPath);
        UnixFileMode directoryMode = directoryInfo.UnixFileMode;
        UnixFileMode fileMode = fileInfo.UnixFileMode;
        const UnixFileMode writable =
            UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;
        try
        {
            fileInfo.UnixFileMode = fileMode & ~writable;
            directoryInfo.UnixFileMode = directoryMode & ~writable;
        }
        catch
        {
            // A half-applied block would leave the work directory unusable for the rest of the run.
            new DirectoryInfo(directory).UnixFileMode = directoryMode;
            new FileInfo(destinationPath).UnixFileMode = fileMode;
            throw;
        }

        (bool unixEffective, string unixExplanation) = ProbeUnix(directory);
        return new BakeSaveBlocker(
            directory,
            destinationPath,
            null,
            directoryMode,
            fileMode,
            unixEffective,
            unixExplanation);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_restored)
        {
            return;
        }
        _restored = true;

        _exclusive?.Dispose();
        if (!OperatingSystem.IsWindows())
        {
            // The directory is restored first so the file mode can always be written back.
            new DirectoryInfo(_directory).UnixFileMode = _directoryMode;
            new FileInfo(_path).UnixFileMode = _fileMode;
        }
    }

    private static (bool Effective, string Explanation) ProbeWindows(string path)
    {
        try
        {
            using var probe = new FileStream(
                path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        }
        catch (IOException)
        {
            return (true, string.Empty);
        }
        catch (UnauthorizedAccessException)
        {
            return (true, string.Empty);
        }

        return (
            false,
            "The destination layer is still writable while an exclusive handle is held, so the " +
            "runtime's save cannot be made to fail on this system.");
    }

    private static (bool Effective, string Explanation) ProbeUnix(string directory)
    {
        string probePath = Path.Combine(directory, ProbeName);
        try
        {
            using (File.Create(probePath))
            {
            }
        }
        catch (UnauthorizedAccessException)
        {
            return (true, string.Empty);
        }
        catch (IOException)
        {
            return (true, string.Empty);
        }

        File.Delete(probePath);
        return (
            false,
            "The destination directory is still writable after its write permission was removed, " +
            "so this process ignores directory permissions and the runtime's save cannot be made " +
            "to fail.");
    }
}
