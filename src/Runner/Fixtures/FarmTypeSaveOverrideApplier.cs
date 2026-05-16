using System;
using System.Xml;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Fixtures;

public static class FarmTypeSaveOverrideApplier
{
    public static void Apply(string saveFilePath, ScenarioFarmTypeSaveOverride? farmType)
    {
        if (farmType is null)
            return;

        if (!string.Equals(farmType.WhichFarm, "mod", StringComparison.Ordinal))
            throw new InvalidOperationException("Only farm_type.which_farm = \"mod\" save overrides are supported.");
        if (string.IsNullOrWhiteSpace(farmType.ModFarmId))
            throw new InvalidOperationException("farm_type.mod_farm_id is required for mod farm save overrides.");

        var document = new XmlDocument { PreserveWhitespace = true };
        document.Load(saveFilePath);

        var root = document.DocumentElement
            ?? throw new InvalidOperationException($"Save file '{saveFilePath}' is missing a root XML element.");
        var whichFarm = root["whichFarm"]
            ?? throw new InvalidOperationException($"Save file '{saveFilePath}' is missing required whichFarm element.");

        whichFarm.InnerText = farmType.ModFarmId;

        var settings = new XmlWriterSettings { OmitXmlDeclaration = document.FirstChild is not XmlDeclaration };
        using var writer = XmlWriter.Create(saveFilePath, settings);
        document.Save(writer);
    }
}
