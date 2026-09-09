using System;
using System.Text;
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
            return ExtractTestFailures(result);
        }

        return $"Test execution completed with unexpected output:\n{result}";
    }

    /// <summary>
    /// Parses modern <c>dotnet test</c> console output and extracts structured per-test failure
    /// blocks (<c>Failed &lt;TestName&gt;</c> + <c>Error Message:</c> + <c>Stack Trace:</c>) instead of
    /// dumping the raw console stream. If no failure block can be parsed, it falls back to the
    /// last 35 non-empty output lines for context.
    /// </summary>
    public static string ExtractTestFailures(string rawOutput)
    {
        var failurePattern = new Regex(
            @"Failed\s+([^\r\n\[]+).*?Error Message:\s*([^\r\n]+).*?Stack Trace:\s*([^\r\n]+)",
            RegexOptions.Singleline | RegexOptions.Multiline);

        string output = rawOutput ?? string.Empty;
        var matches = failurePattern.Matches(output);

        // No structured failure blocks found — fall back to the last 35 output lines.
        if (matches.Count == 0)
        {
            var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var tail = lines.Length <= 35 ? lines : lines[^35..];
            return string.Join("\n", tail);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Critic extracted {matches.Count} failing assertion(s):");
        int index = 1;
        foreach (Match match in matches)
        {
            sb.AppendLine($"{index++}. Test: {match.Groups[1].Value.Trim()}");
            sb.AppendLine($"   Error: {match.Groups[2].Value.Trim()}");
            sb.AppendLine($"   Trace: {match.Groups[3].Value.Trim()}");
        }
        return sb.ToString();
    }
}