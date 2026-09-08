using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CodingSahayi;

/// <summary>Loss value captured during training.</summary>
public sealed class TrainingMetric
{
    public int    Step  { get; init; }
    public double Loss  { get; init; }
}

/// <summary>
/// Orchestrates the Soup LoRA fine-tuning pipeline:
/// dataset export → soup.yaml generation → <c>soup train</c> execution (live streaming) → metric parsing.
/// </summary>
public static class ModelTrainerTool
{
    // ANSI/VT escape-sequence scrubber so control codes don't render as garbage in the WinUI console.
    private static readonly Regex AnsiRegex = new(@"\x1B\[[^@-~]*[=@-~]", RegexOptions.Compiled);

    // Matches lines like:
    //   step=10  loss=0.8423
    //   {'loss': 0.8423, 'step': 10}
    //   [Step 10] loss: 0.8423
    private static readonly Regex LossRegex = new(
        @"(?:step[=:\s]+(\d+).*?loss[=:\s]+([\d.]+))|(?:loss[=:\s]+([\d.]+).*?step[=:\s]+(\d+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Runs <c>soup train --config &lt;configPath&gt;</c> as a live-streaming process.
    /// Every sanitized output line (stdout + stderr) is forwarded through <paramref name="onOutputLine"/>,
    /// which the caller marshals to its UI thread. The process tree is terminated on cancellation
    /// via <c>taskkill /T /F</c> so lingering Python/CUDA workers don't pin GPU VRAM.
    /// </summary>
    /// <param name="soupExePath">Absolute path to the <c>soup.exe</c> executable (or a <c>python</c> wrapper).</param>
    /// <param name="configPath">Absolute path to the generated <c>soup.yaml</c>.</param>
    /// <param name="onOutputLine">Callback invoked for each line emitted by the process (already ANSI-scrubbed).</param>
    /// <param name="ct">Cancellation token; cancelling kills the child process tree.</param>
    /// <returns><c>true</c> if the process exited with code 0, otherwise <c>false</c>.</returns>
    public static async Task<bool> RunSoupTrainingAsync(
        string soupExePath,
        string configPath,
        Action<string>? onOutputLine,
        CancellationToken ct = default,
        string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(soupExePath))
            throw new ArgumentException("Soup executable path is required.", nameof(soupExePath));
        if (string.IsNullOrWhiteSpace(configPath))
            throw new ArgumentException("soup.yaml config path is required.", nameof(configPath));

        // Pre-spawn integrity guard: the process cannot start without the generated
        // soup.yaml on disk. Report the failure to the live console and abort instead
        // of spawning a process with no config.
        if (!File.Exists(configPath))
        {
            onOutputLine?.Invoke($"❌ Config not found: {configPath}");
            onOutputLine?.Invoke("⛔ Aborting: soup.yaml is missing. Ensure the .soup directory and config were written before training.");
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = soupExePath,
            Arguments = $"train --config \"{configPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory
        };

        var rawBuffer = new StringBuilder();      // full raw (scrubbed) output for metric parsing
        var outputLock = new object();
        var exitTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        void HandleLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            string clean = AnsiRegex.Replace(line, "");
            lock (outputLock) rawBuffer.AppendLine(clean);
            onOutputLine?.Invoke(clean);   // already sanitized
        }

