using Xunit;

// Tests across the Runner.Dsl.Tests assembly share static state via SdvTestSession.Current.
// Disabling parallelization at the assembly level avoids races without reserving a named
// collection — T8's [CollectionDefinition("SDV")] (with SdvFixture) keeps the collection
// name available for worked examples + user scenarios.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
