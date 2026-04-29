using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using SdvTestFramework.Protocol.Reports;

namespace SdvTestFramework.Runner.Reports;

/// <summary>
/// Pure-function generator for the HTML run report. Writes:
/// <list type="bullet">
///   <item><c>summary.json</c> — machine-readable equivalent.</item>
///   <item><c>index.html</c> — landing page with all scenarios.</item>
///   <item><c>scenarios/&lt;name&gt;/report.html</c> — per-scenario detail.</item>
///   <item><c>scenarios/&lt;name&gt;/steps.json</c> — per-scenario raw data.</item>
///   <item><c>assets/styles.css</c> — single embedded stylesheet.</item>
/// </list>
/// </summary>
public static class HtmlReportGenerator
{
    public static void Generate(RunDirectory runDir, RunSummary summary)
    {
        // 1. summary.json
        var jsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        var json = JsonSerializer.Serialize(summary, jsonOpts);
        File.WriteAllText(Path.Combine(runDir.Root, "summary.json"), json);

        // 2. assets/styles.css
        File.WriteAllText(Path.Combine(runDir.AssetsDir, "styles.css"), CssTemplate);

        // 3. index.html
        File.WriteAllText(Path.Combine(runDir.Root, "index.html"), RenderIndex(summary));

        // 4. per-scenario report.html + steps.json
        foreach (var s in summary.Scenarios)
        {
            var scenDir = runDir.ScenarioDir(s.Name);
            File.WriteAllText(Path.Combine(scenDir, "report.html"), RenderScenarioReport(s, scenDir));
            var scenJson = JsonSerializer.Serialize(s, jsonOpts);
            File.WriteAllText(Path.Combine(scenDir, "steps.json"), scenJson);
        }
    }

    public static void GenerateHub(string baseDir)
    {
        Directory.CreateDirectory(baseDir);
        File.WriteAllText(Path.Combine(baseDir, "index.html"), RenderHub(baseDir));
    }

