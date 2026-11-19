using BH.oM.Test;
using BH.oM.Test.Results;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BH.Test.Serialiser
{
    // TEMPORARY. Sandbox fault-injection experiment for ci-serialisation; never for merging.
    // Reads $GITHUB_WORKSPACE/.sertest/inject.txt. No file means no injection.
    // Plain lines "Leg=mode" are this checkout's state. Lines "case <head ref>: Leg=mode; Leg=mode"
    // form a table looked up by GITHUB_HEAD_REF; when any case line exists, plain lines are ignored.
    // Legs: Methods, Objects, Types. Modes, joined with '+': fail-all, fail-half, fail-first:N, rename, throw.
    internal static class FaultInjection
    {
        public static List<TestResult> Apply(string verifyMethod, List<TestResult> results)
        {
            string leg = verifyMethod.Replace("ToFromJson", "");
            string mode = Mode(leg, out string source);
            Console.WriteLine($"[fault-injection] {verifyMethod}: leg={leg} mode={mode ?? "none"} source={source} results={results.Count}");
            if (mode == null)
                return results;

            List<string> parts = mode.Split('+').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (parts.Contains("throw"))
                throw new InvalidOperationException($"Injected throw in {verifyMethod} (fault-injection test).");

            HashSet<string> failing = new HashSet<string>(StringComparer.Ordinal);
            List<string> ordered = results.Select(x => x.Description).OrderBy(x => x, StringComparer.Ordinal).ToList();
            foreach (string part in parts)
            {
                if (part == "fail-all")
                    failing.UnionWith(ordered);
                else if (part == "fail-half")
                    failing.UnionWith(ordered.Where(x => Fnv1a(x) % 2 == 0));
                else if (part.StartsWith("fail-first:"))
                    failing.UnionWith(ordered.Take(int.Parse(part.Substring("fail-first:".Length))));
                else if (part != "rename")
                    throw new InvalidOperationException($"Unknown fault-injection mode '{part}'.");
            }

            string renamed = parts.Contains("rename") ? ordered.FirstOrDefault(x => failing.Contains(x)) : null;
            List<TestResult> output = results.Select(r => failing.Contains(r.Description)
                ? new TestResult
                {
                    Description = r.Description == renamed ? r.Description + "Renamed" : r.Description,
                    Status = TestStatus.Error,
                    Message = $"Injected failure in {verifyMethod} (fault-injection test).",
                    Information = new List<ITestInformation>()
                }
                : r).ToList();

            Console.WriteLine($"[fault-injection] {verifyMethod}: injected {failing.Count} failure(s) of {results.Count}{(renamed != null ? ", renamed 1" : "")}");
            return output;
        }

        private static string Mode(string leg, out string source)
        {
            string workspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? "";
            string path = Path.Combine(workspace, ".sertest", "inject.txt");
            string headRef = Environment.GetEnvironmentVariable("GITHUB_HEAD_REF") ?? "";
            if (!File.Exists(path))
            {
                source = $"no marker at {path}";
                return null;
            }

            List<string> lines = File.ReadAllLines(path).Select(x => x.Trim())
                .Where(x => x.Length > 0 && !x.StartsWith("#")).ToList();
            string marker = lines.FirstOrDefault(x => x.StartsWith("marker:"))?.Substring("marker:".Length).Trim() ?? "unnamed";
            lines = lines.Where(x => !x.StartsWith("marker:")).ToList();

            List<string> assignments;
            if (lines.Any(x => x.StartsWith("case ")))
            {
                string row = lines.FirstOrDefault(x => x.StartsWith($"case {headRef}:"));
                source = $"marker '{marker}', table row for head ref '{headRef}'{(row == null ? " (none)" : "")}";
                assignments = row == null ? new List<string>() : row.Substring(row.IndexOf(':') + 1).Split(';').ToList();
            }
            else
            {
                source = $"marker '{marker}', plain";
                assignments = lines;
            }

            foreach (string a in assignments)
            {
                string[] kv = a.Split(new[] { '=' }, 2);
                if (kv.Length == 2 && kv[0].Trim() == leg)
                    return kv[1].Trim();
            }
            return null;
        }

        private static uint Fnv1a(string text)
        {
            uint hash = 2166136261;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash;
        }
    }
}
