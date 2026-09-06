using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CodingSahayi.Data;
using Microsoft.EntityFrameworkCore;

namespace CodingSahayi;

/// <summary>
/// Exports ProjectKnowledge entries to a JSONL dataset file suitable for LLM fine-tuning.
/// Each line is a JSON object in the OpenAI chat-completion messages format.
/// </summary>
public static class DatasetExporter
{
    /// <summary>
    /// Reads all non-empty ProjectKnowledge entries from the database and writes them
    /// to a JSONL file at <paramref name="outputPath"/> in the instruction-response format:
    /// <code>{"messages": [{"role": "user", "content": "..."}, {"role": "assistant", "content": "..."}]}</code>
    /// </summary>
    /// <param name="outputPath">Absolute path of the output .jsonl file to create or overwrite.</param>
    /// <returns>The number of records written.</returns>
    public static async Task<int> ExportKnowledgeToJsonl(string outputPath)
    {
        // Ensure output directory exists
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        int recordsWritten = 0;

        await using var db = new AppDbContext();

        // Fetch all entries that have actual learned content — the practical
        // equivalent of "successful" entries since there is no IsSuccessful flag.
        var entries = await db.ProjectKnowledgeBase
            .Where(k => !string.IsNullOrWhiteSpace(k.LearnedImplementation)
                     && !string.IsNullOrWhiteSpace(k.TaskDescription))
            .OrderBy(k => k.DateLearned)
            .ToListAsync();

        await using var writer = new StreamWriter(outputPath, append: false, System.Text.Encoding.UTF8);

        var serializerOptions = new JsonSerializerOptions
        {
            // Compact output — one JSON object per line
            WriteIndented = false
        };

        foreach (var entry in entries)
        {
            var record = new FineTuneRecord
            {
                Messages = new[]
                {
                    new ChatMessage { Role = "user",      Content = entry.TaskDescription },
                    new ChatMessage { Role = "assistant", Content = entry.LearnedImplementation }
                }
            };

            string json = JsonSerializer.Serialize(record, serializerOptions);
            await writer.WriteLineAsync(json);
            recordsWritten++;
        }

        return recordsWritten;
    }

    // ─── Private POCO types used only for JSON serialisation ───────────────

    private sealed class FineTuneRecord
    {
        [System.Text.Json.Serialization.JsonPropertyName("messages")]
        public ChatMessage[] Messages { get; set; } = Array.Empty<ChatMessage>();
    }

    private sealed class ChatMessage
    {
        [System.Text.Json.Serialization.JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }
}
