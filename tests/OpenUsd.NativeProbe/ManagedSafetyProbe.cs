// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.NativeProbe;

internal static class ManagedSafetyProbe
{
    internal static bool TryRun(string[] args, out int exitCode)
    {
        if (args is not ["--managed-safety"])
        {
            exitCode = 0;
            return false;
        }

        Run();
        Console.WriteLine("Managed safety probes passed.");
        exitCode = 0;
        return true;
    }

    internal static void Run()
    {
        var scalarValue = new OpenUsdNativeScalarValue
        {
            KindValue = int.MaxValue,
        };
        var scalarResult = new OpenUsdNativeScalarResult(scalarValue, textValue: null);
        bool scalarKindRejected = false;
        try
        {
            _ = UsdScalarValue.FromNative(scalarResult);
        }
        catch (InvalidOperationException exception)
            when (exception.Message == "The native scalar kind is not supported.")
        {
            scalarKindRejected = true;
        }
        if (!scalarKindRejected)
        {
            throw new InvalidOperationException(
                "The NativeAOT probe accepted an invalid scalar kind.");
        }

        bool invalidUtf8Rejected = false;
        try
        {
            _ = NativePackedStringListDecoder.Decode(
                [0xc3, 0],
                [(nuint)0],
                "NativeAOT packed UTF-8 probe");
        }
        catch (OpenUsdNativeException exception)
            when (exception.Status == OpenUsdNativeStatus.NativeError)
        {
            invalidUtf8Rejected = true;
        }
        if (!invalidUtf8Rejected)
        {
            throw new InvalidOperationException(
                "The NativeAOT probe accepted invalid packed UTF-8.");
        }
    }
}
