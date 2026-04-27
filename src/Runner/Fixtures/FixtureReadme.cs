using System.Text;

namespace SdvTestFramework.Runner.Fixtures;

/// <summary>Generates a short human-readable README for a fixture directory.</summary>
public static class FixtureReadme
{
    public static string Generate(FixtureSpec spec, FixtureMetadata meta)
    {
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(spec.Name).AppendLine();
        sb.AppendLine(spec.Description).AppendLine();

        sb.AppendLine("## Environment").AppendLine();
        sb.Append("- SDV ").AppendLine(meta.SdvVersion);
        sb.Append("- SMAPI ").AppendLine(meta.SmapiVersion);
        sb.Append("- Farmer: ").Append(meta.Farmer.Name).Append(" (").Append(meta.Farmer.Gender).AppendLine(")");
        sb.Append("- Created: ").AppendLine(meta.CreatedAt);
        sb.AppendLine();

        if (!string.IsNullOrEmpty(spec.Base))
        {
            sb.AppendLine("## Derived from").AppendLine();
            sb.Append("Built from: `").Append(spec.Base).AppendLine("`.").AppendLine();
        }

        if (meta.ModsInstalled.Length > 0)
        {
            sb.AppendLine("## Mods installed during capture").AppendLine();
            foreach (var m in meta.ModsInstalled)
                sb.Append("- ").AppendLine(m);
            sb.AppendLine();
        }

        sb.AppendLine("## Regenerate").AppendLine();
        sb.AppendLine("```bash");
        sb.Append("sdv-test fixture create ").Append(spec.Name).Append(" --from ").AppendLine(meta.RegenerateWith);
        sb.AppendLine("```").AppendLine();

        sb.AppendLine("_This file is auto-generated. Safe to delete; re-runs of `fixture create` regenerate it._");
        return sb.ToString();
    }
}
