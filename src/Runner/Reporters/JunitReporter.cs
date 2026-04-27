using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using SdvTestFramework.Runner.Scenarios;

namespace SdvTestFramework.Runner.Reporters;

/// <summary>
/// JUnit XML reporter producing Jenkins-compatible output (testsuites → testsuite →
/// testcase). Scenarios map to testcases; <see cref="ScenarioReport.Path"/> becomes the
/// <c>classname</c> attribute following the Jenkins convention of "classname = file path".
/// </summary>
/// <remarks>
/// Schema: https://llg.cubic.org/docs/junit/. Consumed by GitHub Actions, GitLab, Jenkins,
/// and most other CI test-result aggregators. Failure bodies carry all <see cref="ScenarioReport.Failures"/>
/// entries joined by newline; the <c>message</c> attribute carries just the first (most UIs
/// display only the message).
/// </remarks>
public sealed class JunitReporter : IReporter
{
    public void Report(IReadOnlyList<ScenarioReport> reports, TextWriter output)
    {
        int totalFailures = 0;
        int totalMs = 0;
        foreach (var r in reports)
        {
            if (!r.Passed) totalFailures++;
            totalMs += r.DurationMs;
        }

        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = false,
        };

        using var w = XmlWriter.Create(output, settings);
        w.WriteStartDocument();

        w.WriteStartElement("testsuites");
        w.WriteAttributeString("tests", reports.Count.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("failures", totalFailures.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("errors", "0");
        w.WriteAttributeString("time", FormatSeconds(totalMs));

        w.WriteStartElement("testsuite");
        w.WriteAttributeString("name", "sdv-test");
        w.WriteAttributeString("tests", reports.Count.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("failures", totalFailures.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("errors", "0");
        w.WriteAttributeString("time", FormatSeconds(totalMs));
        w.WriteAttributeString("timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

        foreach (var r in reports)
        {
            w.WriteStartElement("testcase");
            w.WriteAttributeString("classname", r.Path);
            w.WriteAttributeString("name", r.Name);
            w.WriteAttributeString("time", FormatSeconds(r.DurationMs));

            if (!r.Passed)
            {
                w.WriteStartElement("failure");
                w.WriteAttributeString("type", "assertion");
                w.WriteAttributeString("message", r.Failures.Count > 0 ? r.Failures[0] : "assertion failed");
                w.WriteString(string.Join("\n", r.Failures));
                w.WriteEndElement();  // failure
            }

            w.WriteEndElement();  // testcase
        }

        w.WriteEndElement();  // testsuite
        w.WriteEndElement();  // testsuites
        w.WriteEndDocument();
    }

    /// <summary>Milliseconds → seconds, 3-decimal fixed, invariant culture. Matches Jenkins' parser.</summary>
    private static string FormatSeconds(int millis)
    {
        return (millis / 1000.0).ToString("F3", CultureInfo.InvariantCulture);
    }
}
