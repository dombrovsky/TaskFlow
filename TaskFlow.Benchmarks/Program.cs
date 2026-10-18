namespace TaskFlow.Benchmarks;

using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--validate" }))
        {
            await WorkloadValidation.RunAsync().ConfigureAwait(false);
            return 0;
        }

        // Supply defaults through the CLI so explicit job/count options retain precedence.
        if (!args.Any(arg => arg is "--job" or "-j" || arg.StartsWith("--job=", StringComparison.Ordinal)))
        {
            var options = args.ToList();
            AddDefault(options, "--warmupCount", "3");
            AddDefault(options, "--iterationCount", "8");
            AddDefault(options, "--launchCount", "1");
            args = options.ToArray();
        }
        var config = DefaultConfig.Instance
            .AddColumn(StatisticColumn.OperationsPerSecond)
            .AddExporter(JsonExporter.Full);
        var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config).ToArray();
        var informationOnly = args.Any(arg => arg is "--list" or "--info" or "--help" or "-h");
        return (summaries.Length == 0 && !informationOnly) || summaries.Any(summary => summary.HasCriticalValidationErrors
            || summary.Reports.Any(report => !report.Success)) ? 1 : 0;
    }

    private static void AddDefault(List<string> options, string name, string value)
    {
        if (!options.Any(option => option == name || option.StartsWith(name + "=", StringComparison.Ordinal)))
            options.AddRange([name, value]);
    }
}