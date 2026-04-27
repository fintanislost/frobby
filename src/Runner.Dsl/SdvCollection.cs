using Xunit;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>
/// xUnit collection definition for DSL tests. Users reference this via
/// <c>[Collection("SDV")]</c>; the <see cref="SdvFixture"/> runs once per assembly.
/// </summary>
[CollectionDefinition("SDV")]
public class SdvCollection : ICollectionFixture<SdvFixture> { }
