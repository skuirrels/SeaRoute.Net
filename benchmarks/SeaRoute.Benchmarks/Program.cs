using BenchmarkDotNet.Running;

namespace SeaRoute.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<RoutingBenchmarks>();
    }
}
