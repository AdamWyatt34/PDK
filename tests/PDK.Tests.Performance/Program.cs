using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using PDK.Tests.Performance.Benchmarks;
using PDK.Tests.Performance.Config;

namespace PDK.Tests.Performance;

public class Program
{
    public static void Main(string[] args)
    {
        var config = new PdkBenchmarkConfig();

        // Check for specific benchmark filter
        if (args.Contains("--parsing"))
        {
            BenchmarkRunner.Run<ParsingBenchmarks>(config);
        }
        else if (args.Contains("--execution"))
        {
            BenchmarkRunner.Run<ExecutionModeBenchmarks>(config);
        }
        else if (args.Contains("--optimization"))
        {
            BenchmarkRunner.Run<OptimizationBenchmarks>(config);
        }
        else if (args.Contains("--realworld"))
        {
            BenchmarkRunner.Run<RealWorldBenchmarks>(config);
        }
        else if (args.Contains("--all"))
        {
            // Run all benchmarks
            var switcher = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly);
            switcher.Run(args, config);
        }
        else
        {
            // Interactive mode - let user choose
            var switcher = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly);
            switcher.Run(args, config);
        }
    }
}
