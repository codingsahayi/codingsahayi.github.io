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
    private const int PromptVersion = 4; // Bump this when the default prompt changes
    
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
                return "lm-studio";
            }
        }
        set
        {
            var vault = new PasswordVault();
            vault.Add(new PasswordCredential(ResourceName, "LocalApiKey", value));
        }
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
            var json = LocalSettings.Values["ConfiguredModels"] as string;
            if (!string.IsNullOrEmpty(json))
            {
                try { return JsonSerializer.Deserialize<List<CodingSahayi.Data.ModelEndpointConfig>>(json) ?? new List<CodingSahayi.Data.ModelEndpointConfig>(); }
                catch { }
            }
            return new List<CodingSahayi.Data.ModelEndpointConfig>
            {
                new CodingSahayi.Data.ModelEndpointConfig { DisplayName = "Ollama Local", ModelIdentifier = "gemma4:26b", BaseUrl = "http://localhost:11434/v1", Type = CodingSahayi.Data.ModelType.Local, CostTier = CodingSahayi.Data.CostTier.Local, Priority = 1 },
                new CodingSahayi.Data.ModelEndpointConfig { DisplayName = "Claude 3.5 Sonnet", ModelIdentifier = "anthropic/claude-3.5-sonnet-20240620", BaseUrl = "https://openrouter.ai/api/v1", Type = CodingSahayi.Data.ModelType.Cloud, CostTier = CodingSahayi.Data.CostTier.Paid, Priority = 2 }
            };
        }
        set => LocalSettings.Values["ConfiguredModels"] = JsonSerializer.Serialize(value);
    }

    // EnsureModelInList removed as model config is now object-based

    private const string DefaultSystemPrompt = """
You are an expert native Windows coding agent operating inside a WinUI 3 IDE called Coding Sahayi.

OPERATIONAL RULES:
1. EXPLORE FIRST: Never assume file paths. Use list_directory or search_code to locate files before reading or editing them.
2. READ BEFORE EDITING: Always read_file to see the current contents before modifying a file.
3. SMALL CHANGES: Use patch_file for 1-2 small changes. Include enough surrounding lines in targetSnippet to make it unique.
4. LARGE CHANGES: When you need to make 3 or more changes to a single file, use write_file to rewrite the ENTIRE file at once instead of multiple patch_file calls. This is much more efficient.
5. FIX ALL ERRORS AT ONCE: When a build fails with multiple errors, read ALL affected files, plan ALL fixes, then apply them all before rebuilding. Use write_file to rewrite each affected file with all fixes included.
6. VERIFY: After modifications, run dotnet build via execute_terminal to verify.
7. AUTO-CORRECT: If a build fails, inspect ALL errors, fix everything, then rebuild. Do NOT fix one error at a time.

TOOL USAGE RULES:
- You must only execute ONE tool call at a time.
- You must invoke tools using the native tool calling API. Do NOT output raw JSON tool calls in your chat text.
- The execute_terminal working directory defaults to the user's workspace. You do not need to specify it.

CRITICAL RULE: 
Once you have successfully completed the user's objective and verified the build succeeds, you must immediately output a final text summary and you MUST NOT call any further tools.
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
