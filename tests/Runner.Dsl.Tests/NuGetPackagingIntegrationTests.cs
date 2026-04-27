using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests;

/// <summary>Integration surface for NuGet packaging — verified manually via the local-install smoke (Task 4 step 4).</summary>
public class NuGetPackagingIntegrationTests
{
    [Fact(Skip = "Requires manual local-install smoke — run scripts/pack.sh + dotnet tool install --add-source ./nupkg.")]
    public void NuGetPackages_InstallAndResolve() { }
}