    private static string RenderIndex(RunSummary s)
    {
        var passed = s.Scenarios.Count(x => x.Passed);
        var total = s.Scenarios.Count;
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append("<title>sdv-test run ").Append(WebUtility.HtmlEncode(s.RunId)).AppendLine("</title>");
        sb.AppendLine("<link rel=\"stylesheet\" href=\"assets/styles.css\">");
        sb.AppendLine("</head><body>");
        sb.AppendLine("<nav class=\"breadcrumbs\"><a href=\"../index.html\">All reports</a></nav>");
        sb.Append("<h1>Run ").Append(WebUtility.HtmlEncode(s.RunId)).AppendLine("</h1>");
        sb.Append("<p class=\"summary\">").Append(passed).Append(" passed").Append(" / ").Append(total).Append(" total");
        sb.Append(" · ").Append(s.DurationMs).Append("ms · ").Append(WebUtility.HtmlEncode(s.Started)).AppendLine("</p>");
        AppendRunMetadata(sb, s.Metadata);
        sb.AppendLine("<table class=\"scenarios\">");
        sb.AppendLine("<thead><tr><th>Scenario</th><th>Outcome</th><th>Duration</th><th>Steps/Asserts</th></tr></thead>");
        sb.AppendLine("<tbody>");
        foreach (var sc in s.Scenarios)
        {
            var cls = sc.Passed ? "pass" : "fail";
            var label = sc.Passed ? "PASS" : "FAIL";
            sb.Append("<tr class=\"").Append(cls).Append("\">");
            var safe = SanitizeName(sc.Name);
            sb.Append("<td><a href=\"scenarios/").Append(WebUtility.HtmlEncode(safe))
              .Append("/report.html\">").Append(WebUtility.HtmlEncode(sc.Name)).Append("</a></td>");
            sb.Append("<td class=\"").Append(cls).Append("\">").Append(label).Append("</td>");
            sb.Append("<td>").Append(sc.DurationMs).Append("ms</td>");
            sb.Append("<td>").Append(sc.Steps.Count).Append("st / ").Append(sc.Assertions.Count).Append("as</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody></table>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void AppendRunMetadata(StringBuilder sb, RunMetadata? metadata)
    {
        if (metadata is null)
            return;

        sb.AppendLine("<section class=\"metadata\">");
        sb.AppendLine("<h2>Run metadata</h2>");
        sb.AppendLine("<dl>");
        AppendDefinition(sb, "Launch mode", metadata.LaunchMode);
        AppendDefinition(sb, "Launcher", metadata.Launcher);
        AppendDefinition(sb, "Command", metadata.Command);
        AppendDefinition(sb, "Working directory", metadata.WorkingDirectory);
        sb.AppendLine("</dl>");

        if (metadata.Repositories.Count > 0)
        {
            sb.AppendLine("<table class=\"repositories\">");
            sb.AppendLine("<thead><tr><th>Source</th><th>Commit</th><th>State</th><th>Path</th></tr></thead>");
            sb.AppendLine("<tbody>");
            foreach (var repo in metadata.Repositories)
            {
                sb.AppendLine("<tr>");
                sb.Append("<td>").Append(WebUtility.HtmlEncode(repo.Label)).AppendLine("</td>");
                sb.Append("<td><code>").Append(WebUtility.HtmlEncode(repo.Commit ?? "(unknown)")).AppendLine("</code></td>");
                sb.Append("<td>").Append(repo.Dirty ? "dirty" : "clean").AppendLine("</td>");
                sb.Append("<td>").Append(WebUtility.HtmlEncode(repo.Path)).AppendLine("</td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        sb.AppendLine("</section>");
    }

    private static void AppendDefinition(StringBuilder sb, string term, string value)
    {
        sb.Append("<dt>").Append(WebUtility.HtmlEncode(term)).AppendLine("</dt>");
        sb.Append("<dd>").Append(WebUtility.HtmlEncode(value)).AppendLine("</dd>");
    }

    private static string RenderScenarioReport(ScenarioOutcome s, string scenarioDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append("<title>").Append(WebUtility.HtmlEncode(s.Name)).AppendLine("</title>");
        sb.AppendLine("<link rel=\"stylesheet\" href=\"../../assets/styles.css\">");
        sb.AppendLine("</head><body>");
        sb.Append("<h1>").Append(WebUtility.HtmlEncode(s.Name)).AppendLine("</h1>");
        sb.AppendLine("<nav class=\"breadcrumbs\"><a href=\"../../index.html\">back to run</a> · <a href=\"../../../index.html\">All reports</a></nav>");

        var cls = s.Passed ? "pass" : "fail";
        sb.Append("<p class=\"badge ").Append(cls).Append("\">")
          .Append(s.Passed ? "PASSED" : "FAILED").AppendLine("</p>");
        sb.Append("<p>Duration: ").Append(s.DurationMs).AppendLine("ms</p>");
        if (s.Path is not null)
        {
            sb.Append("<p>Path: ").Append(WebUtility.HtmlEncode(s.Path)).AppendLine("</p>");
        }

        sb.AppendLine("<h2>Steps</h2>");
        if (s.Steps.Count == 0)
        {
            sb.AppendLine("<p><em>(none)</em></p>");
        }
        else
        {
            sb.AppendLine("<ol class=\"steps\">");
            for (var i = 0; i < s.Steps.Count; i++)
            {
                var step = s.Steps[i];
                var stepCls = step.Passed ? "pass" : "fail";
                sb.Append("<li class=\"").Append(stepCls).Append("\">");
                sb.Append("<code>").Append(WebUtility.HtmlEncode(step.Action)).Append("</code>");
                sb.Append(" — ").Append(step.DurationMs).Append("ms");
                if (step.Detail is { } d)
                    sb.Append(" — ").Append(WebUtility.HtmlEncode(d));
                AppendStepScreenshots(sb, s.Screenshots, i, scenarioDir);
                sb.AppendLine("</li>");
            }
            sb.AppendLine("</ol>");
        }

        sb.AppendLine("<h2>Assertions</h2>");
        if (s.Assertions.Count == 0)
        {
            sb.AppendLine("<p><em>(none)</em></p>");
        }
        else
        {
            sb.AppendLine("<ul class=\"asserts\">");
            foreach (var a in s.Assertions)
            {
                var aCls = a.Passed ? "pass" : "fail";
                sb.Append("<li class=\"").Append(aCls).Append("\">");
                sb.Append("<strong>").Append(WebUtility.HtmlEncode(a.Type)).Append("</strong>");
                sb.Append(" — ").Append(a.Passed ? "PASS" : "FAIL");
                if (a.Detail is { } d)
                    sb.Append(" — ").Append(WebUtility.HtmlEncode(d));
                sb.AppendLine("</li>");
            }
            sb.AppendLine("</ul>");
        }

        if (s.Diffs.Count > 0)
        {
            sb.AppendLine("<section class=\"forensics\">");
            sb.AppendLine("<h2>Failure forensics</h2>");
            sb.AppendLine("<div class=\"diff-grid\">");
            foreach (var d in s.Diffs)
            {
                // Each DiffSet path encodes the assertion-id as its parent directory
                // (e.g. ".../diffs/assertion-03-bitmap/baseline.png"). We extract that
                // dir name and use it for both the H3 label and the page-relative URL,
                // so the URL is stable regardless of whether the path is absolute or
                // run-dir-relative.
                var assertId = Path.GetFileName(Path.GetDirectoryName(d.Baseline)) ?? "assertion-unknown";
                sb.AppendLine("<figure class=\"diff-set\">");
                sb.Append("<h3>").Append(WebUtility.HtmlEncode(assertId)).AppendLine("</h3>");
                sb.AppendLine("<div class=\"triptych\">");
                AppendDiffFigure(sb, $"diffs/{assertId}/baseline.png", "baseline", scenarioDir);
                AppendDiffFigure(sb, $"diffs/{assertId}/capture.png", "capture", scenarioDir);
                AppendDiffFigure(sb, $"diffs/{assertId}/diff.png", "diff", scenarioDir);
                sb.AppendLine("</div>");
                sb.AppendLine("</figure>");
            }
            sb.AppendLine("</div>");
            sb.AppendLine("</section>");
        }

        if (s.Screenshots.Count > 0)
        {
            sb.AppendLine("<h2>Screenshots</h2>");
            sb.AppendLine("<div class=\"screenshots\">");
            foreach (var ss in s.Screenshots)
            {
                var fileName = Path.GetFileName(ss);
                AppendImageFigure(sb, $"screenshots/{fileName}", fileName, scenarioDir);
            }
            sb.AppendLine("</div>");
        }

        if (s.Screenshots.Count > 0 || s.Diffs.Count > 0)
            AppendImageModal(sb);

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string RenderHub(string baseDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<title>Frobby Reports</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(CssTemplate);
        sb.AppendLine("</style>");
        sb.AppendLine("</head><body>");
        sb.AppendLine("<h1>Frobby Reports</h1>");
        sb.AppendLine("<table class=\"scenarios\">");
        sb.AppendLine("<thead><tr><th>Run</th><th>Preview</th><th>Started</th><th>Duration</th><th>Scenarios</th></tr></thead>");
        sb.AppendLine("<tbody>");

        foreach (var dir in Directory.EnumerateDirectories(baseDir).OrderBy(Path.GetFileName))
        {
            var indexPath = Path.Combine(dir, "index.html");
            if (!File.Exists(indexPath))
                continue;

            var runName = Path.GetFileName(dir);
            var started = string.Empty;
            var duration = string.Empty;
            var scenarios = string.Empty;
            var summaryPath = Path.Combine(dir, "summary.json");
            if (File.Exists(summaryPath))
            {
                try
                {
                    var summary = JsonSerializer.Deserialize<RunSummary>(
                        File.ReadAllText(summaryPath),
                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
                    if (summary is not null)
                    {
                        started = summary.Started;
                        duration = $"{summary.DurationMs}ms";
                        var passed = summary.Scenarios.Count(s => s.Passed);
                        scenarios = $"{passed}/{summary.Scenarios.Count} passed";
                    }
                }
                catch
                {
                    // A broken summary should not hide the run link.
                }
            }

            var runUrl = $"{runName}/index.html";
            sb.Append("<tr><td><a href=\"").Append(WebUtility.HtmlEncode(runUrl))
                .Append("\">").Append(WebUtility.HtmlEncode(runName)).Append("</a></td>");
            sb.Append("<td><button type=\"button\" class=\"preview-button\" data-report-src=\"")
                .Append(WebUtility.HtmlEncode(runUrl))
                .Append("\" data-report-title=\"")
                .Append(WebUtility.HtmlEncode(runName))
                .Append("\">Preview</button></td>");
            sb.Append("<td>").Append(WebUtility.HtmlEncode(started)).Append("</td>");
            sb.Append("<td>").Append(WebUtility.HtmlEncode(duration)).Append("</td>");
            sb.Append("<td>").Append(WebUtility.HtmlEncode(scenarios)).AppendLine("</td></tr>");
        }

        sb.AppendLine("</tbody></table>");
        sb.AppendLine("<dialog id=\"report-modal\" class=\"report-modal\">");
        sb.AppendLine("<form method=\"dialog\"><button class=\"modal-close\" aria-label=\"Close report preview\">Close</button></form>");
        sb.AppendLine("<h2 id=\"report-modal-title\">Report preview</h2>");
        sb.AppendLine("<iframe id=\"report-frame\" title=\"Report preview\"></iframe>");
        sb.AppendLine("</dialog>");
        sb.AppendLine("<script>");
        sb.AppendLine("const modal = document.getElementById('report-modal');");
        sb.AppendLine("const frame = document.getElementById('report-frame');");
        sb.AppendLine("const title = document.getElementById('report-modal-title');");
        sb.AppendLine("document.querySelectorAll('[data-report-src]').forEach(button => {");
        sb.AppendLine("  button.addEventListener('click', () => {");
        sb.AppendLine("    frame.src = button.dataset.reportSrc;");
        sb.AppendLine("    title.textContent = button.dataset.reportTitle || 'Report preview';");
        sb.AppendLine("    if (typeof modal.showModal === 'function') modal.showModal();");
        sb.AppendLine("    else window.location.href = button.dataset.reportSrc;");
        sb.AppendLine("  });");
        sb.AppendLine("});");
        sb.AppendLine("modal.addEventListener('close', () => { frame.removeAttribute('src'); });");
        sb.AppendLine("</script>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void AppendStepScreenshots(StringBuilder sb, IReadOnlyList<string> screenshots, int stepIndex, string scenarioDir)
    {
        var prefix = $"step-{stepIndex:D2}-";
        var matches = screenshots
            .Where(ss => Path.GetFileName(ss).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
            return;

        sb.AppendLine("<div class=\"step-screenshots\">");
        foreach (var ss in matches)
        {
            var fileName = Path.GetFileName(ss);
            AppendImageFigure(sb, $"screenshots/{fileName}", fileName, scenarioDir);
        }
        sb.AppendLine("</div>");
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }

    /// <summary>
    /// Emit a <c>&lt;figure&gt;</c> with a triptych image. <paramref name="urlRelativeToScenarioPage"/>
    /// is the URL written into the <c>src</c> attribute — the per-scenario report.html lives at
    /// <c>scenarios/&lt;name&gt;/report.html</c>, so the URL is sibling-relative
    /// (e.g. <c>diffs/assertion-03-bitmap/baseline.png</c>).
    /// </summary>
    private static void AppendDiffFigure(StringBuilder sb, string urlRelativeToScenarioPage, string caption, string scenarioDir)
    {
        AppendImageFigure(sb, urlRelativeToScenarioPage, caption, scenarioDir);
    }

    private static void AppendImageFigure(StringBuilder sb, string urlRelativeToScenarioPage, string caption, string scenarioDir)
    {
        var safeHref = WebUtility.HtmlEncode(urlRelativeToScenarioPage);
        var safeImageUrl = WebUtility.HtmlEncode(CacheBustedImageUrl(scenarioDir, urlRelativeToScenarioPage));
        var safeCaption = WebUtility.HtmlEncode(caption);
        sb.Append("<figure><a class=\"image-link\" href=\"").Append(safeHref)
          .Append("\" data-full-image-src=\"").Append(safeImageUrl)
          .Append("\" data-full-image-title=\"").Append(safeCaption)
          .Append("\"><img src=\"").Append(safeImageUrl)
          .Append("\" alt=\"").Append(safeCaption).Append("\"></a>");
        sb.Append("<figcaption>").Append(safeCaption).AppendLine("</figcaption></figure>");
    }

    private static string CacheBustedImageUrl(string scenarioDir, string urlRelativeToScenarioPage)
    {
        var queryStart = urlRelativeToScenarioPage.IndexOf('?');
        var pathOnly = queryStart >= 0 ? urlRelativeToScenarioPage[..queryStart] : urlRelativeToScenarioPage;
        var localPath = Path.Combine(
            scenarioDir,
            pathOnly.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(localPath))
            return urlRelativeToScenarioPage;

        var version = File.GetLastWriteTimeUtc(localPath).Ticks;
        var separator = queryStart >= 0 ? "&" : "?";
        return $"{urlRelativeToScenarioPage}{separator}v={version}";
    }

    private static void AppendImageModal(StringBuilder sb)
    {
        sb.AppendLine("<dialog id=\"image-modal\" class=\"image-modal\">");
        sb.AppendLine("<form method=\"dialog\"><button class=\"modal-close\" aria-label=\"Close image preview\">Close</button></form>");
        sb.AppendLine("<img id=\"image-modal-img\" alt=\"\">");
        sb.AppendLine("<p id=\"image-modal-caption\"></p>");
        sb.AppendLine("</dialog>");
        sb.AppendLine("<script>");
        sb.AppendLine("const imageModal = document.getElementById('image-modal');");
        sb.AppendLine("if (imageModal) {");
        sb.AppendLine("  const imageModalImg = document.getElementById('image-modal-img');");
        sb.AppendLine("  const imageModalCaption = document.getElementById('image-modal-caption');");
        sb.AppendLine("  document.querySelectorAll('[data-full-image-src]').forEach(link => {");
        sb.AppendLine("    link.addEventListener('click', event => {");
        sb.AppendLine("      if (typeof imageModal.showModal !== 'function') return;");
        sb.AppendLine("      event.preventDefault();");
        sb.AppendLine("      imageModalImg.src = link.dataset.fullImageSrc;");
        sb.AppendLine("      imageModalImg.alt = link.dataset.fullImageTitle || '';");
        sb.AppendLine("      imageModalCaption.textContent = link.dataset.fullImageTitle || link.dataset.fullImageSrc;");
        sb.AppendLine("      imageModal.showModal();");
        sb.AppendLine("    });");
        sb.AppendLine("  });");
        sb.AppendLine("  imageModal.addEventListener('click', event => { if (event.target === imageModal) imageModal.close(); });");
        sb.AppendLine("  imageModal.addEventListener('close', () => { imageModalImg.removeAttribute('src'); });");
        sb.AppendLine("}");
        sb.AppendLine("</script>");
    }

    private const string CssTemplate = """
        body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; max-width: 1200px; margin: 2em auto; padding: 0 1em; color: #222; }
        h1 { color: #111; border-bottom: 2px solid #ddd; padding-bottom: 0.3em; }
        h2 { color: #333; margin-top: 1.5em; }
        .summary { color: #555; font-size: 0.95em; }
        .badge { display: inline-block; padding: 0.3em 0.8em; border-radius: 4px; font-weight: bold; color: white; }
        .badge.pass { background: #2d6a3e; }
        .badge.fail { background: #b03030; }
        table.scenarios { border-collapse: collapse; width: 100%; margin: 1em 0; }
        table.scenarios th, table.scenarios td { padding: 0.6em 0.8em; text-align: left; border-bottom: 1px solid #eee; }
        table.scenarios tr.pass td.pass { color: #2d6a3e; font-weight: bold; }
        table.scenarios tr.fail td.fail { color: #b03030; font-weight: bold; }
        .metadata { border: 1px solid #ddd; border-radius: 6px; padding: 0.8em 1em; margin: 1em 0; background: #fafafa; }
        .metadata h2 { margin-top: 0; }
        .metadata dl { display: grid; grid-template-columns: max-content 1fr; gap: 0.35em 1em; margin: 0 0 1em; }
        .metadata dt { font-weight: 700; color: #444; }
        .metadata dd { margin: 0; overflow-wrap: anywhere; }
        table.repositories { border-collapse: collapse; width: 100%; }
        table.repositories th, table.repositories td { padding: 0.45em 0.6em; text-align: left; border-bottom: 1px solid #e6e6e6; vertical-align: top; }
        a { color: #1556b0; text-decoration: none; }
        a:hover { text-decoration: underline; }
        .breadcrumbs { margin: 0 0 1em; color: #666; }
        button.preview-button, button.modal-close { border: 1px solid #bbb; background: #f7f7f7; border-radius: 4px; padding: 0.35em 0.65em; font: inherit; cursor: pointer; }
        button.preview-button:hover, button.modal-close:hover { background: #eee; }
        .report-modal { width: min(1200px, 94vw); height: min(860px, 88vh); border: 1px solid #999; border-radius: 6px; padding: 1em; }
        .report-modal::backdrop { background: rgba(0, 0, 0, 0.35); }
        .report-modal form { text-align: right; margin: 0; }
        .report-modal h2 { margin-top: 0.4em; }
        .report-modal iframe { width: 100%; height: calc(100% - 5.5em); border: 1px solid #ddd; }
        ol.steps li, ul.asserts li { padding: 0.3em 0; }
        ol.steps li.fail, ul.asserts li.fail { color: #b03030; }
        ol.steps li.pass, ul.asserts li.pass { color: #2d6a3e; }
        code { background: #f5f5f5; padding: 0.1em 0.4em; border-radius: 3px; }
        .step-screenshots { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 0.7em; margin: 0.7em 0 0.4em; }
        .step-screenshots figure { margin: 0; }
        .step-screenshots img { max-width: 100%; border: 1px solid #ddd; }
        .step-screenshots figcaption { font-size: 0.8em; color: #666; margin-top: 0.25em; }
        .screenshots { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 1em; margin: 1em 0; }
        .screenshots figure { margin: 0; }
        .screenshots img { max-width: 100%; border: 1px solid #ddd; }
        .screenshots figcaption { font-size: 0.85em; color: #666; margin-top: 0.3em; }
        .image-link { display: block; cursor: zoom-in; }
        .image-modal { width: min(96vw, 1600px); max-width: none; height: min(94vh, 1000px); border: 1px solid #999; border-radius: 6px; padding: 1em; }
        .image-modal::backdrop { background: rgba(0, 0, 0, 0.72); }
        .image-modal form { text-align: right; margin: 0 0 0.75em; }
        .image-modal img { display: block; max-width: 100%; max-height: calc(100% - 4.5em); margin: 0 auto; object-fit: contain; }
        .image-modal p { margin: 0.75em 0 0; text-align: center; color: #444; overflow-wrap: anywhere; }
        section.forensics { background: #fff5f5; border-left: 4px solid #b03030; padding: 1em; margin: 1em 0; }
        section.forensics h2 { margin-top: 0; color: #b03030; }
        .diff-set h3 { font-family: monospace; font-size: 1em; margin: 0.5em 0; }
        .triptych { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 0.5em; }
        .triptych img { width: 100%; height: auto; border: 1px solid #ddd; }
        .triptych figcaption { text-align: center; font-size: 0.85em; color: #666; }
        """;
}
