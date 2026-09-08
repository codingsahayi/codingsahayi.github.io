using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OpenAI.Chat;
using OpenAI;

namespace CodingSahayi;

public class AgentContextManager
{

    private readonly ChatCompletionOptions _chatOptions;
    private readonly List<ChatMessage> _apiHistory = new();
    
    private const int MaxContextMessages = 20;

    public string WorkspaceDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public delegate void ToolStartHandler(string toolCallId, string toolName, string arguments);
    public delegate void ToolEndHandler(string toolCallId, string output, bool success);

    private readonly string _systemPrompt;

    public AgentContextManager(string systemPrompt = null, IEnumerable<string> allowedTools = null)
    {
        _systemPrompt = systemPrompt ?? SettingsManager.SystemPrompt;
        _chatOptions = new ChatCompletionOptions
        {
            AllowParallelToolCalls = false
        };

        var allTools = new List<ChatTool>
        {
            ChatTool.CreateFunctionTool(
                "read_file",
                "Reads a file from the disk.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"}},\"required\":[\"filePath\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "write_file",
                "Writes content to a file on the disk.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"content\":{\"type\":\"string\"}},\"required\":[\"filePath\",\"content\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "patch_file",
                "Replaces a specific snippet of text in a file with a new snippet.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"targetSnippet\":{\"type\":\"string\"},\"replacementSnippet\":{\"type\":\"string\"}},\"required\":[\"filePath\",\"targetSnippet\",\"replacementSnippet\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "list_directory",
                "Lists files and folders in a directory.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"directoryPath\":{\"type\":\"string\"},\"maxDepth\":{\"type\":\"integer\"}},\"required\":[\"directoryPath\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "search_code",
                "Searches for a string across files in a directory.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"searchQuery\":{\"type\":\"string\"},\"fileExtensionFilter\":{\"type\":\"string\"}},\"required\":[\"searchQuery\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "execute_terminal",
                "Executes a command. You do not need to provide a working directory; it will automatically default to the user's selected workspace root.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"command\":{\"type\":\"string\"},\"workingDirectory\":{\"type\":\"string\"},\"timeoutSeconds\":{\"type\":\"integer\"}},\"required\":[\"command\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "search_directory",
                "Recursively searches a directory for files matching a pattern.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"searchPattern\":{\"type\":\"string\"}},\"required\":[\"path\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "batch_patch_file",
                "Applies multiple patches to one or more files in a single call. Use this to fix ALL errors at once instead of patching one at a time. Each patch object has filePath, targetSnippet, and replacementSnippet.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"patches\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"targetSnippet\":{\"type\":\"string\"},\"replacementSnippet\":{\"type\":\"string\"}},\"required\":[\"filePath\",\"targetSnippet\",\"replacementSnippet\"]}}},\"required\":[\"patches\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "analyze_structure",
                "Analyzes structural components (classes, functions, interfaces) of a specified source file based on its file extension.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"}},\"required\":[\"filePath\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "verify_syntax",
                "Parses a source file based on its file extension and returns syntax errors with line numbers.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"}},\"required\":[\"filePath\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "resolve_symbol",
                "Looks up the definition details of a specific symbol in a source file based on its file extension.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"symbolName\":{\"type\":\"string\"}},\"required\":[\"filePath\",\"symbolName\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "semantic_code_search",
                "Performs a semantic vector search across the code base using Ollama embeddings to find relevant code chunks.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"}},\"required\":[\"query\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "run_tests",
                "Runs tests in a specified directory using dotnet test in a PTY, returning formatted failure output or success.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"projectPath\":{\"type\":\"string\"}},\"required\":[\"projectPath\"]}")
            ),
            ChatTool.CreateFunctionTool(
                "train_local_model",
                "Fine-tunes the local model on accumulated ProjectKnowledge entries using Soup CLI (D:\\Soup) with LoRA and layer streaming.",
                BinaryData.FromString("{\"type\":\"object\",\"properties\":{}}")
            )
        };

        if (allowedTools != null)
        {
            var allowedSet = new HashSet<string>(allowedTools);
            foreach (var tool in allTools)
            {
                if (allowedSet.Contains(tool.FunctionName))
                    _chatOptions.Tools.Add(tool);
            }
        }
        else
        {
            foreach (var tool in allTools)
                _chatOptions.Tools.Add(tool);
        }

        InitializeClient();
        _apiHistory.Add(new SystemChatMessage(_systemPrompt));
    }

    public void ReinitializeClient()
    {
        InitializeClient();
        if (_apiHistory.Count > 0)
        {
            _apiHistory[0] = new SystemChatMessage(_systemPrompt);
        }
        else
        {
            _apiHistory.Add(new SystemChatMessage(_systemPrompt));
        }
    }

    public void LoadHistory(IEnumerable<CodingSahayi.Data.ChatMessageEntity> dbMessages)
    {
        InitializeClient();
        _apiHistory.Clear();
        _apiHistory.Add(new SystemChatMessage(_systemPrompt));
        
        foreach (var msg in dbMessages.OrderBy(m => m.Timestamp))
        {
            if (msg.Role == "User")
            {
                _apiHistory.Add(new UserChatMessage(msg.Content));
            }
            else if (msg.Role == "Agent" || msg.Role == "Assistant")
            {
                _apiHistory.Add(new AssistantChatMessage(msg.Content));
            }
        }
    }

    private ChatClient GetChatClient(CodingSahayi.Data.ModelEndpointConfig config)
    {
        var endpoint = config.BaseUrl;
        if (!endpoint.EndsWith("/")) endpoint += "/";
        var options = new OpenAIClientOptions { Endpoint = new Uri(endpoint), NetworkTimeout = TimeSpan.FromMinutes(10) };
        var client = new OpenAIClient(new System.ClientModel.ApiKeyCredential(config.ApiKey ?? ""), options);
        return client.GetChatClient(config.ModelIdentifier);
    }

    private void InitializeClient()
    {
        // Handled dynamically now
    }

    public async Task<bool> IsLocalAvailableAsync()
    {
        try
        {
            var localConfig = SettingsManager.ConfiguredModels.OrderBy(m => m.Priority).FirstOrDefault(m => m.Type == CodingSahayi.Data.ModelType.Local && m.IsEnabled);
            if (localConfig == null) return false;
            var client = GetChatClient(localConfig);
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
            var pingHistory = new List<ChatMessage> { new UserChatMessage("Respond with exactly one word: pong.") };
            await client.CompleteChatAsync(pingHistory, cancellationToken: cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<(string route, string plan)> PlanAndRouteTask(string userPrompt, System.Threading.CancellationToken cancellationToken = default)
    {
        try
        {
            using var routerCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            routerCts.CancelAfter(TimeSpan.FromSeconds(15));
            
            var routerSystem = new SystemChatMessage(
                "You are a task complexity classifier. Analyze the user's task and respond with ONLY a JSON object.\n" +
                "Evaluate based on: Does it need multi-file edits? Does it need architectural reasoning? Does it need tool calls (reading/writing files, running commands)?\n" +
                "Simple tasks: greetings, Q&A, explanations, single-concept questions, formatting.\n" +
                "Complex tasks: code generation, debugging, refactoring, multi-step builds, file operations.\n" +
                "Output format: {\"Plan\": \"brief 1-sentence plan\", \"ComplexityScore\": <1-10>}");
            var routerUser = new UserChatMessage(userPrompt);
            var routerHistory = new List<ChatMessage> { routerSystem, routerUser };
            
            var routerOptions = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
            };
            
            var localConfig = SettingsManager.ConfiguredModels.OrderBy(m => m.Priority).FirstOrDefault(m => m.Type == CodingSahayi.Data.ModelType.Local && m.IsEnabled);
            if (localConfig == null) return ("API", "");
            var client = GetChatClient(localConfig);
            var completion = await client.CompleteChatAsync(routerHistory, routerOptions, routerCts.Token);
            var responseText = completion.Value.Content[0].Text;
            
            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;
            
            string plan = "";
            if (root.TryGetProperty("Plan", out var planProp) || root.TryGetProperty("plan", out planProp))
                plan = planProp.GetString() ?? "";
            
            int score = 5; // default to cloud
            JsonElement scoreProp;
            if (root.TryGetProperty("ComplexityScore", out scoreProp) || root.TryGetProperty("complexity_score", out scoreProp))
            {
                if (scoreProp.ValueKind == JsonValueKind.Number)
                    score = scoreProp.GetInt32();
                else if (scoreProp.ValueKind == JsonValueKind.String && int.TryParse(scoreProp.GetString(), out int parsed))
                    score = parsed;
            }
            
            string route = score >= 5 ? "API" : "LOCAL";
            return (route, plan);
        }
        catch
        {
            return ("API", "");
        }
    }

    public async Task<string> ProcessMessageAsync(
        string userMessage, 
        Action<string> onStatusUpdate, 
        ToolStartHandler onToolStart,
        ToolEndHandler onToolEnd,
        int maxIterations = 30,
        System.Threading.CancellationToken cancellationToken = default)
    {
        Serilog.Log.Information("ProcessMessageAsync started. User message length: {Length}", userMessage.Length);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        NativeTools._fileBackups.Clear();
        _apiHistory.Add(new UserChatMessage(userMessage));
        PruneContextIfNecessary();

        // --- ROUTING PHASE ---
        string routeDecision = "API";
        CodingSahayi.Data.ModelEndpointConfig activeConfig = null;
        string currentModel = SettingsManager.ModelName;
        
        if (currentModel == "Local Model Only")
        {
            routeDecision = "LOCAL";
            activeConfig = SettingsManager.ConfiguredModels.OrderBy(m => m.Priority).FirstOrDefault(m => m.Type == CodingSahayi.Data.ModelType.Local && m.IsEnabled);
            onStatusUpdate("Using LOCAL model...");
        }
        else if (currentModel == "Hybrid Router (Auto)")
        {
            onStatusUpdate("Checking local model availability...");
            bool localAvailable = await IsLocalAvailableAsync();
            
            if (localAvailable)
            {
                onStatusUpdate("Routing task...");
                var (route, plan) = await PlanAndRouteTask(userMessage, cancellationToken);
                routeDecision = route;
                
                if (routeDecision == "LOCAL")
                {
                    activeConfig = SettingsManager.ConfiguredModels.OrderBy(m => m.Priority).FirstOrDefault(m => m.Type == CodingSahayi.Data.ModelType.Local && m.IsEnabled);
                    onStatusUpdate(!string.IsNullOrEmpty(plan) 
                        ? $"Routing via LOCAL model — {plan}" 
                        : "Routing via LOCAL model...");
                    
                    // --- MEMORY BANK INJECTION ---
                    try {
                        using var db = new CodingSahayi.Data.AppDbContext();
                        var activeProject = db.Projects.FirstOrDefault(p => p.WorkspacePath == WorkspaceDirectory);
                        if (activeProject != null) {
                            var pastLessons = db.ProjectKnowledgeBase.Where(k => k.ProjectId == activeProject.Id).Select(k => k.LearnedImplementation).ToList();
                            if (pastLessons.Any()) {
                                string appendedLessons = "\n\nProject Context & Past Lessons:\n" + string.Join("\n", pastLessons);
                                if (_apiHistory.FirstOrDefault() is SystemChatMessage sysMsg) {
                                    // Make sure we don't append it multiple times if it's already there
                                    if (!sysMsg.Content[0].Text.Contains("Project Context & Past Lessons")) {
                                        _apiHistory[0] = new SystemChatMessage(sysMsg.Content[0].Text + appendedLessons);
                                    }
                                }
                            }
                        }
                    } catch { }
                }
                else
                {
                    activeConfig = SettingsManager.ConfiguredModels.OrderBy(m => m.Priority).FirstOrDefault(m => m.Type == CodingSahayi.Data.ModelType.Cloud && m.IsEnabled);
                    onStatusUpdate(!string.IsNullOrEmpty(plan) 
                        ? $"Routing via CLOUD API — {plan}" 
                        : "Routing via CLOUD API...");
                }
            }
            else
            {
                activeConfig = SettingsManager.ConfiguredModels.OrderBy(m => m.Priority).FirstOrDefault(m => m.Type == CodingSahayi.Data.ModelType.Cloud && m.IsEnabled);
                onStatusUpdate("Local model offline, using CLOUD API...");
            }
        }
        else
        {
            onStatusUpdate("Using CLOUD API...");
            
            string displayMatch = currentModel;
            int parenIdx = currentModel.IndexOf(" (");
            if (parenIdx > 0) displayMatch = currentModel.Substring(0, parenIdx);
            
            activeConfig = SettingsManager.ConfiguredModels.FirstOrDefault(m => m.DisplayName == displayMatch) ?? 
                           SettingsManager.ConfiguredModels.OrderBy(m => m.Priority).FirstOrDefault(m => m.Type == CodingSahayi.Data.ModelType.Cloud && m.IsEnabled);
            routeDecision = "API";
        }

        if (activeConfig == null)
        {
            activeConfig = SettingsManager.ConfiguredModels.FirstOrDefault();
        }

        // --- AGENTIC LOOP ---
        int iterationCount = 0;
        bool requiresAction = true;
        string finalResponse = string.Empty;

        while (requiresAction)
        {
            if (iterationCount >= maxIterations)
            {
                finalResponse = "I've hit my iteration limit. How would you like me to proceed?";
                break;
            }
            
            iterationCount++;
            requiresAction = false;
            
            try
            {
                onStatusUpdate($"Calling model {activeConfig?.DisplayName}... (Iteration {iterationCount}/{maxIterations})");
                ChatCompletion completion = await ExecuteWithFallbackAsync(activeConfig, _apiHistory, _chatOptions, onStatusUpdate, cancellationToken);

                if (completion.FinishReason == ChatFinishReason.ToolCalls)
                {
                    _apiHistory.Add(new AssistantChatMessage(completion));
                    onStatusUpdate($"Running {completion.ToolCalls.Count} tool(s)... (Iteration {iterationCount}/{maxIterations})");

                    foreach (var toolCall in completion.ToolCalls)
                    {
                        string toolResult = string.Empty;
                        bool success = true;
                        try
                        {
                            var argsStr = toolCall.FunctionArguments.ToString();
                            onToolStart?.Invoke(toolCall.Id, toolCall.FunctionName, argsStr);

                            int retryCount = 0;
                            bool toolIsModifying = toolCall.FunctionName == "patch_file" || toolCall.FunctionName == "write_file" || toolCall.FunctionName == "batch_patch_file";
                            
                            while (retryCount < 2)
                            {
                                var args = JsonSerializer.Deserialize<JsonElement>(argsStr);
                                var execution = await ExecuteToolAsync(toolCall.FunctionName, args, cancellationToken);
                                toolResult = execution.result;
                                success = execution.success;
                                
                                if (toolIsModifying && success)
                                {
                                    string filePath = ResolvePath(args.TryGetProperty("filePath", out var fp) ? (fp.GetString() ?? "") : "");
                                    if (toolCall.FunctionName == "batch_patch_file") {
                                        if (args.TryGetProperty("patches", out var pArr) && pArr.ValueKind == JsonValueKind.Array && pArr.GetArrayLength() > 0)
                                            filePath = ResolvePath(pArr[0].GetProperty("filePath").GetString() ?? "");
                                    }
                                    
                                    if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath) && filePath.EndsWith(".cs"))
                                    {
                                        bool patchValid = await EvaluatePatchAsync(filePath, argsStr);
                                        if (!patchValid)
                                        {
                                            retryCount++;
                                            if (retryCount >= 2)
                                            {
                                                NativeTools.RollbackChanges();
                                                toolResult = $"CRITICAL FAILURE: Patch was invalid or caused syntax errors. Rolled back all changes.";
                                                success = false;
                                                finalResponse = toolResult;
                                                break;
                                            }
                                            else
                                            {
                                                string syntaxStatus = WorkspaceCodeAnalysisService.VerifySyntax(filePath);
                                                NativeTools.RollbackChanges();
                                                var retryHistory = new List<ChatMessage>(_apiHistory);
                                                retryHistory.Add(new ToolChatMessage(toolCall.Id, $"Error: The patch caused syntax errors:\n{syntaxStatus}\nPlease generate a corrected tool call."));
                                                var clientRetry = GetChatClient(activeConfig);
                                                var retryResp = await clientRetry.CompleteChatAsync(retryHistory, _chatOptions, cancellationToken);
                                                if (retryResp.Value.FinishReason == ChatFinishReason.ToolCalls && retryResp.Value.ToolCalls.Count > 0)
                                                {
                                                    argsStr = retryResp.Value.ToolCalls[0].FunctionArguments.ToString();
                                                    continue;
                                                }
                                            }
                                        }
                                    }
                                }
                                
                                // --- MEMORY BANK EXTRACTION ---
                                if (routeDecision == "API" && toolCall.FunctionName == "execute_terminal" && argsStr.Contains("dotnet build") && success && toolResult.Contains("Build succeeded"))
                                {
                                    _ = Task.Run(async () => {
                                        try {
                                            var summaryPrompt = "Summarize the architectural change you just made in 1-2 sentences. Focus on file structure and logic.";
                                            var bgHistory = new List<ChatMessage>(_apiHistory) { new UserChatMessage(summaryPrompt) };
                                            var clientBg = GetChatClient(SettingsManager.ConfiguredModels.OrderBy(m => m.Priority).FirstOrDefault(m => m.Type == CodingSahayi.Data.ModelType.Cloud && m.IsEnabled));
                                            var resp = await clientBg.CompleteChatAsync(bgHistory, new ChatCompletionOptions { AllowParallelToolCalls = false });
                                            var summaryResponse = resp.Value.Content[0].Text;
                                            
                                            using var bgDb = new CodingSahayi.Data.AppDbContext();
                                            var proj = bgDb.Projects.FirstOrDefault(p => p.WorkspacePath == WorkspaceDirectory);
                                            if (proj != null) {
                                                bgDb.ProjectKnowledgeBase.Add(new CodingSahayi.Data.ProjectKnowledge {
                                                    ProjectId = proj.Id,
                                                    TaskDescription = userMessage,
                                                    LearnedImplementation = summaryResponse,
                                                    DateLearned = DateTime.UtcNow
                                                });
                                                bgDb.SaveChanges();
                                            }
                                        } catch { }
                                    });
                                }
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            toolResult = $"Error parsing tool args: {ex.Message}";
                            success = false;
                        }

                        onToolEnd?.Invoke(toolCall.Id, toolResult, success);
                        _apiHistory.Add(new ToolChatMessage(toolCall.Id, toolResult));
                    }
                    
                    PruneContextIfNecessary();
                    requiresAction = true; 
                }
                else
                {
                    _apiHistory.Add(new AssistantChatMessage(completion));
                    finalResponse = completion.Content[0].Text;
                    
                    if (finalResponse.TrimStart().StartsWith("{") && finalResponse.Contains("\"name\"") && finalResponse.Contains("\"parameters\""))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(finalResponse);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("name", out var nameProp) && root.TryGetProperty("parameters", out var paramsProp))
                            {
                                string toolName = nameProp.GetString() ?? "";
                                string argsStr = paramsProp.ToString();
                                string toolId = "fallback_" + Guid.NewGuid().ToString().Substring(0, 8);
                                
                                onToolStart?.Invoke(toolId, toolName, argsStr);
                                var execution = await ExecuteToolAsync(toolName, paramsProp, cancellationToken);
                                onToolEnd?.Invoke(toolId, execution.result, execution.success);
                                
                                _apiHistory.Add(new UserChatMessage($"[Tool Execution Result]:\n{execution.result}"));
                                requiresAction = true;
                                continue;
                            }
                        }
                        catch { }
                    }

                    PruneContextIfNecessary();
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Error in agent loop");
                finalResponse = $"**Error:** {ex.Message}";
                requiresAction = false;
            }
        }
        
        if (NativeTools._fileBackups.Count > 0 && !finalResponse.Contains("**Error:**") && !finalResponse.Contains("hit my iteration limit"))
        {
            var diffs = string.Join("\n\n", NativeTools._fileBackups.Keys.Select(k => $"File: {k}\n(Modified)"));
            _ = Task.Run(async () => {
                try {
                    var summaryPrompt = $"Generate a concise 'Design Pattern/Implementation Lesson' summarizing these changes based on the user prompt: '{userMessage}'. Modified files:\n{diffs}";
                    var bgHistory = new List<ChatMessage>(_apiHistory) { new UserChatMessage(summaryPrompt) };
                    var clientBg = GetChatClient(SettingsManager.ConfiguredModels.OrderBy(m => m.Priority).FirstOrDefault(m => m.Type == CodingSahayi.Data.ModelType.Local && m.IsEnabled));
                    var resp = await clientBg.CompleteChatAsync(bgHistory, new ChatCompletionOptions { AllowParallelToolCalls = false });
                    var summaryResponse = resp.Value.Content[0].Text;
                    
                    using var bgDb = new CodingSahayi.Data.AppDbContext();
                    var proj = bgDb.Projects.FirstOrDefault(p => p.WorkspacePath == WorkspaceDirectory);
                    if (proj != null) {
                        bgDb.ProjectKnowledgeBase.Add(new CodingSahayi.Data.ProjectKnowledge {
                            ProjectId = proj.Id,
                            TaskDescription = userMessage,
                            LearnedImplementation = summaryResponse,
                            DateLearned = DateTime.UtcNow
                        });
                        bgDb.SaveChanges();
                    }
                } catch { }
            });
        }
        
        stopwatch.Stop();
        Serilog.Log.Information("ProcessMessageAsync completed. Final response length: {Length}. Routing: {Route}. Elapsed: {ElapsedMs}ms", finalResponse.Length, routeDecision, stopwatch.ElapsedMilliseconds);
        return finalResponse;
    }
    
    private async Task<bool> EvaluatePatchAsync(string filePath, string proposedPatch)
    {
        string syntaxStatus = WorkspaceCodeAnalysisService.VerifySyntax(filePath);
        if (syntaxStatus == "Syntax OK" || syntaxStatus.Contains("Syntax OK - Fallback")) return true;

        var systemPrompt = new SystemChatMessage("You are a code critic. The following file has syntax errors after a patch. Analyze the errors and the proposed patch. Reply with ONLY 'REJECT' if the patch is broken, or 'ACCEPT' if the error is a false positive.");
        var userPrompt = new UserChatMessage($"File: {filePath}\nErrors:\n{syntaxStatus}\nPatch:\n{proposedPatch}");
        
        try
        {
            var localConfig = SettingsManager.ConfiguredModels.OrderBy(m => m.Priority).FirstOrDefault(m => m.Type == CodingSahayi.Data.ModelType.Local && m.IsEnabled);
            var client = GetChatClient(localConfig ?? SettingsManager.ConfiguredModels.First());
            var resp = await client.CompleteChatAsync(new List<ChatMessage> { systemPrompt, userPrompt });
            if (resp.Value.Content[0].Text.Contains("REJECT")) return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<ChatCompletion> ExecuteWithFallbackAsync(CodingSahayi.Data.ModelEndpointConfig primaryConfig, List<ChatMessage> history, ChatCompletionOptions options, Action<string> onStatusUpdate, System.Threading.CancellationToken cancellationToken)
    {
        var currentConfig = primaryConfig;
        
        while (currentConfig != null)
        {
            var client = GetChatClient(currentConfig);
            int maxRetries = 3;
            for (int retry = 0; retry <= maxRetries; retry++)
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var completion = await client.CompleteChatAsync(history, options, cancellationToken);
                    stopwatch.Stop();
                    await LogApiRequestAsync(currentConfig, history, completion, stopwatch.ElapsedMilliseconds, 200);
                    return completion;
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    int statusCode = 500;
                    if (ex is System.ClientModel.ClientResultException crex)
                    {
                        statusCode = crex.Status;
                    }
                    await LogApiRequestAsync(currentConfig, history, null, stopwatch.ElapsedMilliseconds, statusCode);

                    // Honor the provider's AllowFallback setting: if fallback is disabled
                    // for this config, never route a retry or fallback through it.
                    if (!currentConfig.AllowFallback) throw;

                    if (retry < maxRetries && IsTransientError(statusCode, ex))
                    {
                        int delaySeconds = (int)Math.Pow(2, retry + 1);
                        onStatusUpdate($"Rate limited / transient error. Retrying in {delaySeconds}s... (attempt {retry + 1}/{maxRetries})");
                        await Task.Delay(delaySeconds * 1000, cancellationToken);
                    }
                    else if (retry == maxRetries || !IsTransientError(statusCode, ex))
                    {
                        if (!string.IsNullOrEmpty(currentConfig.FallbackModelId))
                        {
                            var fallbackConfig = SettingsManager.ConfiguredModels.FirstOrDefault(m => m.Id == currentConfig.FallbackModelId && m.IsEnabled);
                            if (fallbackConfig != null)
                            {
                                Serilog.Log.Warning(ex, "Primary model {ModelName} failed. Falling back to {FallbackName}", currentConfig.DisplayName, fallbackConfig.DisplayName);
                                onStatusUpdate($"Model {currentConfig.DisplayName} failed. Falling back to {fallbackConfig.DisplayName}...");
                                currentConfig = fallbackConfig;
                                break; // break retry loop to try fallback
                            }
                        }
                        throw; // No fallback or fallback failed
                    }
                }
            }
        }
        
        throw new Exception("No available models to execute the request.");
    }

    private async Task LogApiRequestAsync(CodingSahayi.Data.ModelEndpointConfig config, List<ChatMessage> history, ChatCompletion? completion, long latencyMs, int statusCode)
    {
        try
        {
            using var db = new CodingSahayi.Data.AppDbContext();
            string promptContent = "";
            if (history != null && history.Any())
            {
                var lastMsg = history.LastOrDefault(m => m is UserChatMessage);
                if (lastMsg != null && lastMsg.Content != null && lastMsg.Content.Count > 0)
                    promptContent = lastMsg.Content[0].Text;
            }

            var log = new CodingSahayi.Data.ApiRequestLog
            {
                Timestamp = DateTime.UtcNow,
                Feature = "Chat",
                Provider = config.BaseUrl.Contains("api.openai.com") ? "OpenAI" : (config.Type == CodingSahayi.Data.ModelType.Local ? "Local" : "Cloud"),
                Model = config.ModelIdentifier,
                LatencyMs = latencyMs,
                StatusCode = statusCode,
                FullPrompt = promptContent,
                PromptSnippet = promptContent.Length > 100 ? promptContent.Substring(0, 100) + "..." : promptContent
            };

            if (completion != null)
            {
                log.PromptTokens = completion.Usage?.InputTokenCount ?? 0;
                log.CompletionTokens = completion.Usage?.OutputTokenCount ?? 0;
                log.TotalTokens = completion.Usage?.TotalTokenCount ?? 0;
                if (completion.Content != null && completion.Content.Count > 0)
                {
                    log.ResponseContent = completion.Content[0].Text;
                }
                else if (completion.ToolCalls != null && completion.ToolCalls.Count > 0)
                {
                    log.ResponseContent = $"[ToolCall: {completion.ToolCalls[0].FunctionName}]";
                }
                
                if (config.Type != CodingSahayi.Data.ModelType.Local)
                {
                    // rough estimate 
                    log.EstimatedCostUsd = (log.PromptTokens * 0.00000015) + (log.CompletionTokens * 0.00000060);
                }
            }

            db.ApiRequestLogs.Add(log);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to log API request");
        }
    }

    private async Task<(string result, bool success)> ExecuteToolAsync(string toolName, JsonElement args, System.Threading.CancellationToken cancellationToken = default)
    {
        string toolResult = string.Empty;
        bool success = true;
        try
        {
            switch (toolName)
            {
                case "read_file":
                    toolResult = NativeTools.ReadFile(ResolvePath(args.GetProperty("filePath").GetString() ?? ""));
                    if (toolResult.StartsWith("Error")) success = false;
                    break;
                case "write_file":
                case "patch_file":
                    string wFilePath = ResolvePath(args.GetProperty("filePath").GetString() ?? "");
                    string newText = "";
                    if (toolName == "write_file")
                    {
                        newText = NativeTools.WriteFile(wFilePath, args.GetProperty("content").GetString() ?? "");
                    }
                    else
                    {
                        newText = NativeTools.PatchFile(
                            wFilePath, 
                            args.GetProperty("targetSnippet").GetString() ?? "", 
                            args.GetProperty("replacementSnippet").GetString() ?? "");
                        if (newText.StartsWith("Error"))
                        {
                            toolResult = newText;
                            success = false;
                            break;
                        }
                    }

                    string oldText = System.IO.File.Exists(wFilePath) ? System.IO.File.ReadAllText(wFilePath) : "";
                    
                    // Offload the diff computation off the UI thread to avoid stalling the
                    // dispatcher on large files. The dialog itself is shown on the UI thread.
                    var diff = await Task.Run(() => DiffManager.GenerateDiff(oldText, newText));
                    var tcs = new TaskCompletionSource<bool>();
                    
                    var dispatcher = (Microsoft.UI.Xaml.Application.Current as App)?._window?.DispatcherQueue;
                    if (dispatcher != null)
                    {
                        dispatcher.TryEnqueue(async () =>
                        {
                            var dialog = new DiffReviewDialog(diff);
                            dialog.XamlRoot = (Microsoft.UI.Xaml.Application.Current as App)?._window?.Content.XamlRoot;
                            await dialog.ShowAsync();
                            tcs.SetResult(dialog.IsAccepted);
                        });
                        
                        bool accepted = await tcs.Task;
                        if (accepted)
                        {
                            if (!NativeTools._fileBackups.ContainsKey(wFilePath))
                                NativeTools._fileBackups[wFilePath] = oldText;

                            var dir = System.IO.Path.GetDirectoryName(wFilePath);
                            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                            System.IO.File.WriteAllText(wFilePath, newText);
                            toolResult = $"Success: Applied changes to {wFilePath}";
                        }
                        else
                        {
                            toolResult = $"Error: User rejected the changes to {wFilePath}.";
                            success = false;
                        }
                    }
                    else
                    {
                        // Fallback if UI is not available
                        System.IO.File.WriteAllText(wFilePath, newText);
                        toolResult = $"Success: Applied changes to {wFilePath} (Auto-accepted)";
                    }
                    break;
                case "list_directory":
                    string dirPath = args.TryGetProperty("directoryPath", out var p) ? p.GetString() ?? WorkspaceDirectory : WorkspaceDirectory;
                    toolResult = NativeTools.ListDirectory(ResolvePath(dirPath), GetIntProperty(args, "maxDepth", 3));
                    if (toolResult.StartsWith("Error")) success = false;
                    break;
                case "search_code":
                    toolResult = NativeTools.SearchCode(
                        WorkspaceDirectory,
                        args.GetProperty("searchQuery").GetString() ?? "",
                        args.TryGetProperty("fileExtensionFilter", out var fe) ? (fe.GetString() ?? "*.*") : "*.*");
                    if (toolResult.StartsWith("Error")) success = false;
                    break;
                case "search_directory":
                    string sPath = args.TryGetProperty("path", out var sp) ? (sp.GetString() ?? "") : "";
                    string sPattern = args.TryGetProperty("searchPattern", out var spt) ? (spt.GetString() ?? "*") : "*";
                    toolResult = NativeTools.SearchDirectory(ResolvePath(sPath), sPattern);
                    if (toolResult.StartsWith("Error") || toolResult.StartsWith("Access denied") || toolResult.StartsWith("Directory not found")) success = false;
                    break;
                case "execute_terminal":
                    string wdPath = args.TryGetProperty("workingDirectory", out var wd) ? (wd.GetString() ?? "") : "";
                    string resolvedWd = ResolvePath(wdPath);
                    if (System.IO.File.Exists(resolvedWd)) resolvedWd = System.IO.Path.GetDirectoryName(resolvedWd) ?? "";

                    toolResult = NativeTools.ExecuteTerminalSafe(
                        args.GetProperty("command").GetString() ?? "",
                        resolvedWd,
                        GetIntProperty(args, "timeoutSeconds", 45),
                        cancellationToken);
                    if (toolResult.StartsWith("Failed") || toolResult.Contains("TIMED OUT") || toolResult.StartsWith("Cancelled")) success = false;
                    break;
                case "analyze_structure":
                    toolResult = WorkspaceCodeAnalysisService.AnalyzeStructure(ResolvePath(args.GetProperty("filePath").GetString() ?? ""));
                    if (toolResult.StartsWith("Error")) success = false;
                    break;
                case "verify_syntax":
                    toolResult = WorkspaceCodeAnalysisService.VerifySyntax(ResolvePath(args.GetProperty("filePath").GetString() ?? ""));
                    if (toolResult.StartsWith("Error")) success = false;
                    break;
                case "resolve_symbol":
                    toolResult = WorkspaceCodeAnalysisService.ResolveSymbol(
                        ResolvePath(args.GetProperty("filePath").GetString() ?? ""),
                        args.GetProperty("symbolName").GetString() ?? "");
                    if (toolResult.StartsWith("Error")) success = false;
                    break;
                case "semantic_code_search":
                    toolResult = NativeTools.SemanticCodeSearch(args.GetProperty("query").GetString() ?? "");
                    if (toolResult.StartsWith("Error")) success = false;
                    break;
                case "run_tests":
                    toolResult = await TestRunnerTool.RunTestsAsync(ResolvePath(args.GetProperty("projectPath").GetString() ?? ""));
                    if (toolResult.Contains("Test Failures Detected") || toolResult.StartsWith("Error")) success = false;
                    break;
                case "train_local_model":
                    string targetWorkspace = !string.IsNullOrWhiteSpace(WorkspaceDirectory) ? WorkspaceDirectory :
                        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodingSahayi", "FineTuning");

                    // Prepare artifacts (dataset + soup.yaml) and resolve the Soup executable.
                    var (prepOk, exePath, configPath, prepMsg) =
                        await ModelTrainerTool.PreparePipelineAsync(targetWorkspace, null);

                    if (!prepOk)
                    {
                        toolResult = prepMsg;
                        success = false;
                        break;
                    }

                    var trainBuffer = new System.Text.StringBuilder();
                    bool trainOk = await ModelTrainerTool.RunSoupTrainingAsync(
                        exePath,
                        configPath,
                        line => trainBuffer.AppendLine(line));

                    var metrics = ModelTrainerTool.ParseMetrics(trainBuffer.ToString());
                    toolResult = trainOk
                        ? $"✅ Training complete. Captured {metrics.Count} loss data-points."
                        : $"❌ Training failed. Captured {metrics.Count} loss data-points.";
                    if (metrics.Count > 0)
                        toolResult += $" Final loss @ step {metrics[^1].Step}: {metrics[^1].Loss:F4}";
                    if (!trainOk)
                        success = false;
                    break;
                default:
                    if (toolName == "batch_patch_file")
                    {
                        var sb = new System.Text.StringBuilder();
                        int successCount = 0, failCount = 0;
                        if (args.TryGetProperty("patches", out var patches) && patches.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var patch in patches.EnumerateArray())
                            {
                                string pFile = ResolvePath(patch.GetProperty("filePath").GetString() ?? "");
                                string pTarget = patch.GetProperty("targetSnippet").GetString() ?? "";
                                string pReplace = patch.GetProperty("replacementSnippet").GetString() ?? "";
                                string pResult = NativeTools.PatchFile(pFile, pTarget, pReplace);
                                sb.AppendLine($"[{System.IO.Path.GetFileName(pFile)}]: {pResult}");
                                if (pResult.StartsWith("Error")) failCount++; else successCount++;
                            }
                        }
                        toolResult = $"Batch complete: {successCount} succeeded, {failCount} failed.\n{sb}";
                        if (failCount > 0) success = false;
                    }
                    else
                    {
                        toolResult = $"Unknown tool: {toolName}";
                        success = false;
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            toolResult = $"Error executing tool: {ex.Message}";
            success = false;
        }
        return (toolResult, success);
    }
    
    private void PruneContextIfNecessary()
    {
        if (_apiHistory.Count <= MaxContextMessages) return;

        var systemMessage = _apiHistory.FirstOrDefault();
        
        while (_apiHistory.Count > MaxContextMessages * 0.8)
        {
            if (_apiHistory.Count > 1)
            {
                _apiHistory.RemoveAt(1);
            }
        }
    }

    private static bool IsTransientError(int statusCode, Exception ex)
    {
        // Prefer the structured HTTP status when available (ClientResultException).
        if (statusCode != 0)
        {
            // 429 Too Many Requests, 529 Overloaded, and transient 5xx (500-504) are retryable.
            if (statusCode is 429 or 529) return true;
            if (statusCode >= 500 && statusCode <= 504) return true;
            return false;
        }

        // Fallback: string-match the exception message when no status code is available.
        var message = ex.Message ?? "";
        return message.Contains("429")
            || message.Contains("529")
            || message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
            || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || message.Contains("overloaded", StringComparison.OrdinalIgnoreCase)
            || (message.Contains("500") || message.Contains("502")
                || message.Contains("503") || message.Contains("504"));
    }

    private string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return WorkspaceDirectory;
        return System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(WorkspaceDirectory, path);
    }

    private int GetIntProperty(JsonElement element, string propertyName, int defaultValue)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int num)) return num;
            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out int strNum)) return strNum;
        }
        return defaultValue;
    }
}
