using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CodingSahayi;

/// <summary>
/// Result returned by <see cref="ModelTrainerTool.RunSoupTrainingAsync"/>.
/// </summary>
public sealed class TrainingResult
{
    /// <summary>Human-readable summary of how the run finished.</summary>
    public string ExitMessage { get; init; } = string.Empty;

    /// <summary>Path of the JSONL dataset file that was used for training.</summary>
    public string DatasetPath { get; init; } = string.Empty;

    /// <summary>Path of the generated <c>soup.yaml</c> config file.</summary>
    public string ConfigPath { get; init; } = string.Empty;

    /// <summary>Training loss values extracted from the PTY output, in order.</summary>
    public IReadOnlyList<TrainingMetric> Metrics { get; init; } = Array.Empty<TrainingMetric>();

    /// <summary>Full raw PTY output from the <c>soup train</c> process.</summary>
    public string RawOutput { get; init; } = string.Empty;
}

/// <summary>A single loss measurement captured during training.</summary>
public sealed class TrainingMetric
{
    public int    Step  { get; init; }
    public double Loss  { get; init; }
}

/// <summary>
/// Orchestrates the full fine-tuning pipeline:
/// dataset export → soup.yaml generation → <c>soup train</c> execution → metric parsing.
/// </summary>
public static class ModelTrainerTool
{
    // Matches lines like:
    //   step=10  loss=0.8423
    //   {'loss': 0.8423, 'step': 10}
    //   [Step 10] loss: 0.8423
    private static readonly Regex LossRegex = new(
        @"(?:step[=:\s]+(\d+).*?loss[=:\s]+([\d.]+))|(?:loss[=:\s]+([\d.]+).*?step[=:\s]+(\d+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Runs the complete fine-tuning pipeline for the given workspace.
    /// </summary>
    /// <param name="workspacePath">
    /// Directory used as the training root.  The <c>.soup/</c> subdirectory will be
    /// created here to hold the dataset, config, and adapter output.
    /// </param>
    /// <param name="progress">
    /// Optional progress sink that receives human-readable status lines as the
    /// pipeline advances (safe to marshal to the UI thread).
    /// </param>
    /// <returns>A <see cref="TrainingResult"/> describing the completed run.</returns>
    public static async Task<TrainingResult> RunSoupTrainingAsync(
        string workspacePath,
        IProgress<string>? progress = null)
    {
        // ── Step 1: Export knowledge base to JSONL ────────────────────────────
        progress?.Report("📦 Exporting ProjectKnowledge to JSONL dataset…");

        var soupDir     = Path.Combine(workspacePath, ".soup");
        var datasetPath = Path.Combine(soupDir, "dataset.jsonl");

        int recordCount;
        try
        {
            recordCount = await DatasetExporter.ExportKnowledgeToJsonl(datasetPath);
        }
        catch (Exception ex)
        {
            var msg = $"❌ Dataset export failed: {ex.Message}";
            progress?.Report(msg);
            return new TrainingResult { ExitMessage = msg };
        }

        if (recordCount == 0)
        {
            var msg = "⚠️ No ProjectKnowledge entries found. Train the agent on some tasks first.";
            progress?.Report(msg);
            return new TrainingResult { ExitMessage = msg, DatasetPath = datasetPath };
        }

        progress?.Report($"✅ Exported {recordCount} records → {datasetPath}");

        // ── Step 2: Generate soup.yaml ────────────────────────────────────────
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
            return new TrainingResult { ExitMessage = msg, DatasetPath = datasetPath };
        }

        progress?.Report($"✅ Config written → {configPath}");

        // ── Step 3: Execute `soup train` via PTY ──────────────────────────────
        progress?.Report("🚀 Launching `soup train` — this may take a long time…");

        // 4-hour timeout to accommodate large datasets on slow hardware
        const int TimeoutSeconds = 4 * 60 * 60;

        string rawOutput;
        try
        {
            rawOutput = await PtyManager.ExecuteCommandAsync(
                app:              "soup",
                arguments:        $"train --config \"{configPath}\"",
                workingDirectory: workspacePath,
                timeoutSeconds:   TimeoutSeconds);
        }
        catch (Exception ex)
        {
            var msg = $"❌ soup train failed to start: {ex.Message}";
            progress?.Report(msg);
            return new TrainingResult
            {
                ExitMessage = msg,
                DatasetPath = datasetPath,
                ConfigPath  = configPath
            };
        }

        // ── Step 4: Parse loss metrics from PTY stream ────────────────────────
        progress?.Report("📊 Parsing training metrics…");

        var metrics = ParseMetrics(rawOutput);

        string exitMessage = rawOutput.Contains("Error", StringComparison.OrdinalIgnoreCase)
                          || rawOutput.Contains("Traceback", StringComparison.OrdinalIgnoreCase)
            ? $"⚠️ Training finished with errors. Captured {metrics.Count} loss data-points."
            : $"✅ Training complete. Captured {metrics.Count} loss data-points.";

        if (metrics.Count > 0)
        {
            var last = metrics[^1];
            exitMessage += $" Final loss @ step {last.Step}: {last.Loss:F4}";
        }

        progress?.Report(exitMessage);

        return new TrainingResult
        {
            ExitMessage = exitMessage,
            DatasetPath = datasetPath,
            ConfigPath  = configPath,
            Metrics     = metrics,
            RawOutput   = rawOutput
        };
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static List<TrainingMetric> ParseMetrics(string rawOutput)
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
