using System;

namespace CodingSahayi.Data
{
    public class Project
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string WorkspacePath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        
        public System.Collections.Generic.ICollection<Conversation> Conversations { get; set; } = new System.Collections.Generic.List<Conversation>();
    }

    public class Conversation
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
        
        public Project Project { get; set; } = null!;
    }

    public class ChatMessageEntity
    {
        public int Id { get; set; }
        public int ConversationId { get; set; }
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        
        public Conversation Conversation { get; set; } = null!;
    }

    public class ProjectKnowledge
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public string TaskDescription { get; set; } = string.Empty;
        public string LearnedImplementation { get; set; } = string.Empty;
        public DateTime DateLearned { get; set; }
    }

    public class CodeChunk
    {
        public int Id { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string EmbeddingJson { get; set; } = string.Empty;
    }

    public enum ModelType { Cloud, Local }
    public enum CostTier { Free, Paid, Local }

    public class ModelEndpointConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string DisplayName { get; set; } = string.Empty;
        public string ModelIdentifier { get; set; } = string.Empty; // e.g., "meta/llama-3.1-70b-instruct" or "gemma4:26b"
        public string BaseUrl { get; set; } = "http://localhost:11434/v1";
        // API keys are NOT serialized into the LocalSettings JSON blob — they are
        // stored securely in Windows PasswordVault keyed by the model's Id.
        [System.Text.Json.Serialization.JsonIgnore]
        public string ApiKey { get; set; } = string.Empty;
        public ModelType Type { get; set; } = ModelType.Local;
        public CostTier CostTier { get; set; } = CostTier.Local;
        public int Priority { get; set; } = 1; // Lower = higher priority (1 is primary)
        public string? FallbackModelId { get; set; } // Points to another ModelEndpointConfig.Id
        public bool IsEnabled { get; set; } = true;
        public bool IsDefault { get; set; } = false;
        public bool AllowFallback { get; set; } = true;
    }

    public class ApiRequestLog
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Feature { get; set; } = "Chat";
        public string Provider { get; set; } = "OpenAI";
        public string Model { get; set; } = string.Empty;
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens { get; set; }
        public long LatencyMs { get; set; }
        public int StatusCode { get; set; } = 200;
        public string PromptSnippet { get; set; } = string.Empty;
        public string FullPrompt { get; set; } = string.Empty;
        public string ResponseContent { get; set; } = string.Empty;
        public double EstimatedCostUsd { get; set; }
    }
}