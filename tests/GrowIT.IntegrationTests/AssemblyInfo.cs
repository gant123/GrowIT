// The shared GrowItApiFactory mutates process-global state to satisfy boot-time config validation:
// it sets environment variables (Jwt__Key, Email__RequireProviderInProduction, ...) that Program.cs
// reads before builder.Build(), i.e. before the factory's in-memory config would apply. Because
// those writes are process-wide, running test classes in parallel lets one class's environment
// leak into another class's app boot. Run this assembly's tests sequentially so that global state
// is deterministic. (Modest cost: these are full-host integration tests, already heavy to parallelize.)
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
