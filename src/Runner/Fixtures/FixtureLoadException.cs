using System;

namespace SdvTestFramework.Runner.Fixtures;

/// <summary>Thrown by <see cref="FixtureLoader"/> when a fixture script can't be parsed or doesn't validate.</summary>
public sealed class FixtureLoadException : Exception
{
    public FixtureLoadException(string file, string message) : base($"{file}: {message}") { }
    public FixtureLoadException(string file, string message, Exception inner) : base($"{file}: {message}", inner) { }
}
