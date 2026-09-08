using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CodingSahayi;

public static class TestRunnerTool
{
    public static async Task<string> RunTestsAsync(string projectPath)
    {
        string result = await PtyManager.ExecuteCommandAsync("dotnet", "test", projectPath, 60);
        
        if (result.Contains("Passed!") && !result.Contains("Failed!"))
        {
            return "All tests passed successfully.";
        }
        
        if (result.Contains("Failed"))
        {
            return ExtractFailures(result);
        }

        return $"Test execution completed with unexpected output:\n{result}";
    }

    /// <summary>
    /// Parses modern VSTest output (dotnet test) into structured per-test failure blocks.
    /// Extracts test name, the failure/error message, and the stack trace using regex,
    /// falling back to the last 30 output lines when no block could be parsed.
    /// </summary>
    private static string ExtractFailures(string result)
    {
        // Failures appear in two common VSTest shapes:
        //   A)  Failed test_name [x ms]
        //       Error Message:
        //        <message>
        //       Stack Trace:
        //        <trace>
        //   B)  Failed!  - Failed: N, Passed: M...
        var blocks = Regex.Matches(result,
            @"^\s*Failed\s+([^\r\n\[\]]+?)(?:\s*\[[\d.]+\s*ms\])?\s*\r?\n(?:.*?Error Message:\s*\r?\n(.*?))?(?:Stack Trace:\s*\r?\n(.*?))?(?=\r?\n\s*Failed\s+|$)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var allNames = Regex.Matches(result, @"Failed\s+([^\r\n\[\]]+?)(?:\s*\[[\d.]+\s*ms\])?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value.Trim())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct()
            .ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Test Failures Detected:");

        int index = 1;
        foreach (Match block in blocks)
        {
            string testName = block.Groups[1].Value.Trim();
            string message = block.Groups[2].Success ? block.Groups[2].Value.Trim() : "";
            string trace = block.Groups[3].Success ? block.Groups[3].Value.Trim() : "";

            sb.AppendLine($"{index}. {testName}");
            if (!string.IsNullOrEmpty(message))
                sb.AppendLine($"   Assertion: {message}");
            if (!string.IsNullOrEmpty(trace))
                sb.AppendLine($"   Trace: {trace}");
            index++;
        }

        // If the regex produced no per-block capture, fall back to naming the failed
        // tests we saw, plus a trailing slice of the raw output for context.
        if (index == 1)
        {
            if (allNames.Count > 0)
            {
                foreach (var name in allNames)
                {
                    sb.AppendLine($"{index}. {name}");
                    index++;
                }
            }
            else
            {
                // Last-resort fallback: last 30 lines of raw output.
                var lines = result.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                var tail = lines.Skip(Math.Max(0, lines.Count - 30)).ToList();
                sb.AppendLine("(Could not parse per-test failure blocks. Raw output tail:)");
                foreach (var line in tail)
                    sb.AppendLine("   " + line);
            }
        }

        return sb.ToString();
    }
}
