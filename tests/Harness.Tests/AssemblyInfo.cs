using Xunit;

// Harness tests exercise shared static state in the recorder, patch hooks, and SMAPI-facing
// adapters. Running them in parallel can interleave recorder arm/disarm/reset calls.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