        process.OutputDataReceived += (s, e) => { if (e.Data != null) HandleLine(e.Data); };
        process.ErrorDataReceived  += (s, e) => { if (e.Data != null) HandleLine(e.Data); };
        process.Exited             += (s, e) => exitTcs.TrySetResult(process.ExitCode);

        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Failed to start process: {soupExePath}");
        }
        catch (Exception ex)
        {
            onOutputLine?.Invoke($"❌ Failed to start soup.exe ({soupExePath}): {ex.Message}");
            return false;
        }

        // Begin async stream reads BEFORE the process can flood the pipe buffers (deadlock risk otherwise).
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Register cancellation → kill the whole process tree (python + CUDA children).
        using var reg = ct.Register(() =>
        {
            try { process.Kill(true); }                    // .NET 5+: kills the process tree directly
            catch { /* already exited */ }
            if (!process.HasExited)
            {
                try
                {
                    var pi = new ProcessStartInfo
                    {
                        FileName = "taskkill",
                        Arguments = $"/PID {process.Id} /T /F",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var kill = Process.Start(pi);
                    kill?.WaitForExit(5000);
                }
                catch { /* best-effort */ }
                exitTcs.TrySetResult(-1);
            }
        });

        onOutputLine?.Invoke($"▶ Starting: {soupExePath} train --config \"{configPath}\"");

        int exitCode;
        try
        {
            // Wait for exit but respect cancellation via WhenAny so we can react to ct immediately.
            var exitTask = exitTcs.Task;
            var completed = await Task.WhenAny(exitTask, Task.Delay(Timeout.Infinite, ct)).ConfigureAwait(false);
            if (completed == exitTask)
            {
                exitCode = await exitTask.ConfigureAwait(false);
            }
            else
            {
                // ct fired before process exited → teardown handled in the registration callback.
                onOutputLine?.Invoke("⏹ Training cancelled.");
                exitCode = -1;
                try { process.Kill(true); } catch { }
            }
        }
        catch (OperationCanceledException)
        {
            onOutputLine?.Invoke("⏹ Training cancelled.");
            exitCode = -1;
        }
        finally
        {
            // Drain any residual buffered output and dispose the registration.
            try { if (!process.HasExited) process.Kill(true); } catch { }
            reg.Dispose();
        }

        // Parse loss metrics from the aggregated raw output.
        var metrics = ParseMetrics(rawBuffer.ToString());

        if (exitCode == 0)
        {
            onOutputLine?.Invoke($"✅ Training complete. Captured {metrics.Count} loss data-points.");
            return true;
        }

        if (exitCode == -1)
            onOutputLine?.Invoke("⚠ Training was aborted (cancellation).");
        else
            onOutputLine?.Invoke($"❌ soup train exited with code {exitCode}. Captured {metrics.Count} loss data-points.");
        return false;
    }

    /// <summary>
    /// Prepares the fine-tuning pipeline artifacts for <paramref name="workspacePath"/>:
    /// exports the ProjectKnowledge DB to JSONL, generates <c>soup.yaml</c>, and resolves the
    /// Soup executable path. Returns the resolved (exePath, configPath) on success.
    /// </summary>
    public static async Task<(bool ok, string exePath, string configPath, string message)> PreparePipelineAsync(
        string workspacePath,
        IProgress<string>? progress)
    {
        var soupDir     = Path.Combine(workspacePath, ".soup");
        var datasetPath = Path.Combine(soupDir, "dataset.jsonl");

        progress?.Report("📦 Exporting ProjectKnowledge to JSONL dataset…");
        int recordCount;
        try
        {
            recordCount = await DatasetExporter.ExportKnowledgeToJsonl(datasetPath);
        }
        catch (Exception ex)
        {
            var msg = $"❌ Dataset export failed: {ex.Message}";
            progress?.Report(msg);
            return (false, "", "", msg);
        }

        if (recordCount == 0)
        {
            var msg = "⚠️ No ProjectKnowledge entries found. Train the agent on some tasks first.";
            progress?.Report(msg);
            return (false, "", "", msg);
        }
        progress?.Report($"✅ Exported {recordCount} records → {datasetPath}");

        progress?.Report("⚙️  Generating soup.yaml LoRA config…");
        string configPath;
        try
        {
            configPath = await SoupConfigManager.GenerateSoupYaml(workspacePath, datasetPath);
        }
        catch (Exception ex)
        {
            var msg = $"❌ Config generation failed: {ex.Message}";
            progress?.Report(msg);
            return (false, "", "", msg);
        }
        if (!File.Exists(configPath))
        {
            var msg = $"❌ Generated config not found: {configPath}";
            progress?.Report(msg);
            return (false, "", "", msg);
        }
        progress?.Report($"✅ Config written → {configPath}");

        var (app, _, defaultWorkDir) = ResolveSoupInvocation(configPath, workspacePath);
        if (string.IsNullOrWhiteSpace(app) || app == "soup")
        {
            var msg = "❌ Could not locate a soup.exe. Verify the Soup repository path or install Soup into PATH.";
            progress?.Report(msg);
            return (false, "", "", msg);
        }
        progress?.Report($"🔍 Soup executable: {app}");

        // If we resolved a python wrapper in the repo, the working dir shifts to the repo root.
        var runDir = Path.GetDirectoryName(app) ?? defaultWorkDir;
        return (true, app, configPath, "");
    }

    /// <summary>
    /// Resolves the Soup executable, prioritizing the configured Soup repository, its
    /// virtual environments, Python scripts, PATH, or the repo's python module.
    /// </summary>
    public static (string App, string Arguments, string WorkingDir) ResolveSoupInvocation(string configPath, string defaultWorkingDir)
    {
        string soupRepo = SettingsManager.SoupPath;

        // 1. soup.exe inside the repository venvs
        if (!string.IsNullOrWhiteSpace(soupRepo) && Directory.Exists(soupRepo))
        {
            string venvSoup = Path.Combine(soupRepo, ".venv", "Scripts", "soup.exe");
            if (File.Exists(venvSoup))
                return (venvSoup, $"train --config \"{configPath}\"", defaultWorkingDir);

            string envSoup = Path.Combine(soupRepo, "venv", "Scripts", "soup.exe");
            if (File.Exists(envSoup))
                return (envSoup, $"train --config \"{configPath}\"", defaultWorkingDir);
        }

        // 2. Standard Python user Scripts directory
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string userPythonScript = Path.Combine(localAppData, "Programs", "Python", "Python312", "Scripts", "soup.exe");
        if (File.Exists(userPythonScript))
            return (userPythonScript, $"train --config \"{configPath}\"", defaultWorkingDir);

        // 3. PATH
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(dir, "soup.exe");
                if (File.Exists(candidate))
                    return (candidate, $"train --config \"{configPath}\"", defaultWorkingDir);
            }
        }

        // 4. Cloned repo with src/soup_cli → run via python module
        if (!string.IsNullOrWhiteSpace(soupRepo) && Directory.Exists(Path.Combine(soupRepo, "src", "soup_cli")))
        {
            string pythonExe = Path.Combine(localAppData, "Programs", "Python", "Python312", "python.exe");
            if (!File.Exists(pythonExe)) pythonExe = "python";
            return (pythonExe, $"-m soup_cli.cli train --config \"{configPath}\"", soupRepo);
        }

        // 5. Default fallback (caller validates; treated as unresolved when app == "soup")
        return ("soup", $"train --config \"{configPath}\"", defaultWorkingDir);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    public static List<TrainingMetric> ParseMetrics(string rawOutput)
    {
        var metrics = new List<TrainingMetric>();
        int autoStep = 0;

        foreach (var line in rawOutput.Split('\n'))
        {
            var match = LossRegex.Match(line);
            if (!match.Success) continue;

            // Group layout depends on which alternative matched:
            //   Alt 1: groups 1 (step), 2 (loss)
            //   Alt 2: groups 3 (loss), 4 (step)
            bool alt1 = match.Groups[1].Success;
            string stepStr = alt1 ? match.Groups[1].Value : match.Groups[4].Value;
            string lossStr = alt1 ? match.Groups[2].Value : match.Groups[3].Value;

            if (!double.TryParse(lossStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double loss))
                continue;

            int step = int.TryParse(stepStr, out int s) ? s : ++autoStep;

            metrics.Add(new TrainingMetric { Step = step, Loss = loss });
        }

        return metrics;
    }
}
