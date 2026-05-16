using System;
using System.Collections.Generic;
using System.IO;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Fixtures;

public static class ScenarioFixtureVariantStager
{
    public static int StageAll(
        string repoRoot,
        string sdvSavesDir,
        IReadOnlyList<(string Path, ScenarioSpec Spec)> scenarios,
        TextWriter error)
    {
        var fixturesRoot = Path.Combine(repoRoot, "tests", "fixtures");
        Directory.CreateDirectory(sdvSavesDir);

        if (!Directory.Exists(fixturesRoot))
            return 0;

        var seen = new HashSet<(string Fixture, string StagedName)>();
        foreach (var (_, spec) in scenarios)
        {
            if (string.IsNullOrEmpty(spec.Fixture))
                continue;

            var stagedName = ScenarioFixtureStageName.For(spec.Fixture, spec.SaveOverrides);
            var pair = (spec.Fixture, stagedName);
            if (!seen.Add(pair))
                continue;

            var src = Path.Combine(fixturesRoot, spec.Fixture, "save");
            if (!Directory.Exists(src))
                continue;

            try
            {
                FixtureStager.Stage(spec.Fixture, fixturesRoot, sdvSavesDir, spec.SaveOverrides, stagedName);
            }
            catch (Exception ex)
            {
                error.WriteLine($"[stage-error] fixture '{spec.Fixture}': {ex.Message}");
                return 2;
            }
        }

        return 0;
    }

    public static void ApplyEffectiveFixtureNames(List<(string Path, ScenarioSpec Spec)> scenarios)
    {
        foreach (var (_, spec) in scenarios)
        {
            if (string.IsNullOrEmpty(spec.Fixture))
                continue;

            spec.Fixture = ScenarioFixtureStageName.For(spec.Fixture, spec.SaveOverrides);
        }
    }
}
