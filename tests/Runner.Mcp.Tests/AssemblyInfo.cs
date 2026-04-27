using Xunit;

// MCP tests share static state via SdvLifecycle (once it lands in T3). Disable parallelization
// at the assembly level — same approach used by Runner.Dsl.Tests.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
