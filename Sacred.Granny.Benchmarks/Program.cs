using BenchmarkDotNet.Running;
using Sacred.Granny.Benchmarks;

// `dotnet run -c Release -- verify` checks the optimised evaluator against the legacy copy instead of benchmarking.
if (args.Length > 0 && args[0] == "verify") return EquivalenceCheck.Run();
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return 0;
