// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class NativeTemporaryDirectoryScopeTests
{
    [Test]
    public async Task RestoresUnsetAndNestedNativeValues()
    {
        string? original = NativeTemporaryDirectoryScope.ReadCurrent();
        string first = Path.Combine(Path.GetTempPath(), "native-temp-\u03bc");
        string second = Path.Combine(first, "nested");
        string? unset;
        string? firstValue;
        string? secondValue;
        string? restoredFirst;
        string? restoredUnset;

        using (new NativeTemporaryDirectoryScope(null))
        {
            unset = NativeTemporaryDirectoryScope.ReadCurrent();
            using (new NativeTemporaryDirectoryScope(first))
            {
                firstValue = NativeTemporaryDirectoryScope.ReadCurrent();
                using (new NativeTemporaryDirectoryScope(second))
                {
                    secondValue = NativeTemporaryDirectoryScope.ReadCurrent();
                }
                restoredFirst = NativeTemporaryDirectoryScope.ReadCurrent();
            }
            restoredUnset = NativeTemporaryDirectoryScope.ReadCurrent();
        }

        await Assert.That(unset).IsNull();
        await Assert.That(firstValue).IsEqualTo(first);
        await Assert.That(secondValue).IsEqualTo(second);
        await Assert.That(restoredFirst).IsEqualTo(first);
        await Assert.That(restoredUnset).IsNull();
        await Assert.That(NativeTemporaryDirectoryScope.ReadCurrent()).IsEqualTo(original);
    }

    [Test]
    public async Task RestoresNativeValueWhenTheBodyThrows()
    {
        string? original = NativeTemporaryDirectoryScope.ReadCurrent();
        bool caught = false;
        try
        {
            using var scope = new NativeTemporaryDirectoryScope("native-temp-exception");
            throw new InvalidOperationException("scope control");
        }
        catch (InvalidOperationException exception) when (exception.Message == "scope control")
        {
            caught = true;
        }

        await Assert.That(caught).IsTrue();
        await Assert.That(NativeTemporaryDirectoryScope.ReadCurrent()).IsEqualTo(original);
    }

    [Test]
    public async Task RestoresEmptyNativeValueAfterNestedOverride()
    {
        string? original = NativeTemporaryDirectoryScope.ReadCurrent();
        string? empty;
        string? nested;
        string? restoredEmpty;
        using (new NativeTemporaryDirectoryScope(string.Empty))
        {
            empty = NativeTemporaryDirectoryScope.ReadCurrent();
            using (new NativeTemporaryDirectoryScope("native-temp-nested"))
            {
                nested = NativeTemporaryDirectoryScope.ReadCurrent();
            }
            restoredEmpty = NativeTemporaryDirectoryScope.ReadCurrent();
        }

        await Assert.That(empty).IsEqualTo(string.Empty);
        await Assert.That(nested).IsEqualTo("native-temp-nested");
        await Assert.That(restoredEmpty).IsEqualTo(string.Empty);
        await Assert.That(NativeTemporaryDirectoryScope.ReadCurrent()).IsEqualTo(original);
    }
}
