using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Windows.Security.Credentials;
using Windows.Storage;

namespace CodingSahayi;

public static class SettingsManager
{
    private const string ResourceName = "VibeCoderAgent";
    private const string ApiKeyUserName = "ApiKey";
    private const int PromptVersion = 5; // Bump this when the default prompt changes
    
    private static readonly ApplicationDataContainer LocalSettings = ApplicationData.Current.LocalSettings;

    public static string ApiEndpoint
    {
        get => LocalSettings.Values["ApiEndpoint"] as string ?? "https://openrouter.ai/api/v1";
        set => LocalSettings.Values["ApiEndpoint"] = value;
    }

    public static string ModelName
    {
        get => LocalSettings.Values["ModelName"] as string ?? "anthropic/claude-3.5-sonnet-20240620";
        set => LocalSettings.Values["ModelName"] = value;
    }

    public static string LocalApiBaseUrl
    {
        get => LocalSettings.Values["LocalApiBaseUrl"] as string ?? "http://localhost:11434/v1";
        set => LocalSettings.Values["LocalApiBaseUrl"] = value;
    }

    public static string LocalApiKey
    {
        get
        {
            var vault = new PasswordVault();
            try
            {
                var credential = vault.Retrieve(ResourceName, "LocalApiKey");
                credential.RetrievePassword();
                return credential.Password;
            }
            catch (Exception)
            {
                return "ollama";
            }
        }
        set
        {
            var vault = new PasswordVault();
            vault.Add(new PasswordCredential(ResourceName, "LocalApiKey", value));
        }
    }

    public static bool UseLocalModel
    {
        get => LocalSettings.Values["UseLocalModel"] as bool? ?? false;
        set => LocalSettings.Values["UseLocalModel"] = value;
    }

    public static string HybridRouterStrategy
    {
        get => LocalSettings.Values["HybridRouterStrategy"] as string ?? "CostOptimized";
        set => LocalSettings.Values["HybridRouterStrategy"] = value;
    }

    public static string LocalModelName
    {
        get => LocalSettings.Values["LocalModelName"] as string ?? "local-model";
        set => LocalSettings.Values["LocalModelName"] = value;
    }

    public static string SoupPath
    {
        get => LocalSettings.Values["SoupPath"] as string ?? @"D:\Soup";
        set => LocalSettings.Values["SoupPath"] = value;
    }

    public static string SoupBaseModel
    {
        get => LocalSettings.Values["SoupBaseModel"] as string ?? "Qwen/Qwen2.5-Coder-1.5B";
        set => LocalSettings.Values["SoupBaseModel"] = value;
    }

    public static List<CodingSahayi.Data.ModelEndpointConfig> ConfiguredModels
    {
        get
        {
            List<CodingSahayi.Data.ModelEndpointConfig> list = new();
            var json = LocalSettings.Values["ConfiguredModels"] as string;
            
            if (!string.IsNullOrEmpty(json))
            {
                try { list = JsonSerializer.Deserialize<List<CodingSahayi.Data.ModelEndpointConfig>>(json) ?? new(); }
                catch { }
            }
            
            // Deduplicate by BaseUrl and ModelIdentifier
            list = list.GroupBy(x => new { x.BaseUrl, x.ModelIdentifier })
                       .Select(g => g.First())
                       .ToList();
            
            if (list.Count == 0)
            {
                list.Add(new CodingSahayi.Data.ModelEndpointConfig { DisplayName = "Ollama Local", ModelIdentifier = "gemma4:26b", BaseUrl = "http://localhost:11434/v1", Type = CodingSahayi.Data.ModelType.Local, CostTier = CodingSahayi.Data.CostTier.Local, Priority = 1 });
                list.Add(new CodingSahayi.Data.ModelEndpointConfig { DisplayName = "Claude 3.5 Sonnet", ModelIdentifier = "anthropic/claude-3.5-sonnet-20240620", BaseUrl = "https://openrouter.ai/api/v1", Type = CodingSahayi.Data.ModelType.Cloud, CostTier = CodingSahayi.Data.CostTier.Paid, Priority = 2 });
            }
            
            return list;
        }
        set => LocalSettings.Values["ConfiguredModels"] = JsonSerializer.Serialize(value);
    }

    // EnsureModelInList removed as model config is now object-based

    private const string DefaultSystemPrompt = """
You are an elite, autonomous AI software engineer operating directly inside the Coding Sahayi IDE. You have full access to the user's workspace, file system, and terminal.

YOUR PRIMARY DIRECTIVE:
Deliver production-ready, highly robust, and bug-free code. Solve the user's problem end-to-end without requiring hand-holding.

CORE BEHAVIORS:
1. THINK BEFORE ACTING: Always reason step-by-step about your approach. Analyze constraints, edge cases, and potential side-effects before modifying code.
2. EXPLORE INTENTIONALLY: Never guess file paths or APIs. Use your read tools (search, list_directory, read_file) to understand the codebase context BEFORE making changes.
3. BE EFFICIENT: 
   - For small edits (1-2 lines), use patch_file. Make sure your target snippet is entirely unique.
   - For structural changes or when fixing multiple errors in one file, use write_file to replace the entire file. Do NOT make 10 patch_file calls for one file.
4. TEST PROACTIVELY: After making changes, ALWAYS use execute_terminal to run tests, build the project (e.g., `dotnet build`), or lint. 
5. AUTO-CORRECT: If a terminal command or build fails, read the error output carefully. Do not ask the user for help. Investigate the cause, fix ALL errors simultaneously across all affected files, and verify again.
6. AVOID LOOPS: If you attempt a fix and it fails twice, STOP. Re-evaluate your fundamental assumptions.

TOOL EXECUTION RULES:
- Call ONE tool at a time (unless running strictly independent read operations).
- Wait for the tool's response before proceeding.
- When calling terminal commands, wait for them to complete unless they are long-running daemons.

COMMUNICATION:
- Be concise. The user wants results, not essays.
- Do NOT output your internal tool JSON or raw commands in the chat response. 
- When you are completely finished and have verified your solution works, output a brief summary of what you did and STOP.
""";

    public static string SystemPrompt
    {
        get
        {
            int storedVersion = LocalSettings.Values["SystemPromptVersion"] as int? ?? 0;
            if (storedVersion < PromptVersion)
            {
                // New default prompt available — auto-upgrade
                LocalSettings.Values["SystemPrompt"] = DefaultSystemPrompt;
                LocalSettings.Values["SystemPromptVersion"] = PromptVersion;
                return DefaultSystemPrompt;
            }
            return LocalSettings.Values["SystemPrompt"] as string ?? DefaultSystemPrompt;
        }
        set
        {
            LocalSettings.Values["SystemPrompt"] = value;
            LocalSettings.Values["SystemPromptVersion"] = PromptVersion;
        }
    }

    public static string SecureApiKey
    {
        get
        {
            var vault = new PasswordVault();
            try
            {
                var credential = vault.Retrieve(ResourceName, ApiKeyUserName);
                credential.RetrievePassword();
                return credential.Password;
            }
            catch (Exception)
            {
                return "YOUR_API_KEY";
            }
        }
        set
        {
            var vault = new PasswordVault();
            vault.Add(new PasswordCredential(ResourceName, ApiKeyUserName, value));
        }
    }
}
