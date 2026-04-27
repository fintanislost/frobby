using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SdvTestFramework.Runner.Reports;

public static class ReportRunId
{
    public static string ForExplicitReportBase(IReadOnlyList<string> paths, string? filter)
    {
        if (!string.IsNullOrWhiteSpace(filter))
            return "filter-" + Sanitize(filter);

        var stems = paths
            .Where(p => p.EndsWith(".test.json", StringComparison.OrdinalIgnoreCase))
            .Select(p => Path.GetFileName(p))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.EndsWith(".test.json", StringComparison.OrdinalIgnoreCase)
                ? p.Substring(0, p.Length - ".test.json".Length)
                : Path.GetFileNameWithoutExtension(p))
            .ToList();

        if (stems.Count == 1)
            return Sanitize(stems[0]);

        var numbers = stems
            .Select(s => Regex.Match(s, @"^(\d+)"))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .OrderBy(n => n)
            .ToList();

        if (numbers.Count == stems.Count && numbers.Count > 1)
            return $"{numbers.First():00}-{numbers.Last():00}";

        return "run";
    }

    private static string Sanitize(string value)
    {
        var chars = value.Trim().ToLowerInvariant().Select(c =>
            char.IsLetterOrDigit(c) ? c : '-');
        var collapsed = Regex.Replace(new string(chars.ToArray()), "-+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(collapsed) ? "run" : collapsed;
    }
}
