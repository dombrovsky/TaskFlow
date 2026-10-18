namespace TaskFlow.Benchmarks;

using BenchmarkDotNet.Attributes;
using System.Reflection;

internal static class WorkloadValidation
{
    public static async Task RunAsync()
    {
        var cases = 0;
        foreach (var type in typeof(Program).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(FlowBenchmark).IsAssignableFrom(type)))
        {
            var parameters = type.GetProperties()
                .Where(property => property.IsDefined(typeof(ParamsAttribute))).ToArray();
            foreach (var values in ParameterSets(parameters, 0))
            {
                foreach (var method in MethodsWith<BenchmarkAttribute>(type))
                {
                    var benchmark = (FlowBenchmark)Activator.CreateInstance(type)!;
                    for (var i = 0; i < parameters.Length; i++)
                        parameters[i].SetValue(benchmark, values[i]);
                    benchmark.Setup();
                    try
                    {
                        // Repeat to catch incomplete drains or stale gates between invocations.
                        for (var repeat = 0; repeat < 2; repeat++)
                        {
                            foreach (var setup in MethodsWith<IterationSetupAttribute>(type))
                                setup.Invoke(benchmark, null);
                            try
                            {
                                var result = method.Invoke(benchmark, null);
                                if (result is Task<int> single)
                                {
                                    if (await single.ConfigureAwait(false) != Workload.Result)
                                        throw new InvalidOperationException("Unexpected operation result.");
                                }
                                else if (result is Task<int[]> batch)
                                    Workload.Check(await batch.ConfigureAwait(false));
                                else if (result is not null)
                                    throw new InvalidOperationException("Unrecognized benchmark result; extend validation.");
                            }
                            finally
                            {
                                foreach (var cleanup in MethodsWith<IterationCleanupAttribute>(type))
                                    cleanup.Invoke(benchmark, null);
                            }
                        }
                    }
                    finally
                    {
                        await benchmark.Cleanup().ConfigureAwait(false);
                    }
                    cases++;
                    Console.WriteLine($"Validated {type.Name}.{method.Name}({string.Join(", ", values)})");
                }
            }
        }
        Console.WriteLine($"Validated {cases} benchmark cases, each invoked twice; every batch contained {Workload.BatchSize} results.");
    }

    private static IEnumerable<MethodInfo> MethodsWith<T>(Type type) where T : Attribute
    {
        return type.GetMethods().Where(method => method.IsDefined(typeof(T), inherit: true));
    }

    private static IEnumerable<object?[]> ParameterSets(PropertyInfo[] properties, int index)
    {
        if (index == properties.Length)
        {
            yield return [];
            yield break;
        }
        foreach (var value in properties[index].GetCustomAttribute<ParamsAttribute>()!.Values)
            foreach (var tail in ParameterSets(properties, index + 1))
                yield return new[] { value }.Concat(tail).ToArray();
    }
}