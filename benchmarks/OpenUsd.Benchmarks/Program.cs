// Copyright (c) marcschier. Licensed under the MIT License.

using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace OpenUsd.Benchmarks;

internal static class Program
{
    public static int Main(string[] args)
    {
        Summary[] summaries =
            [.. BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args)];
        if (summaries.Length == 0)
        {
            Console.Error.WriteLine("BenchmarkDotNet did not execute any benchmark cases.");
            return 1;
        }
        foreach (Summary summary in summaries)
        {
            if (summary.HasCriticalValidationErrors ||
                summary.Reports.IsDefaultOrEmpty ||
                summary.Reports.Any(report => !report.Success))
            {
                Console.Error.WriteLine(
                    $"BenchmarkDotNet reported a failed or invalid run: {summary.Title}.");
                return 1;
            }
        }
        return 0;
    }
}
