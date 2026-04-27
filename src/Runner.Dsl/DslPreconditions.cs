using System;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Common precondition helpers shared by DSL facets.</summary>
internal static class DslPreconditions
{
    /// <summary>Exception message when DSL methods are called before session initialization.</summary>
    internal static InvalidOperationException NoSession() =>
        new("SdvTestSession.Current is not initialized. Ensure your test class has [Collection(\"SDV\")] and the assembly declares [CollectionDefinition(\"SDV\")] with SdvFixture.");
}
