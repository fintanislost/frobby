using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class StateLocationHandlerTests
{
    [Fact(Skip = "Requires live SDV (GameLocation.furniture population).")]
    public void Handle_LocationWithFurniture_IncludesFurnitureSummary() { /* integration */ }
}
